using Sharprompt.Drivers;

namespace SyncClipboard.Updater;

internal sealed class UpdaterConsoleDriver(IConsoleDriver inner) : IConsoleDriver
{
    public ConsoleKeyInfo ReadKey()
    {
        ConsoleKeyInfo key;
        do
        {
            key = inner.ReadKey();
        } while (key.Key == ConsoleKey.Escape);
        return key;
    }

    public Action CancellationCallback { get => inner.CancellationCallback; set => inner.CancellationCallback = value; }
    public bool KeyAvailable => inner.KeyAvailable;
    public bool CursorVisible { set => inner.CursorVisible = value; }
    public int CursorLeft => inner.CursorLeft;
    public int CursorTop => inner.CursorTop;
    public int BufferWidth => inner.BufferWidth;
    public int BufferHeight => inner.BufferHeight;
    public int WindowWidth => inner.WindowWidth;
    public int WindowHeight => inner.WindowHeight;
    public void Beep() => inner.Beep();
    public void Reset() => inner.Reset();
    public void ClearLine(int top) => inner.ClearLine(top);
    public void Write(string value, ConsoleColor color) => inner.Write(value, color);
    public void WriteLine() => inner.WriteLine();
    public void SetCursorPosition(int left, int top) => inner.SetCursorPosition(left, top);
    public void Dispose() => inner.Dispose();
}
