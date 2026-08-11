using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities.Network;

public sealed class WindowsWifiNetworkInfoProvider : IWifiNetworkInfoProvider
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorAccessDenied = 5;
    private const uint ClientVersion = 2;

    public event EventHandler? NetworkChanged
    {
        add { }
        remove { }
    }

    public bool CanOpenWifiSettings => OperatingSystem.IsWindows();

    public async Task<(WifiAccessStatus Status, IReadOnlyList<WifiNetworkInfo> Networks, string? Error)> GetConnectedNetworksAsync(
        bool requestAccess,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return (WifiAccessStatus.Unsupported, [], null);
        }

        try
        {
            var result = await Task.Run(
                () => QueryConnectedNetworks(cancellationToken),
                cancellationToken).ConfigureAwait(false);
            return (WifiAccessStatus.Available, result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception ex) when ((uint)ex.NativeErrorCode == ErrorAccessDenied)
        {
            return (WifiAccessStatus.Denied, [], ex.Message);
        }
        catch (Exception ex)
        {
            return (WifiAccessStatus.Error, [], ex.Message);
        }
    }

    public void OpenWifiSettings()
    {
        if (!OperatingSystem.IsWindows()) return;
        Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true });
    }

    private static List<WifiNetworkInfo> QueryConnectedNetworks(CancellationToken cancellationToken)
    {
        var result = WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out var clientHandle);
        if (result != ErrorSuccess)
        {
            throw new Win32Exception((int)result);
        }

        try
        {
            result = WlanEnumInterfaces(clientHandle, IntPtr.Zero, out var interfaceListPointer);
            if (result != ErrorSuccess)
            {
                throw new Win32Exception((int)result);
            }

            try
            {
                var count = Marshal.ReadInt32(interfaceListPointer);
                var current = IntPtr.Add(interfaceListPointer, sizeof(int) * 2);
                var itemSize = Marshal.SizeOf<WlanInterfaceInfo>();
                var networks = new List<WifiNetworkInfo>();

                for (var index = 0; index < count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var info = Marshal.PtrToStructure<WlanInterfaceInfo>(current);
                    current = IntPtr.Add(current, itemSize);
                    if (info.State != WlanInterfaceState.Connected)
                    {
                        continue;
                    }

                    var interfaceGuid = info.InterfaceGuid;
                    result = WlanQueryInterface(
                        clientHandle,
                        ref interfaceGuid,
                        WlanInterfaceOpcode.CurrentConnection,
                        IntPtr.Zero,
                        out _,
                        out var connectionPointer,
                        out _);
                    if (result == ErrorAccessDenied)
                    {
                        throw new Win32Exception((int)result);
                    }
                    if (result != ErrorSuccess)
                    {
                        continue;
                    }

                    try
                    {
                        var connection = Marshal.PtrToStructure<WlanConnectionAttributes>(connectionPointer);
                        var ssidBytes = connection.AssociationAttributes.Ssid.Bytes ?? [];
                        var ssidLength = Math.Min((int)connection.AssociationAttributes.Ssid.Length, ssidBytes.Length);
                        var ssid = Encoding.UTF8.GetString(ssidBytes, 0, ssidLength);
                        if (!string.IsNullOrEmpty(ssid))
                        {
                            networks.Add(new WifiNetworkInfo(info.InterfaceGuid.ToString("D"), info.Description, ssid));
                        }
                    }
                    finally
                    {
                        WlanFreeMemory(connectionPointer);
                    }
                }

                return networks;
            }
            finally
            {
                WlanFreeMemory(interfaceListPointer);
            }
        }
        finally
        {
            var closeResult = WlanCloseHandle(clientHandle, IntPtr.Zero);
            Debug.Assert(closeResult == ErrorSuccess, $"WlanCloseHandle failed with error code {closeResult}.");
        }
    }

    private enum WlanInterfaceState
    {
        NotReady,
        Connected,
        AdHocNetworkFormed,
        Disconnecting,
        Disconnected,
        Associating,
        Discovering,
        Authenticating,
    }

    private enum WlanInterfaceOpcode
    {
        AutoconfEnabled = 1,
        BackgroundScanEnabled,
        MediaStreamingMode,
        RadioState,
        BssType,
        InterfaceState,
        CurrentConnection,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;

        public WlanInterfaceState State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid Ssid;
        public int BssType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Bssid;

        public int PhyType;
        public uint PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanSecurityAttributes
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool SecurityEnabled;

        [MarshalAs(UnmanagedType.Bool)]
        public bool OneXEnabled;

        public int AuthAlgorithm;
        public int CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public WlanInterfaceState State;
        public int ConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;

        public WlanAssociationAttributes AssociationAttributes;
        public WlanSecurityAttributes SecurityAttributes;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        WlanInterfaceOpcode opcode,
        IntPtr reserved,
        out uint dataSize,
        out IntPtr data,
        out int opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
