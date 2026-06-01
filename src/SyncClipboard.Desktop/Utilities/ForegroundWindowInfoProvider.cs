using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class ForegroundWindowInfoProvider : IForegroundWindowInfoProvider
{
    public ForegroundWindowDetail GetForegroundWindowDetail()
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
                return ForegroundWindowDetail.Invalid;
            }

            var lines = output.Split('\n');
            if (lines.Length < 2)
            {
                return ForegroundWindowDetail.Invalid;
            }

            var positionLine = lines.FirstOrDefault(l => l.Trim().StartsWith("Position:"));
            var geometryLine = lines.FirstOrDefault(l => l.Trim().StartsWith("Geometry:"));

            if (positionLine == null || geometryLine == null)
            {
                return ForegroundWindowDetail.Invalid;
            }

            var positionParts = positionLine.Split(' ', '\t').Where(p => p.Contains(',')).FirstOrDefault()?.Split(',');
            var geometryParts = geometryLine.Split(' ', '\t').Where(p => p.Contains('x')).FirstOrDefault()?.Split('x');

            if (positionParts == null || geometryParts == null || positionParts.Length < 2 || geometryParts.Length < 2)
            {
                return ForegroundWindowDetail.Invalid;
            }

            if (!int.TryParse(positionParts[0], out var x) || !int.TryParse(positionParts[1], out var y))
            {
                return ForegroundWindowDetail.Invalid;
            }

            if (!int.TryParse(geometryParts[0], out var width) || !int.TryParse(geometryParts[1], out var height))
            {
                return ForegroundWindowDetail.Invalid;
            }

            return new ForegroundWindowDetail
            {
                WindowInfo = null,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                IsValid = true
            };
        }
        catch
        {
            return ForegroundWindowDetail.Invalid;
        }
    }

    public ForegroundWindowInfo? GetForegroundWindowInfo()
    {
        return null;
    }
}
