using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class CaretPositionProvider(ILogger logger) : ICaretPositionProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "CaretPosition";

    public ScreenPosition GetCaretPosition()
    {
        var result = TryGetCaretPositionViaAtSpi();
        return result;
    }

    private ScreenPosition TryGetCaretPositionViaAtSpi()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = """
                        -c "
                        import gi
                        gi.require_version('Atspi', '2.0')
                        from gi.repository import Atspi
                        
                        Atspi.init()
                        focus = Atspi.get_focus()
                        if not focus:
                            exit(1)
                        
                        try:
                            text = focus.query_text()
                        except:
                            exit(1)
                        
                        if not text:
                            exit(1)
                        
                        offset = text.get_caret_offset()
                        rect = text.get_range_extents(offset, offset, Atspi.CoordType.SCREEN)
                        print(f'{rect.x},{rect.y}')
                        "
                        """,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadLine();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(1000);

            if (process.ExitCode != 0)
            {
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.Write(Tag, $"Python script failed with exit code {process.ExitCode}: {error.Trim()}");
                }
                else
                {
                    _logger.Write(Tag, $"Python script failed with exit code {process.ExitCode}");
                }
                return ScreenPosition.Invalid;
            }

            if (string.IsNullOrEmpty(output))
            {
                _logger.Write(Tag, "Python script returned empty output");
                return ScreenPosition.Invalid;
            }

            if (!output.Contains(','))
            {
                _logger.Write(Tag, $"Python script returned invalid format: {output}");
                return ScreenPosition.Invalid;
            }

            var parts = output.Split(',');
            if (parts.Length < 2)
            {
                _logger.Write(Tag, $"Failed to parse output: {output}");
                return ScreenPosition.Invalid;
            }

            if (!int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y))
            {
                _logger.Write(Tag, $"Failed to parse coordinates from: {output}");
                return ScreenPosition.Invalid;
            }

            _logger.Write(Tag, $"Caret position: ({x}, {y})");
            return new ScreenPosition { X = x, Y = y, IsValid = true };
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return ScreenPosition.Invalid;
        }
    }
}
