using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Win32;
using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Utilities;

internal sealed record WindowsStartupTaskInfo(
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    int ActionCount,
    string PrincipalUserId,
    int PrincipalLogonType,
    string TriggerUserId,
    int TriggerCount,
    int RunLevel,
    bool Enabled,
    bool TriggerEnabled,
    bool DisallowStartIfOnBatteries,
    bool StopIfGoingOnBatteries,
    string ExecutionTimeLimit);

internal sealed record WindowsStartupRegistryEntry(
    string Value,
    RegistryValueKind ValueKind);

internal enum WindowsTaskLookupStatus
{
    NotFound,
    Found,
    Error,
}

public class StartUpHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const int TaskRunLevelLeastPrivilege = 0;
    private const int TaskRunLevelHighest = 1;
    private const int FileNotFoundHResult = unchecked((int)0x80070002);
    private const int PathNotFoundHResult = unchecked((int)0x80070003);
    private const int TaskNotFoundHResult = unchecked((int)0x8004130F);
    private static readonly XNamespace TaskXmlNamespace =
        "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private const string WindowsStartupMigrationScript = """
        $ErrorActionPreference = 'Stop'
        $manifest = $ManifestJson | ConvertFrom-Json
        $taskAttempted = $false
        $legacyDeleteAttempted = $false
        $registryDeleted = $false
        $schtasks = Join-Path $env:SystemRoot 'System32\schtasks.exe'
        $tempDirectory = Join-Path (
            [System.IO.Path]::GetTempPath()) (
            'SyncClipboard-startup-' + [Guid]::NewGuid().ToString('N'))
        [System.IO.Directory]::CreateDirectory($tempDirectory) | Out-Null

        function Write-TaskXml {
            param(
                [AllowNull()][string]$Xml,
                [string]$FileName
            )

            if ([string]::IsNullOrEmpty($Xml)) {
                return $null
            }

            $path = Join-Path $tempDirectory $FileName
            [System.IO.File]::WriteAllText(
                $path,
                $Xml,
                [System.Text.UTF8Encoding]::new($false))
            return $path
        }

        function Remove-TransactionFiles {
            try {
                [System.IO.Directory]::Delete($tempDirectory, $true)
            }
            catch {
            }
        }

        $newTaskXmlPath = Write-TaskXml $manifest.NewTaskXml 'new-task.xml'
        $previousTaskXmlPath = Write-TaskXml $manifest.PreviousTaskXml 'previous-task.xml'
        $legacyTaskXmlPath = Write-TaskXml $manifest.LegacyTaskXml 'legacy-task.xml'

        function Invoke-TaskCommand {
            param([string[]]$Arguments)

            & $schtasks @Arguments | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "schtasks $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
            }
        }

        function Test-PathEqual {
            param([string]$First, [string]$Second)

            try {
                return [string]::Equals(
                    [System.IO.Path]::GetFullPath($First),
                    [System.IO.Path]::GetFullPath($Second),
                    [System.StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                return $false
            }
        }

        function Normalize-UserId {
            param([string]$UserId)

            if ($UserId.StartsWith('S-', [System.StringComparison]::OrdinalIgnoreCase)) {
                return $UserId
            }

            try {
                $account = [System.Security.Principal.NTAccount]::new($UserId)
                return $account.Translate(
                    [System.Security.Principal.SecurityIdentifier]).Value
            }
            catch {
                try {
                    $account = [System.Security.Principal.NTAccount]::new(
                        $env:USERDOMAIN,
                        $UserId)
                    return $account.Translate(
                        [System.Security.Principal.SecurityIdentifier]).Value
                }
                catch {
                    return $UserId
                }
            }
        }

        function Assert-NewTask {
            $scheduler = New-Object -ComObject 'Schedule.Service'
            $scheduler.Connect()
            $task = $scheduler.GetFolder('\').GetTask([string]$manifest.NewTaskName)
            $definition = $task.Definition

            if ($definition.Actions.Count -ne 1 -or $definition.Triggers.Count -ne 1) {
                throw 'The startup task has an unexpected action or trigger count.'
            }

            $action = $definition.Actions.Item(1)
            $trigger = $definition.Triggers.Item(1)
            $principalUserId = Normalize-UserId ([string]$definition.Principal.UserId)
            $triggerUserId = Normalize-UserId ([string]$trigger.UserId)

            if ([int]$action.Type -ne 0 -or
                -not (Test-PathEqual ([string]$action.Path) ([string]$manifest.ProgramPath)) -or
                -not [string]::IsNullOrEmpty([string]$action.Arguments) -or
                -not (Test-PathEqual ([string]$action.WorkingDirectory) ([string]$manifest.ProgramDirectory)) -or
                -not [string]::Equals($principalUserId, [string]$manifest.UserId, [System.StringComparison]::OrdinalIgnoreCase) -or
                [int]$definition.Principal.LogonType -ne 3 -or
                [int]$definition.Principal.RunLevel -ne [int]$manifest.RunLevel -or
                [int]$trigger.Type -ne 9 -or
                -not [string]::Equals($triggerUserId, [string]$manifest.UserId, [System.StringComparison]::OrdinalIgnoreCase) -or
                -not [bool]$task.Enabled -or
                -not [bool]$trigger.Enabled -or
                [bool]$definition.Settings.DisallowStartIfOnBatteries -or
                [bool]$definition.Settings.StopIfGoingOnBatteries -or
                [string]$definition.Settings.ExecutionTimeLimit -ne 'PT0S') {
                throw 'The startup task does not match the requested definition.'
            }
        }

        function Set-TaskSecurityDescriptor {
            param(
                [string]$TaskName,
                [AllowNull()][string]$SecurityDescriptor
            )

            if ([string]::IsNullOrEmpty($SecurityDescriptor)) {
                return
            }

            $scheduler = New-Object -ComObject 'Schedule.Service'
            $scheduler.Connect()
            $task = $scheduler.GetFolder('\').GetTask($TaskName)
            $task.SetSecurityDescriptor($SecurityDescriptor, 0x10)
        }

        try {
            if ($null -ne $newTaskXmlPath) {
                $taskAttempted = $true
                Invoke-TaskCommand @(
                    '/Create',
                    '/TN',
                    [string]$manifest.NewTaskName,
                    '/XML',
                    [string]$newTaskXmlPath,
                    '/F')
                Set-TaskSecurityDescriptor (
                    [string]$manifest.NewTaskName) (
                    [string]$manifest.NewTaskSecurityDescriptor)
                Assert-NewTask
            }
            elseif ([bool]$manifest.DeleteNewTask) {
                $taskAttempted = $true
                Invoke-TaskCommand @(
                    '/Delete',
                    '/TN',
                    [string]$manifest.NewTaskName,
                    '/F')
            }

            if ($null -ne $manifest.LegacyTaskName) {
                $legacyDeleteAttempted = $true
                Invoke-TaskCommand @(
                    '/Delete',
                    '/TN',
                    [string]$manifest.LegacyTaskName,
                    '/F')
            }

            if ($null -ne $manifest.RegistryValue) {
                $registryPath = [string]$manifest.UserId + '\' + [string]$manifest.RunKeyPath
                $key = [Microsoft.Win32.Registry]::Users.OpenSubKey(
                    $registryPath,
                    $true)
                try {
                    if ($null -ne $key) {
                        $currentValue = [string]$key.GetValue(
                            [string]$manifest.RegistryValueName)
                        if ([string]::Equals(
                            $currentValue,
                            [string]$manifest.RegistryValue,
                            [System.StringComparison]::Ordinal)) {
                            $key.DeleteValue(
                                [string]$manifest.RegistryValueName,
                                $false)
                            $registryDeleted = $true
                        }
                    }
                }
                finally {
                    if ($null -ne $key) {
                        $key.Dispose()
                    }
                }
            }

            Remove-TransactionFiles
            exit 0
        }
        catch {
            $transactionError = $_

            if ($registryDeleted) {
                try {
                    $registryPath = [string]$manifest.UserId + '\' + [string]$manifest.RunKeyPath
                    $key = [Microsoft.Win32.Registry]::Users.CreateSubKey(
                        $registryPath)
                    try {
                        $key.SetValue(
                            [string]$manifest.RegistryValueName,
                            [string]$manifest.RegistryValue,
                            [Microsoft.Win32.RegistryValueKind][int]$manifest.RegistryValueKind)
                    }
                    finally {
                        $key.Dispose()
                    }
                }
                catch {
                }
            }

            if ($legacyDeleteAttempted -and $null -ne $legacyTaskXmlPath) {
                try {
                    Invoke-TaskCommand @(
                        '/Create',
                        '/TN',
                        [string]$manifest.LegacyTaskName,
                        '/XML',
                        [string]$legacyTaskXmlPath,
                        '/F')
                    Set-TaskSecurityDescriptor (
                        [string]$manifest.LegacyTaskName) (
                        [string]$manifest.LegacyTaskSecurityDescriptor)
                }
                catch {
                }
            }

            if ($taskAttempted) {
                try {
                    if ($null -ne $previousTaskXmlPath) {
                        Invoke-TaskCommand @(
                            '/Create',
                            '/TN',
                            [string]$manifest.NewTaskName,
                            '/XML',
                            [string]$previousTaskXmlPath,
                            '/F')
                        Set-TaskSecurityDescriptor (
                            [string]$manifest.NewTaskName) (
                            [string]$manifest.PreviousTaskSecurityDescriptor)
                    }
                    else {
                        & $schtasks /Delete /TN ([string]$manifest.NewTaskName) /F | Out-Null
                    }
                }
                catch {
                }
            }

            Remove-TransactionFiles
            Write-Output ([string]$transactionError)
            exit 1
        }
        """;

    private sealed record WindowsStartupMigrationManifest(
        string NewTaskName,
        string? NewTaskXml,
        string? NewTaskSecurityDescriptor,
        bool DeleteNewTask,
        string? PreviousTaskXml,
        string? PreviousTaskSecurityDescriptor,
        string? LegacyTaskName,
        string? LegacyTaskXml,
        string? LegacyTaskSecurityDescriptor,
        string? RegistryValue,
        int? RegistryValueKind,
        string RunKeyPath,
        string RegistryValueName,
        string UserId,
        int RunLevel,
        string ProgramPath,
        string ProgramDirectory);

    internal static string GetWindowsTaskName(string appName, string userId) => $"{appName}-{userId}";

    [SupportedOSPlatform("windows")]
    internal static string NormalizeWindowsUserId(string userId)
    {
        if (userId.StartsWith("S-", StringComparison.OrdinalIgnoreCase))
        {
            return userId;
        }

        try
        {
            var account = new NTAccount(userId);
            return ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
        }
        catch
        {
            try
            {
                var account = new NTAccount(Environment.UserDomainName, userId);
                return ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
            }
            catch
            {
                return userId;
            }
        }
    }

    internal static string BuildWindowsTaskXml(
        string userId,
        bool runAsAdmin,
        string programPath,
        string programDirectory)
    {
        var runLevel = runAsAdmin ? "HighestAvailable" : "LeastPrivilege";
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(TaskXmlNamespace + "Task",
                new XAttribute("version", "1.2"),
                runAsAdmin
                    ? null
                    : new XElement(TaskXmlNamespace + "RegistrationInfo",
                        new XElement(
                            TaskXmlNamespace + "SecurityDescriptor",
                            BuildWindowsTaskSecurityDescriptor(userId))),
                new XElement(TaskXmlNamespace + "Triggers",
                    new XElement(TaskXmlNamespace + "LogonTrigger",
                        new XElement(TaskXmlNamespace + "Enabled", true),
                        new XElement(TaskXmlNamespace + "UserId", userId))),
                new XElement(TaskXmlNamespace + "Principals",
                    new XElement(TaskXmlNamespace + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(TaskXmlNamespace + "UserId", userId),
                        new XElement(TaskXmlNamespace + "LogonType", "InteractiveToken"),
                        new XElement(TaskXmlNamespace + "RunLevel", runLevel))),
                new XElement(TaskXmlNamespace + "Settings",
                    new XElement(TaskXmlNamespace + "DisallowStartIfOnBatteries", false),
                    new XElement(TaskXmlNamespace + "StopIfGoingOnBatteries", false),
                    new XElement(TaskXmlNamespace + "Enabled", true),
                    new XElement(TaskXmlNamespace + "ExecutionTimeLimit", "PT0S")),
                new XElement(TaskXmlNamespace + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(TaskXmlNamespace + "Exec",
                        new XElement(TaskXmlNamespace + "Command", programPath),
                        new XElement(TaskXmlNamespace + "WorkingDirectory", programDirectory)))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    internal static string BuildWindowsTaskSecurityDescriptor(string userId) =>
        $"D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;{userId})";

    internal static bool IsExpectedWindowsTask(
        WindowsStartupTaskInfo? task,
        string userId,
        bool? runAsAdmin,
        string programPath,
        string programDirectory)
    {
        if (task is null
            || !PathsEqual(task.ExecutablePath, programPath)
            || !string.IsNullOrEmpty(task.Arguments)
            || !PathsEqual(task.WorkingDirectory, programDirectory)
            || task.ActionCount != 1
            || !string.Equals(task.PrincipalUserId, userId, StringComparison.OrdinalIgnoreCase)
            || task.PrincipalLogonType != 3
            || !string.Equals(task.TriggerUserId, userId, StringComparison.OrdinalIgnoreCase)
            || task.TriggerCount != 1
            || !task.Enabled
            || !task.TriggerEnabled
            || task.DisallowStartIfOnBatteries
            || task.StopIfGoingOnBatteries
            || task.ExecutionTimeLimit != "PT0S"
            || task.RunLevel is not TaskRunLevelLeastPrivilege and not TaskRunLevelHighest)
        {
            return false;
        }

        return runAsAdmin is null
            || task.RunLevel == (runAsAdmin.Value ? TaskRunLevelHighest : TaskRunLevelLeastPrivilege);
    }

    internal static bool NeedsWindowsStartupMigrationElevation(
        bool runAsAdmin,
        WindowsStartupTaskInfo? currentTask,
        WindowsStartupTaskInfo? legacyTask)
    {
        return runAsAdmin
            || currentTask?.RunLevel == TaskRunLevelHighest
            || legacyTask?.RunLevel == TaskRunLevelHighest;
    }

    internal static bool CanAutomaticallyMigrateWindowsStartup(
        WindowsTaskLookupStatus newTaskStatus,
        WindowsTaskLookupStatus legacyTaskStatus,
        bool legacyTaskIsExpectedAndUnprivileged,
        bool registryEntryExists)
    {
        return newTaskStatus == WindowsTaskLookupStatus.NotFound
            && legacyTaskStatus != WindowsTaskLookupStatus.Error
            && (legacyTaskStatus == WindowsTaskLookupStatus.Found
                ? legacyTaskIsExpectedAndUnprivileged
                : registryEntryExists);
    }

    internal static bool CanCurrentWindowsUserRunAsAdmin()
    {
        return OperatingSystem.IsWindows()
            && CurrentWindowsUserBelongsToAdministratorsGroup();
    }

    [SupportedOSPlatform("windows")]
    private static bool CurrentWindowsUserBelongsToAdministratorsGroup()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return ContainsBuiltinAdministratorsSid(
                identity.Groups?.Select(group => group.Value)
                    ?? []);
        }
        catch
        {
            return false;
        }
    }

    internal static bool ContainsBuiltinAdministratorsSid(
        IEnumerable<string> groupIds)
    {
        const string builtinAdministratorsSid = "S-1-5-32-544";
        return groupIds.Contains(
            builtinAdministratorsSid,
            StringComparer.OrdinalIgnoreCase);
    }

    internal static bool IsExpectedLegacyWindowsTask(
        WindowsStartupTaskInfo? task,
        string userId,
        string userName,
        string programPath,
        string programDirectory)
    {
        return task is not null
            && PathsEqual(task.ExecutablePath, programPath)
            && string.IsNullOrEmpty(task.Arguments)
            && PathsEqual(task.WorkingDirectory, programDirectory)
            && task.ActionCount == 1
            && (string.Equals(task.PrincipalUserId, userId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(task.PrincipalUserId, userName, StringComparison.OrdinalIgnoreCase))
            && task.PrincipalLogonType == 3
            && (string.IsNullOrEmpty(task.TriggerUserId)
                || string.Equals(task.TriggerUserId, userId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(task.TriggerUserId, userName, StringComparison.OrdinalIgnoreCase))
            && task.TriggerCount == 1
            && task.RunLevel is TaskRunLevelLeastPrivilege or TaskRunLevelHighest
            && task.Enabled
            && task.TriggerEnabled
            && !task.DisallowStartIfOnBatteries
            && !task.StopIfGoingOnBatteries
            && task.ExecutionTimeLimit == "PT0S";
    }

    internal static string BuildElevatedPowerShellCommand(
        string manifestJson,
        string script)
    {
        var payload = CompressToBase64(manifestJson);
        var bootstrap = $$"""
            $payloadBytes = [Convert]::FromBase64String('{{payload}}')
            $payloadStream = [IO.MemoryStream]::new([byte[]]$payloadBytes)
            $gzipStream = [IO.Compression.GZipStream]::new(
                $payloadStream,
                [IO.Compression.CompressionMode]::Decompress)
            $reader = [IO.StreamReader]::new(
                $gzipStream,
                [Text.UTF8Encoding]::new($false))
            try {
                $ManifestJson = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
                $gzipStream.Dispose()
                $payloadStream.Dispose()
            }
            """;
        return $"{bootstrap.TrimEnd()}{Environment.NewLine}{script}";
    }

    private static string CompressToBase64(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        using var compressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(
                   compressedStream,
                   CompressionLevel.SmallestSize,
                   leaveOpen: true))
        {
            gzipStream.Write(bytes);
        }

        return Convert.ToBase64String(compressedStream.ToArray());
    }

    internal static ProcessStartInfo CreateElevatedPowerShellStartInfo(string command)
    {
        var payload = CompressToBase64(command);
        var bootstrap = $$"""
            $commandBytes = [Convert]::FromBase64String('{{payload}}')
            $commandStream = [IO.MemoryStream]::new([byte[]]$commandBytes)
            $gzipStream = [IO.Compression.GZipStream]::new(
                $commandStream,
                [IO.Compression.CompressionMode]::Decompress)
            $reader = [IO.StreamReader]::new(
                $gzipStream,
                [Text.UTF8Encoding]::new($false))
            try {
                $command = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
                $gzipStream.Dispose()
                $commandStream.Dispose()
            }
            & ([ScriptBlock]::Create($command))
            """;
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            Verb = "runas",
        };

        foreach (var argument in new[]
                 {
                     "-NoProfile",
                     "-NonInteractive",
                     "-ExecutionPolicy",
                     "Bypass",
                     "-EncodedCommand",
                     Convert.ToBase64String(
                         Encoding.Unicode.GetBytes(bootstrap)),
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static ProcessStartInfo CreateSchtasksStartInfo(
        IEnumerable<string> arguments,
        bool elevate)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            UseShellExecute = elevate,
            CreateNoWindow = !elevate,
            WindowStyle = ProcessWindowStyle.Hidden,
            Verb = elevate ? "runas" : string.Empty,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public static bool Status()
    {
        if (OperatingSystem.IsOSPlatform("windows"))
        {
            TryMigrateLegacyWindowsStartupWithoutElevation();
            return CheckWindows();
        }
        else if (OperatingSystem.IsOSPlatform("linux"))
        {
            return CheckLinux();
        }
        else
        {
            throw new PlatformNotSupportedException("This method is only supported on Windows and Linux.");
        }
    }

    // Single-parameter overload for binary compatibility with existing compiled callers
    public static void Set(bool enable) => Set(enable, runAsAdmin: false);

    public static void Set(bool enable, bool runAsAdmin) => TrySet(enable, runAsAdmin);

    public static bool TrySet(bool enable, bool runAsAdmin)
    {
        if (OperatingSystem.IsOSPlatform("windows"))
        {
            return TrySetWindows(enable, runAsAdmin);
        }
        else if (OperatingSystem.IsOSPlatform("linux"))
        {
            SetLinux(enable);
            return CheckLinux() == enable;
        }
        else
        {
            throw new PlatformNotSupportedException("This method is only supported on Windows and Linux.");
        }
    }

    public static bool TryGetRunAsAdmin(out bool runAsAdmin)
    {
        runAsAdmin = false;
        return OperatingSystem.IsOSPlatform("windows")
            && TryGetWindowsRunAsAdmin(out runAsAdmin);
    }

    [SupportedOSPlatform("windows")]
    private static bool TrySetWindows(bool enable, bool runAsAdmin)
    {
        try
        {
            if (enable
                && runAsAdmin
                && !CanCurrentWindowsUserRunAsAdmin())
            {
                return false;
            }

            var (userId, userName) = GetCurrentWindowsUser();
            return enable
                ? TryEnableWindowsStartup(userId, userName, runAsAdmin)
                : TryDisableWindowsStartup(userId, userName);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void TryMigrateLegacyWindowsStartupWithoutElevation()
    {
        try
        {
            var (userId, userName) = GetCurrentWindowsUser();
            var taskName = GetWindowsTaskName(Env.SoftName, userId);
            var newTaskStatus = GetWindowsTaskLookupStatus(taskName);
            var legacyTaskStatus = GetWindowsTaskLookupStatus(Env.SoftName);
            var legacyTask = legacyTaskStatus == WindowsTaskLookupStatus.Found
                ? GetWindowsTaskInfo(Env.SoftName)
                : null;
            var canMigrateLegacyTask = IsLegacyTaskForCurrentUser(
                    legacyTask,
                    userId,
                    userName)
                && legacyTask!.RunLevel == TaskRunLevelLeastPrivilege;
            var registryEntryExists = GetLegacyRegistryEntry() is not null;
            if (!CanAutomaticallyMigrateWindowsStartup(
                    newTaskStatus,
                    legacyTaskStatus,
                    canMigrateLegacyTask,
                    registryEntryExists))
            {
                return;
            }

            TryEnableWindowsStartup(userId, userName, runAsAdmin: false);
        }
        catch
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryEnableWindowsStartup(string userId, string userName, bool runAsAdmin)
    {
        var taskName = GetWindowsTaskName(Env.SoftName, userId);
        var previousTaskStatus = GetWindowsTaskLookupStatus(taskName);
        var previousTaskXml = previousTaskStatus == WindowsTaskLookupStatus.Found
            ? GetWindowsTaskXml(taskName)
            : null;
        var previousTaskSecurityDescriptor =
            previousTaskStatus == WindowsTaskLookupStatus.Found
                ? GetWindowsTaskSecurityDescriptor(taskName)
                : null;
        var previousTask = previousTaskStatus == WindowsTaskLookupStatus.Found
            ? GetWindowsTaskInfo(taskName)
            : null;
        var legacyTaskStatus = GetWindowsTaskLookupStatus(Env.SoftName);
        var legacyTaskCandidate = legacyTaskStatus == WindowsTaskLookupStatus.Found
            ? GetWindowsTaskInfo(Env.SoftName)
            : null;
        var legacyTask = IsLegacyTaskForCurrentUser(legacyTaskCandidate, userId, userName)
            ? legacyTaskCandidate
            : null;
        var legacyTaskXml = legacyTask is null
            ? null
            : GetWindowsTaskXml(Env.SoftName);
        var legacyTaskSecurityDescriptor = legacyTask is null
            ? null
            : GetWindowsTaskSecurityDescriptor(Env.SoftName);
        var registryEntry = GetLegacyRegistryEntry();

        if (previousTaskStatus == WindowsTaskLookupStatus.Error
            || legacyTaskStatus == WindowsTaskLookupStatus.Error
            || (previousTaskStatus == WindowsTaskLookupStatus.Found
                && (previousTaskXml is null
                    || previousTaskSecurityDescriptor is null))
            || (legacyTask is not null
                && (legacyTaskXml is null
                    || legacyTaskSecurityDescriptor is null)))
        {
            return false;
        }

        if (NeedsWindowsStartupMigrationElevation(runAsAdmin, previousTask, legacyTask))
        {
            return TryRunElevatedWindowsStartupTransaction(
                enable: true,
                taskName,
                userId,
                runAsAdmin,
                previousTaskXml,
                previousTaskSecurityDescriptor,
                legacyTask is null ? null : Env.SoftName,
                legacyTaskXml,
                legacyTaskSecurityDescriptor,
                registryEntry);
        }

        if (!TryRegisterWindowsTaskWithoutElevation(taskName, userId, runAsAdmin)
            || !IsExpectedWindowsTask(
                GetWindowsTaskInfo(taskName),
                userId,
                runAsAdmin,
                Env.ProgramPath,
                Env.ProgramDirectory))
        {
            RestoreWindowsTask(taskName, previousTaskXml, previousTask);
            return TryRunElevatedWindowsStartupTransaction(
                enable: true,
                taskName,
                userId,
                runAsAdmin,
                previousTaskXml,
                previousTaskSecurityDescriptor,
                legacyTask is null ? null : Env.SoftName,
                legacyTaskXml,
                legacyTaskSecurityDescriptor,
                registryEntry);
        }

        if (!TryRemoveLegacyWindowsStartup(userId, userName))
        {
            RestoreWindowsTask(Env.SoftName, legacyTaskXml, legacyTask);
            RestoreLegacyRegistryEntry(registryEntry);
            RestoreWindowsTask(taskName, previousTaskXml, previousTask);
            return TryRunElevatedWindowsStartupTransaction(
                enable: true,
                taskName,
                userId,
                runAsAdmin,
                previousTaskXml,
                previousTaskSecurityDescriptor,
                legacyTask is null ? null : Env.SoftName,
                legacyTaskXml,
                legacyTaskSecurityDescriptor,
                registryEntry);
        }

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryDisableWindowsStartup(string userId, string userName)
    {
        var taskName = GetWindowsTaskName(Env.SoftName, userId);
        var taskStatus = GetWindowsTaskLookupStatus(taskName);
        var task = taskStatus == WindowsTaskLookupStatus.Found
            ? GetWindowsTaskInfo(taskName)
            : null;
        var taskXml = taskStatus == WindowsTaskLookupStatus.Found
            ? GetWindowsTaskXml(taskName)
            : null;
        var taskSecurityDescriptor = taskStatus == WindowsTaskLookupStatus.Found
            ? GetWindowsTaskSecurityDescriptor(taskName)
            : null;
        var legacyTaskStatus = GetWindowsTaskLookupStatus(Env.SoftName);
        var legacyTaskCandidate = legacyTaskStatus == WindowsTaskLookupStatus.Found
            ? GetWindowsTaskInfo(Env.SoftName)
            : null;
        var legacyTask = IsLegacyTaskForCurrentUser(legacyTaskCandidate, userId, userName)
            ? legacyTaskCandidate
            : null;
        var legacyTaskXml = legacyTask is null
            ? null
            : GetWindowsTaskXml(Env.SoftName);
        var legacyTaskSecurityDescriptor = legacyTask is null
            ? null
            : GetWindowsTaskSecurityDescriptor(Env.SoftName);
        var registryEntry = GetLegacyRegistryEntry();

        if (taskStatus == WindowsTaskLookupStatus.Error
            || legacyTaskStatus == WindowsTaskLookupStatus.Error
            || (taskStatus == WindowsTaskLookupStatus.Found
                && (taskXml is null
                    || taskSecurityDescriptor is null))
            || (legacyTask is not null
                && (legacyTaskXml is null
                    || legacyTaskSecurityDescriptor is null)))
        {
            return false;
        }

        if (NeedsWindowsStartupMigrationElevation(
            runAsAdmin: false,
            currentTask: task,
            legacyTask))
        {
            return TryRunElevatedWindowsStartupTransaction(
                    enable: false,
                    taskName,
                    userId,
                    runAsAdmin: false,
                    taskXml,
                    taskSecurityDescriptor,
                    legacyTask is null ? null : Env.SoftName,
                    legacyTaskXml,
                    legacyTaskSecurityDescriptor,
                    registryEntry)
                && !CheckWindows();
        }

        var removed = (taskStatus == WindowsTaskLookupStatus.NotFound
                || TryDeleteWindowsTask(taskName, allowElevation: false))
            && (legacyTask is null
                || TryDeleteWindowsTask(Env.SoftName, allowElevation: false))
            && TryDeleteLegacyRegistryEntry()
            && !CheckWindows();
        if (removed)
        {
            return true;
        }

        RestoreLegacyRegistryEntry(registryEntry);
        RestoreWindowsTask(Env.SoftName, legacyTaskXml, legacyTask);
        RestoreWindowsTask(taskName, taskXml, task);
        return TryRunElevatedWindowsStartupTransaction(
                enable: false,
                taskName,
                userId,
                runAsAdmin: false,
                taskXml,
                taskSecurityDescriptor,
                legacyTask is null ? null : Env.SoftName,
                legacyTaskXml,
                legacyTaskSecurityDescriptor,
                registryEntry)
            && !CheckWindows();
    }

    [SupportedOSPlatform("windows")]
    private static bool TryRegisterWindowsTaskWithoutElevation(
        string taskName,
        string userId,
        bool runAsAdmin)
    {
        if (TryCreateTaskViaCom(taskName, userId, runAsAdmin)
            && IsExpectedWindowsTask(
                GetWindowsTaskInfo(taskName),
                userId,
                runAsAdmin,
                Env.ProgramPath,
                Env.ProgramDirectory))
        {
            return true;
        }

        var xml = BuildWindowsTaskXml(userId, runAsAdmin, Env.ProgramPath, Env.ProgramDirectory);
        return TryRegisterTaskXml(taskName, xml, elevate: false);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryCreateTaskViaCom(string taskName, string userId, bool runAsAdmin)
    {
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service", true)!;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder("\\");
            dynamic taskDef = scheduler.NewTask(0);

            taskDef.Principal.UserId = userId;
            taskDef.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN
            taskDef.Principal.RunLevel = runAsAdmin
                ? TaskRunLevelHighest
                : TaskRunLevelLeastPrivilege;
            taskDef.Settings.DisallowStartIfOnBatteries = false;
            taskDef.Settings.StopIfGoingOnBatteries = false;
            taskDef.Settings.ExecutionTimeLimit = "PT0S";
            taskDef.Settings.Enabled = true;

            dynamic trigger = taskDef.Triggers.Create(9); // TASK_TRIGGER_LOGON
            trigger.Enabled = true;
            trigger.UserId = userId;

            dynamic action = taskDef.Actions.Create(0); // TASK_ACTION_EXEC
            action.Path = Env.ProgramPath;
            action.WorkingDirectory = Env.ProgramDirectory;

            // 6 = TASK_CREATE_OR_UPDATE, 3 = TASK_LOGON_INTERACTIVE_TOKEN
            folder.RegisterTaskDefinition(taskName, taskDef, 6, userId, null, 3, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryRegisterTaskXml(string taskName, string xml, bool elevate)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            WriteTaskXml(xmlPath, xml);
            return RunSchtasks(
                ["/Create", "/TN", taskName, "/XML", xmlPath, "/F"],
                elevate);
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch
            {
                // The temporary file is best-effort cleanup after registration.
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryRunElevatedWindowsStartupTransaction(
        bool enable,
        string taskName,
        string userId,
        bool runAsAdmin,
        string? previousTaskXml,
        string? previousTaskSecurityDescriptor,
        string? legacyTaskName,
        string? legacyTaskXml,
        string? legacyTaskSecurityDescriptor,
        WindowsStartupRegistryEntry? registryEntry)
    {
        try
        {
            var newTaskXml = enable
                ? NormalizeTaskXml(
                    BuildWindowsTaskXml(
                        userId,
                        runAsAdmin,
                        Env.ProgramPath,
                        Env.ProgramDirectory))
                : null;

            var manifest = new WindowsStartupMigrationManifest(
                taskName,
                newTaskXml,
                enable && !runAsAdmin
                    ? BuildWindowsTaskSecurityDescriptor(userId)
                    : null,
                DeleteNewTask: !enable && previousTaskXml is not null,
                previousTaskXml is null ? null : NormalizeTaskXml(previousTaskXml),
                previousTaskSecurityDescriptor,
                legacyTaskName,
                legacyTaskXml is null ? null : NormalizeTaskXml(legacyTaskXml),
                legacyTaskSecurityDescriptor,
                registryEntry?.Value,
                registryEntry is null ? null : (int)registryEntry.ValueKind,
                RunKeyPath,
                Env.SoftName,
                userId,
                runAsAdmin ? TaskRunLevelHighest : TaskRunLevelLeastPrivilege,
                Env.ProgramPath,
                Env.ProgramDirectory);
            var command = BuildElevatedPowerShellCommand(
                JsonSerializer.Serialize(manifest),
                WindowsStartupMigrationScript);
            var startInfo = CreateElevatedPowerShellStartInfo(command);
            if (startInfo.ArgumentList[^1].Length >= 30_000)
            {
                return false;
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteTaskXml(string path, string xml)
    {
        File.WriteAllText(
            path,
            NormalizeTaskXml(xml),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string NormalizeTaskXml(string xml)
    {
        var document = XDocument.Parse(xml);
        document.Declaration = new XDeclaration("1.0", "utf-8", null);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryDeleteWindowsTask(string taskName, bool allowElevation)
    {
        var taskStatus = GetWindowsTaskLookupStatus(taskName);
        if (taskStatus == WindowsTaskLookupStatus.NotFound)
        {
            return true;
        }

        if (taskStatus == WindowsTaskLookupStatus.Error)
        {
            return false;
        }

        var arguments = new[] { "/Delete", "/TN", taskName, "/F" };
        if (RunSchtasks(arguments, elevate: false)
            && GetWindowsTaskLookupStatus(taskName) == WindowsTaskLookupStatus.NotFound)
        {
            return true;
        }

        return allowElevation
            && RunSchtasks(arguments, elevate: true)
            && GetWindowsTaskLookupStatus(taskName) == WindowsTaskLookupStatus.NotFound;
    }

    private static bool RunSchtasks(IEnumerable<string> arguments, bool elevate)
    {
        try
        {
            using var process = Process.Start(CreateSchtasksStartInfo(arguments, elevate));
            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static WindowsTaskLookupStatus GetWindowsTaskLookupStatus(string taskName)
    {
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service", true)!;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            scheduler.GetFolder("\\").GetTask(taskName);
            return WindowsTaskLookupStatus.Found;
        }
        catch (Exception exception)
            when (exception.HResult is
                FileNotFoundHResult or PathNotFoundHResult or TaskNotFoundHResult)
        {
            return WindowsTaskLookupStatus.NotFound;
        }
        catch
        {
            return WindowsTaskLookupStatus.Error;
        }
    }

    [SupportedOSPlatform("windows")]
    private static WindowsStartupTaskInfo? GetWindowsTaskInfo(string taskName)
    {
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service", true)!;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic task = scheduler.GetFolder("\\").GetTask(taskName);
            dynamic definition = task.Definition;

            dynamic? execAction = null;
            for (var index = 1; index <= definition.Actions.Count; index++)
            {
                dynamic candidate = definition.Actions.Item(index);
                if (Convert.ToInt32(candidate.Type) == 0) // TASK_ACTION_EXEC
                {
                    execAction = candidate;
                    break;
                }
            }

            dynamic? logonTrigger = null;
            for (var index = 1; index <= definition.Triggers.Count; index++)
            {
                dynamic candidate = definition.Triggers.Item(index);
                if (Convert.ToInt32(candidate.Type) == 9) // TASK_TRIGGER_LOGON
                {
                    logonTrigger = candidate;
                    break;
                }
            }

            if (execAction is null || logonTrigger is null)
            {
                return null;
            }

            return new WindowsStartupTaskInfo(
                Convert.ToString(execAction.Path) ?? string.Empty,
                Convert.ToString(execAction.Arguments) ?? string.Empty,
                Convert.ToString(execAction.WorkingDirectory) ?? string.Empty,
                Convert.ToInt32(definition.Actions.Count),
                NormalizeWindowsUserId(
                    Convert.ToString(definition.Principal.UserId) ?? string.Empty),
                Convert.ToInt32(definition.Principal.LogonType),
                NormalizeWindowsUserId(
                    Convert.ToString(logonTrigger.UserId) ?? string.Empty),
                Convert.ToInt32(definition.Triggers.Count),
                Convert.ToInt32(definition.Principal.RunLevel),
                Convert.ToBoolean(task.Enabled),
                Convert.ToBoolean(logonTrigger.Enabled),
                Convert.ToBoolean(definition.Settings.DisallowStartIfOnBatteries),
                Convert.ToBoolean(definition.Settings.StopIfGoingOnBatteries),
                Convert.ToString(definition.Settings.ExecutionTimeLimit) ?? string.Empty);
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? GetWindowsTaskSecurityDescriptor(string taskName)
    {
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service", true)!;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic task = scheduler.GetFolder("\\").GetTask(taskName);
            const int daclSecurityInformation = 0x4;
            return Convert.ToString(
                task.GetSecurityDescriptor(daclSecurityInformation));
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? GetWindowsTaskXml(string taskName)
    {
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service", true)!;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic task = scheduler.GetFolder("\\").GetTask(taskName);
            return Convert.ToString(task.Xml);
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestoreWindowsTask(
        string taskName,
        string? previousTaskXml,
        WindowsStartupTaskInfo? previousTask)
    {
        var currentTask = GetWindowsTaskInfo(taskName);
        if (currentTask == previousTask)
        {
            return;
        }

        if (previousTaskXml is null)
        {
            if (currentTask is not null)
            {
                TryDeleteWindowsTask(
                    taskName,
                    allowElevation: currentTask.RunLevel == TaskRunLevelHighest);
            }

            return;
        }

        TryRegisterTaskXml(
            taskName,
            previousTaskXml,
            elevate: previousTask?.RunLevel == TaskRunLevelHighest);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryRemoveLegacyWindowsStartup(string userId, string userName)
    {
        var legacyTask = GetWindowsTaskInfo(Env.SoftName);
        if (IsLegacyTaskForCurrentUser(legacyTask, userId, userName)
            && !TryDeleteWindowsTask(
                Env.SoftName,
                allowElevation: legacyTask!.RunLevel == TaskRunLevelHighest))
        {
            return false;
        }

        return TryDeleteLegacyRegistryEntry();
    }

    private static bool IsLegacyTaskForCurrentUser(
        WindowsStartupTaskInfo? task,
        string userId,
        string userName)
    {
        return IsExpectedLegacyWindowsTask(
            task,
            userId,
            userName,
            Env.ProgramPath,
            Env.ProgramDirectory);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryDeleteLegacyRegistryEntry()
    {
        try
        {
            if (GetLegacyRegistryEntry() is null)
            {
                return true;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(Env.SoftName, throwOnMissingValue: false);
            return !HasLegacyRegistryEntry();
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static WindowsStartupRegistryEntry? GetLegacyRegistryEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (key is null
            || key.GetValue(Env.SoftName) is not string path
            || !PathsEqual(path, Env.ProgramPath))
        {
            return null;
        }

        return new WindowsStartupRegistryEntry(path, key.GetValueKind(Env.SoftName));
    }

    [SupportedOSPlatform("windows")]
    private static void RestoreLegacyRegistryEntry(WindowsStartupRegistryEntry? previousEntry)
    {
        if (previousEntry is null)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key.GetValue(Env.SoftName) is null)
            {
                key.SetValue(Env.SoftName, previousEntry.Value, previousEntry.ValueKind);
            }
        }
        catch
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool HasLegacyRegistryEntry()
    {
        return GetLegacyRegistryEntry() is not null;
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckWindows()
    {
        try
        {
            var (userId, userName) = GetCurrentWindowsUser();
            var task = GetWindowsTaskInfo(GetWindowsTaskName(Env.SoftName, userId));
            if (IsExpectedWindowsTask(
                task,
                userId,
                runAsAdmin: null,
                Env.ProgramPath,
                Env.ProgramDirectory))
            {
                return true;
            }

            var legacyTask = GetWindowsTaskInfo(Env.SoftName);
            return (IsLegacyTaskForCurrentUser(legacyTask, userId, userName)
                    && legacyTask!.Enabled
                    && legacyTask.TriggerEnabled
                    && PathsEqual(legacyTask.WorkingDirectory, Env.ProgramDirectory))
                || HasLegacyRegistryEntry();
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetWindowsRunAsAdmin(out bool runAsAdmin)
    {
        runAsAdmin = false;
        try
        {
            var (userId, userName) = GetCurrentWindowsUser();
            var task = GetWindowsTaskInfo(GetWindowsTaskName(Env.SoftName, userId));
            if (IsExpectedWindowsTask(
                task,
                userId,
                runAsAdmin: null,
                Env.ProgramPath,
                Env.ProgramDirectory))
            {
                runAsAdmin = task!.RunLevel == TaskRunLevelHighest;
                return true;
            }

            var legacyTask = GetWindowsTaskInfo(Env.SoftName);
            if (IsLegacyTaskForCurrentUser(legacyTask, userId, userName)
                && legacyTask!.Enabled
                && legacyTask.TriggerEnabled
                && PathsEqual(legacyTask.WorkingDirectory, Env.ProgramDirectory))
            {
                runAsAdmin = legacyTask.RunLevel == TaskRunLevelHighest;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static (string UserId, string UserName) GetCurrentWindowsUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var userId = identity.User?.Value
            ?? throw new InvalidOperationException("The current Windows user does not have a SID.");
        return (userId, identity.Name);
    }

    #region Linux

    [SupportedOSPlatform("linux")]
    private static void SetLinux(bool enable)
    {
        var autoStartFolder = Path.Combine(Env.UserAppDataDirectory, "autostart");
        if (enable)
        {
            if (Directory.Exists(autoStartFolder) is false)
            {
                Directory.CreateDirectory(autoStartFolder);
            }

            DesktopEntryHelper.SetLinuxDesktopEntry(autoStartFolder);
        }
        else
        {
            DesktopEntryHelper.RemvoeLinuxDesktopEntry(autoStartFolder);
        }
    }

    [SupportedOSPlatform("linux")]
    private static bool CheckLinux()
    {
        var desktopFileName = $"{Env.LinuxPackageAppId}.desktop";
        var autoStartFolder = Path.Combine(Env.UserAppDataDirectory, "autostart");
        var autoStartDestkopFilePath = Path.Combine(autoStartFolder, desktopFileName);
        var fileInfo = new FileInfo(autoStartDestkopFilePath);
        if (fileInfo.Exists is false)
        {
            return false;
        }

        if (fileInfo.Length > 1024 * 1024)  // 1Mb
        {
            return false;
        }

        return File.ReadLines(autoStartDestkopFilePath).Any(line => line == $"TryExec={Env.ProgramPath}");
    }

    #endregion
}
