using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Diagnostics;
using System.Runtime.Versioning;
using System;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class MousePositionProvider : IMousePositionProvider
{
    public ScreenPosition GetMousePosition()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xdotool",
                    Arguments = "getmouselocation",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1000);

            if (process.ExitCode != 0 || string.IsNullOrEmpty(output))
            {
                return ScreenPosition.Invalid;
            }

            var parts = output.Split(' ');
            int x = 0, y = 0;
            foreach (var part in parts)
            {
                if (part.StartsWith("x:"))
                {
                    _ = int.TryParse(part.AsSpan(2), out x);
                }
                else if (part.StartsWith("y:"))
                {
                    _ = int.TryParse(part.AsSpan(2), out y);
                }
            }

            return new ScreenPosition
            {
                X = x,
                Y = y,
                IsValid = true
            };
        }
        catch
        {
            return ScreenPosition.Invalid;
        }
    }
}
