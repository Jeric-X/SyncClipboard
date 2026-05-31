using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class ForegroundWindowInfoProvider : IForegroundWindowInfoProvider
{
    public ForegroundWindowInfo GetForegroundWindowInfo()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xdotool",
                    Arguments = "getactivewindow getwindowgeometry",
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
                return ForegroundWindowInfo.Invalid;
            }

            var lines = output.Split('\n');
            if (lines.Length < 2)
            {
                return ForegroundWindowInfo.Invalid;
            }

            var positionLine = lines.FirstOrDefault(l => l.Trim().StartsWith("Position:"));
            var geometryLine = lines.FirstOrDefault(l => l.Trim().StartsWith("Geometry:"));

            if (positionLine == null || geometryLine == null)
            {
                return ForegroundWindowInfo.Invalid;
            }

            var positionParts = positionLine.Split(' ', '\t').Where(p => p.Contains(',')).FirstOrDefault()?.Split(',');
            var geometryParts = geometryLine.Split(' ', '\t').Where(p => p.Contains('x')).FirstOrDefault()?.Split('x');

            if (positionParts == null || geometryParts == null || positionParts.Length < 2 || geometryParts.Length < 2)
            {
                return ForegroundWindowInfo.Invalid;
            }

            if (!int.TryParse(positionParts[0], out var x) || !int.TryParse(positionParts[1], out var y))
            {
                return ForegroundWindowInfo.Invalid;
            }

            if (!int.TryParse(geometryParts[0], out var width) || !int.TryParse(geometryParts[1], out var height))
            {
                return ForegroundWindowInfo.Invalid;
            }

            return new ForegroundWindowInfo
            {
                ProcessName = "",
                WindowTitle = "",
                ExecutableName = "",
                X = x,
                Y = y,
                Width = width,
                Height = height,
                IsValid = true
            };
        }
        catch
        {
            return ForegroundWindowInfo.Invalid;
        }
    }
}
