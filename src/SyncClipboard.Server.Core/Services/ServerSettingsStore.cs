using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SyncClipboard.Server.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SyncClipboard.Server.Core.Services;

public sealed record AccountSummary(string Id, string UserName);
public sealed record AccountUpdate(string UserName, string? Password);

public sealed class ServerSettingsStore : IOptions<AppSettings>
{
    public const string FileName = "server-settings.json";
    // The original single account owns pre-upgrade records and administers the instance.
    public const string AdministratorId = "default_user";

    private sealed record Account(string? Id, string UserName, string? PasswordHash, string? Password = null)
    {
        [JsonIgnore] public string SessionId { get; } = Guid.NewGuid().ToString("N");
        [JsonIgnore] public CancellationTokenSource Revoked { get; } = new();
    }
    private sealed record Settings(AppSettings History, Account[] Users);
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly PasswordHasher<string> _hasher = new();
    private readonly Lock _gate = new();
    private readonly string _path;
    private volatile Settings _settings;

    public AppSettings Value => _settings.History;

    public ServerSettingsStore(string directory, AppSettings history, AccountUpdate? bootstrap)
        : this(directory, history, bootstrap, requireLogin: true) { }

    public static void Migrate(string directory, AppSettings history, AccountUpdate? account) =>
        _ = new ServerSettingsStore(directory, history, account, requireLogin: false);

    private ServerSettingsStore(string directory, AppSettings history, AccountUpdate? bootstrap, bool requireLogin)
    {
        _path = Path.Combine(directory, FileName);
        var exists = File.Exists(_path);
        _settings = exists
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path), Json) ?? throw new InvalidDataException(_path)
            : new(history, []);
        if (_settings.Users is null)
            throw new InvalidDataException($"{_path} requires a Users array.");
        var accounts = _settings.Users.ToArray();
        var rewrite = !exists || _settings.History is null;
        if (accounts.Length == 0 && bootstrap is not null)
        {
            accounts = [new(AdministratorId, bootstrap.UserName, null, bootstrap.Password)];
            rewrite = true;
        }
        var hasAdmin = accounts.Any(a => a.Id == AdministratorId);
        for (var i = 0; i < accounts.Length; i++)
        {
            var account = accounts[i];
            var id = account.Id ?? (i == 0 && !hasAdmin ? AdministratorId : Guid.NewGuid().ToString("N"));
            if (id != AdministratorId && !(Guid.TryParseExact(id, "N", out var guid) && guid.ToString("N") == id))
                throw new InvalidDataException("Invalid account ID.");
            ValidateName(account.UserName);
            var hash = account.Password is null ? account.PasswordHash : HashPassword(id, account.Password);
            if (string.IsNullOrEmpty(hash))
                throw new InvalidDataException("Each account requires a password.");
            accounts[i] = new(id, account.UserName, hash);
            rewrite |= account.Id is null || account.Password is not null;
        }
        if (accounts.DistinctBy(a => a.Id).Count() != accounts.Length
            || accounts.DistinctBy(a => a.UserName).Count() != accounts.Length)
            throw new InvalidDataException("Duplicate account ID or username.");
        if (accounts.Length > 0 && accounts.All(a => a.Id != AdministratorId))
            throw new InvalidDataException($"One account must have ID '{AdministratorId}'.");
        _settings = new(_settings.History ?? new(), accounts);
        if (rewrite)
            Save(_settings, exists);
        if (requireLogin && accounts.Length == 0)
            throw new InvalidDataException($"Add an initial account with UserName and Password to Users in {_path}.");
    }

    public (string Id, string SessionId)? Authenticate(string name, string password)
    {
        var account = _settings.Users.FirstOrDefault(a => a.UserName == name);
        return account is not null && _hasher.VerifyHashedPassword(account.Id!, account.PasswordHash!, password)
            != PasswordVerificationResult.Failed ? (account.Id!, account.SessionId) : null;
    }

    public CancellationToken GetSessionRevocation(string? sessionId) =>
        _settings.Users.FirstOrDefault(a => a.SessionId == sessionId)?.Revoked.Token ?? new CancellationToken(true);

    public IEnumerable<AccountSummary> ListAccounts() => _settings.Users.Select(a => new AccountSummary(a.Id!, a.UserName));

    public string SetAccount(string? id, AccountUpdate update)
    {
        ValidateName(update.UserName);
        lock (_gate)
        {
            var users = _settings.Users.ToList();
            var index = users.FindIndex(a => a.Id == id);
            if (id is not null && index < 0)
                throw new KeyNotFoundException();
            if (users.Any(a => a.Id != id && a.UserName == update.UserName))
                throw new ArgumentException("Username is already in use.");
            if (index >= 0 && update.Password is null && users[index].UserName == update.UserName)
                return users[index].Id!;
            id ??= Guid.NewGuid().ToString("N");
            var hash = index >= 0 && update.Password is null
                ? users[index].PasswordHash : HashPassword(id, update.Password);
            var account = new Account(id, update.UserName, hash);
            var previous = index >= 0 ? users[index] : null;
            if (index >= 0)
                users[index] = account;
            else
                users.Add(account);
            Save(_settings with { Users = [.. users] });
            previous?.Revoked.Cancel();
            return id;
        }
    }

    public bool RemoveAccount(string id)
    {
        if (id == AdministratorId)
            throw new ArgumentException("The administrator account cannot be removed.");
        lock (_gate)
        {
            var previous = _settings.Users.FirstOrDefault(a => a.Id == id);
            var remaining = _settings.Users.Where(a => a.Id != id).ToArray();
            if (remaining.Length == _settings.Users.Length)
                return false;
            Save(_settings with { Users = remaining });
            previous!.Revoked.Cancel();
            return true;
        }
    }

    public void SetHistory(AppSettings history)
    {
        lock (_gate)
            Save(_settings with { History = history });
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains(':'))
            throw new ArgumentException("A username is required and cannot contain a colon.");
    }

    private string HashPassword(string id, string? password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(id, password);
    }

    private void Save(Settings next, bool overwrite = true)
    {
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using (var file = new FileStream(temp, options))
            {
                JsonSerializer.Serialize(file, next, Json);
                file.Flush(true);
            }
            File.Move(temp, _path, overwrite);
            _settings = next;
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
