using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class WindowsZipInstaller(UpdateInstallHelper helper) : IUpdateInstallStrategy
{
    public UpdatePackageKind Kind => UpdatePackageKind.WindowsPortable;

    public UpdateInstallCapability GetCapability()
        => UpdateInstallFiles.GetLocationCapability(Kind, Path.TrimEndingDirectorySeparator(Env.ProgramDirectory));

    public Task<PreparedUpdate> PrepareAsync(UpdateInstallRequest request, CancellationToken token)
        => helper.PrepareAsync(request, request.Capability.TargetPath, (snapshot, work) =>
        {
            var target = snapshot.Capability.TargetPath;
            var stage = Path.Combine(work, "payload");
            var protectedPaths = new[] { Env.AppDataDirectory, Env.StaticConfigPath, Env.PortableUserConfigFile,
                Env.PortableAppDataDirectory, Env.RuntimeConfigPath, Env.AppDataPathConfigPath };
            Directory.CreateDirectory(stage);
            UpdateInstallFiles.ExtractPortable(snapshot.PackagePath, stage, protectedPaths, target);
            UpdateInstallFiles.ValidateWindowsExecutable(Path.Combine(stage, "SyncClipboard.exe"));
            UpdateInstallFiles.ValidatePackageInfo(Path.Combine(stage, Env.UpdateInfoFile), Path.GetFileName(snapshot.PackagePath));
            var backupSize = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)
                .Select(path => Path.Combine(target, Path.GetRelativePath(stage, path)))
                .Where(File.Exists).Sum(path => new FileInfo(path).Length);
            UpdateInstallFiles.CheckSpace(work, backupSize);
            return Task.FromResult(new PreparedUpdate
            {
                Directory = work,
                Kind = Kind.ToString(),
                Target = target,
                Stage = stage,
                Backup = Path.Combine(work, "backup"),
                Executable = Env.ProgramPath,
                Version = snapshot.Version,
                ProcessId = Environment.ProcessId,
                Elevate = !UpdateInstallFiles.CanWrite(target),
                ProtectedPaths = protectedPaths
            });
        }, token);

    public Task StartAsync(PreparedUpdate update, CancellationToken token) => UpdateInstallHelper.StartAsync(update, token);
}
