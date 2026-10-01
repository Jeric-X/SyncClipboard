using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SyncClipboard.Updater;

// WinUI keeps native DLLs mapped until process exit. Keep the entire helper directory until then.
internal static class WindowsWorkspaceCleanup
{
    internal static ProcessStartInfo CreateStartInfo(string workspace)
    {
        UpdateWorker.ValidateWorkspace(workspace);
        const string script = """
            $ErrorActionPreference = 'Stop'
            $directory = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($env:SYNC_CLIPBOARD_CLEANUP_WORK))
            $id = [guid]::Empty
            if ($directory.Parent.Name -ne 'SyncClipboard-updates' -or
                ![guid]::TryParseExact($directory.Name, 'N', [ref]$id) -or
                !(Test-Path -LiteralPath (Join-Path $directory.FullName '.syncclipboard-update'))) { exit 1 }
            $owner = Get-Process -Id ([int]$env:SYNC_CLIPBOARD_CLEANUP_PID) -ErrorAction SilentlyContinue
            if ($owner -and $owner.StartTime.ToUniversalTime().Ticks -eq [long]$env:SYNC_CLIPBOARD_CLEANUP_START) {
                $owner.WaitForExit()
            }
            for ($attempt = 0; $attempt -lt 30; $attempt++) {
                try { Remove-Item -LiteralPath $directory.FullName -Recurse -Force; exit 0 }
                catch { Start-Sleep -Seconds 1 }
            }
            exit 1
            """;
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var current = Process.GetCurrentProcess();
        start.Environment["SYNC_CLIPBOARD_CLEANUP_WORK"] = Path.GetFullPath(workspace);
        start.Environment["SYNC_CLIPBOARD_CLEANUP_PID"] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        start.Environment["SYNC_CLIPBOARD_CLEANUP_START"] = current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
        return start;
    }
}
