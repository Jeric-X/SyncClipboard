using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateInstaller(IEnumerable<IUpdateInstallStrategy> strategies, UpdateInstallCleanup cleanup) : IUpdateInstaller
{
    private readonly Dictionary<UpdatePackageKind, IUpdateInstallStrategy> strategies = strategies.ToDictionary(s => s.Kind);

    public UpdateInstallCapability GetCapability(UpdateInfoConfig updateInfo)
    {
        if (updateInfo.ManageType != UpdateInfoConfig.TypeManual || updateInfo.UpdateSrc != "github")
        {
            return new(UpdatePackageKind.Unsupported, string.Empty);
        }
        var kind = GetPackageKind(updateInfo.PackageName, OperatingSystem.IsWindows());
        return strategies.TryGetValue(kind, out var strategy)
            ? strategy.GetCapability() : new(UpdatePackageKind.Unsupported, string.Empty);
    }

    public static UpdatePackageKind GetPackageKind(string name, bool windows)
    {
        if (Path.GetFileName(name) != name || name.Contains('\\')) return UpdatePackageKind.Unsupported;
        if (windows && name.EndsWith("_portable.zip", StringComparison.OrdinalIgnoreCase)) return UpdatePackageKind.WindowsPortable;
        return UpdatePackageKind.Unsupported;
    }

    public Task<PreparedUpdate> PrepareAsync(UpdateInstallRequest request, CancellationToken token)
        => Task.Run(async () =>
        {
            var strategy = GetStrategy(request.Capability.Kind);
            await UpdateInstallFiles.VerifyHashAsync(request.PackagePath, request.Digest, token);
            return await strategy.PrepareAsync(request, token);
        }, token);

    public Task StartAsync(PreparedUpdate update, CancellationToken token)
    {
        if (!Enum.TryParse<UpdatePackageKind>(update.Kind, out var kind))
        {
            throw new InvalidOperationException(I18n.Strings.UpdateLocationUnsupported);
        }
        return GetStrategy(kind).StartAsync(update, token);
    }

    private IUpdateInstallStrategy GetStrategy(UpdatePackageKind kind) => strategies.TryGetValue(kind, out var strategy)
        ? strategy : throw new InvalidOperationException(I18n.Strings.UpdateLocationUnsupported);

    public Task CleanupCompletedAsync() => cleanup.CleanupCompletedAsync();
}
