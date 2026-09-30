namespace SyncClipboard.Core.Utilities.Updater;

public enum UpdatePackageKind { Unsupported, FileReplacement }

public record UpdateInstallCapability(UpdatePackageKind Kind, string TargetPath, string? Reason = null)
{
    public bool Supported => Kind != UpdatePackageKind.Unsupported;
}

public record UpdateInstallRequest(string PackagePath, string Digest, string Version, UpdateInstallCapability Capability);

// This manifest is also read by the independent helpers. Keep property names stable.
public sealed record UpdateInstallTask
{
    public required string Directory { get; init; }
    public required string Kind { get; init; }
    public required string Target { get; init; }
    public required string Stage { get; init; }
    public required string Backup { get; init; }
    public required string Executable { get; init; }
    public required string Version { get; init; }
    public required int ProcessId { get; init; }
    public string Language { get; init; } = "en";
    public bool Elevate { get; init; }
    public string[] ProtectedPaths { get; init; } = [];
}
