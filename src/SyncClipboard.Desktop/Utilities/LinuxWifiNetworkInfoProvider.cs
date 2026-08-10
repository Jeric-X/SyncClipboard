using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.Utilities;

internal sealed class LinuxWifiNetworkInfoProvider : IWifiNetworkInfoProvider
{
    public event EventHandler? NetworkChanged
    {
        add { }
        remove { }
    }

    public bool CanOpenWifiSettings => false;

    public async Task<(WifiAccessStatus Status, IReadOnlyList<WifiNetworkInfo> Networks, string? Error)> GetConnectedNetworksAsync(
        bool requestAccess,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return (WifiAccessStatus.Unsupported, [], null);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nmcli",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--terse");
            startInfo.ArgumentList.Add("--escape");
            startInfo.ArgumentList.Add("no");
            startInfo.ArgumentList.Add("--fields");
            startInfo.ArgumentList.Add("IN-USE,DEVICE,SSID");
            startInfo.ArgumentList.Add("device");
            startInfo.ArgumentList.Add("wifi");
            startInfo.ArgumentList.Add("list");
            startInfo.ArgumentList.Add("--rescan");
            startInfo.ArgumentList.Add("no");
            startInfo.Environment["LC_ALL"] = "C";

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (WifiAccessStatus.Unsupported, [], "NetworkManager command is unavailable.");
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return (WifiAccessStatus.Unsupported, [], string.IsNullOrWhiteSpace(error) ? "NetworkManager is unavailable." : error.Trim());
            }

            var networks = new List<WifiNetworkInfo>();
            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split(':', 3);
                if (fields.Length == 3 && fields[0] == "*" && !string.IsNullOrEmpty(fields[2]))
                {
                    networks.Add(new WifiNetworkInfo(fields[1], fields[1], fields[2]));
                }
            }

            return (WifiAccessStatus.Available, networks, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (WifiAccessStatus.Unsupported, [], ex.Message);
        }
    }

    public void OpenWifiSettings() { }
}
