using System;
using System.Runtime.Versioning;
using AppKit;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.MacOS.Utilities;

[SupportedOSPlatform("macos")]
internal sealed class MousePositionProvider(ILogger logger) : IMousePositionProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "MousePosition";

    public ScreenPosition? GetMousePosition()
    {
        try
        {
            // NSEvent.MouseLocation returns the current mouse location in screen coordinates
            // Note: macOS coordinate system has origin at bottom-left
            var location = NSEvent.CurrentMouseLocation;

            // Get screen height to flip Y coordinate
            // The mouse position uses bottom-left origin, but we need top-left origin
            var screens = NSScreen.Screens;
            if (screens == null || screens.Length == 0)
            {
                _logger.Write(Tag, "Failed to get screens");
                return null;
            }

            // Find the screen that contains the mouse position
            double screenHeight = 0;
            foreach (var screen in screens)
            {
                var frame = screen.Frame;
                if (location.X >= frame.X && location.X < frame.X + frame.Width &&
                    location.Y >= frame.Y && location.Y < frame.Y + frame.Height)
                {
                    // Found the screen containing the mouse
                    screenHeight = frame.Y + frame.Height;
                    _logger.Write(Tag, $"Screen: X={frame.X}, Y={frame.Y}, Width={frame.Width}, Height={frame.Height}, total height={screenHeight}");
                    break;
                }
            }

            // If not found on any screen, use the main screen height
            if (screenHeight == 0)
            {
                var mainScreen = screens[0].Frame;
                screenHeight = mainScreen.Y + mainScreen.Height;
                _logger.Write(Tag, $"Mouse not found on any screen, using main screen: X={mainScreen.X}, Y={mainScreen.Y}, Width={mainScreen.Width}, Height={mainScreen.Height}, total height={screenHeight}");
            }

            // Flip Y coordinate: new_y = screen_height - original_y
            var flippedY = screenHeight - location.Y;

            var result = new ScreenPosition
            {
                X = (int)location.X,
                Y = (int)flippedY
            };

            _logger.Write(Tag, $"Mouse position: X={result.X}, Y={result.Y} (original Y={location.Y}, screen height={screenHeight})");

            return result;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return null;
        }
    }
}
