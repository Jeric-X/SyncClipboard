using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncClipboard.Server.Core.Models;

public sealed class PushDeviceRegistrationRequest
{
    public string? Platform { get; init; }
    public string? Provider { get; init; }
    public string? Token { get; init; }
    public string? AppVersion { get; init; }
}

public sealed record PushDeviceRegistration(
    string DeviceId,
    string Platform,
    string Provider,
    string PushToken,
    string? AppVersion,
    DateTimeOffset LastUpdated);

[Table("PushDeviceRegistrations")]
public sealed class PushDeviceRegistrationEntity
{
    [Key]
    [MaxLength(36)]
    public string DeviceId { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Platform { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Provider { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string PushToken { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? AppVersion { get; set; }

    public DateTimeOffset LastUpdated { get; set; }

    public PushDeviceRegistration ToRegistration()
    {
        return new PushDeviceRegistration(
            DeviceId, Platform, Provider, PushToken, AppVersion, LastUpdated);
    }
}
