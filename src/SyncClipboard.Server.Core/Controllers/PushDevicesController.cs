using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.PushDevices;

namespace SyncClipboard.Server.Core.Controllers;

[ApiController]
[Route("api/devices/{deviceId}/push")]
[Authorize]
[Tags("SyncClipboard")]
public class PushDevicesController(IPushDeviceRegistry registry) : ControllerBase
{
    private const string ANDROID_PLATFORM = "android";
    private const string FCM_PROVIDER = "fcm";
    private const int MAX_TOKEN_LENGTH = 4096;
    private const int MAX_APP_VERSION_LENGTH = 128;

    [HttpPut]
    public async Task<IActionResult> Put(
        string deviceId,
        [FromBody] PushDeviceRegistrationRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeDeviceId(deviceId, out var normalizedDeviceId))
        {
            return BadRequest("deviceId must be a valid UUID");
        }
        if (request is null)
        {
            return BadRequest("registration body is required");
        }

        var platform = request.Platform?.Trim().ToLowerInvariant();
        if (platform != ANDROID_PLATFORM)
        {
            return BadRequest("unsupported push platform");
        }
        var provider = request.Provider?.Trim().ToLowerInvariant();
        if (provider != FCM_PROVIDER)
        {
            return BadRequest("unsupported push provider");
        }
        var pushToken = request.Token?.Trim();
        if (string.IsNullOrEmpty(pushToken) || pushToken.Length > MAX_TOKEN_LENGTH)
        {
            return BadRequest("push token is invalid");
        }
        var appVersion = request.AppVersion?.Trim();
        if (appVersion?.Length > MAX_APP_VERSION_LENGTH)
        {
            return BadRequest("appVersion is too long");
        }
        if (string.IsNullOrEmpty(appVersion))
        {
            appVersion = null;
        }

        await registry.UpsertAsync(
            normalizedDeviceId,
            platform,
            provider,
            pushToken,
            appVersion,
            cancellationToken);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(
        string deviceId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeDeviceId(deviceId, out var normalizedDeviceId))
        {
            return BadRequest("deviceId must be a valid UUID");
        }

        await registry.RemoveAsync(normalizedDeviceId, cancellationToken);
        return NoContent();
    }

    private static bool TryNormalizeDeviceId(string deviceId, out string normalizedDeviceId)
    {
        if (Guid.TryParse(deviceId, out var parsedDeviceId))
        {
            normalizedDeviceId = parsedDeviceId.ToString("D");
            return true;
        }

        normalizedDeviceId = string.Empty;
        return false;
    }
}
