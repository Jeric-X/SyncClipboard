namespace SyncClipboard.Updater.Core;

public enum UpdatePackageKind { WindowsZip, MacDmg, LinuxAppImage }

// Paths describe the future installation; reading this protocol never changes those paths.
public sealed record UpdateRequest
{
    public int ProtocolVersion { get; init; } = 1;
    public required UpdatePackageKind PackageKind { get; init; }
    public required string TargetVersion { get; init; }
    public required string PackagePath { get; init; }
    public required string InstallationPath { get; init; }
    public required int ParentProcessId { get; init; }
    public string Language { get; init; } = "en";

    public void Validate()
    {
        if (ProtocolVersion != 1) throw new InvalidDataException("Unsupported update protocol version.");
        if (!Enum.IsDefined(PackageKind)) throw new InvalidDataException("Unknown update package kind.");
        if (string.IsNullOrWhiteSpace(TargetVersion)) throw new InvalidDataException("The target version is missing.");
        if (string.IsNullOrWhiteSpace(PackagePath) || string.IsNullOrWhiteSpace(InstallationPath)
            || !Path.IsPathFullyQualified(PackagePath) || !Path.IsPathFullyQualified(InstallationPath))
            throw new InvalidDataException("Update paths must be absolute.");
        if (string.IsNullOrWhiteSpace(Language)) throw new InvalidDataException("The language is missing.");
        if (ParentProcessId <= 0) throw new InvalidDataException("The parent process ID is invalid.");
    }
}
