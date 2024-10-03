namespace SyncClipboard.Core.Utilities.Fake;

public static class FakeFactory
{
    public static I Create<I, T, W>(string name) where T : I, new() where W : I, new()
    {
        try
        {
            return new T();
        }
        catch (Exception ex)
        {
            AppCore.Current.Logger.Write($"{name} failed to start, use fake instead. detail: \n{ex}");
            AppCore.Current.TrayIcon.SetStatusString($"{name}", $"Failed to start.\n{ex.Message}");
            return new W();
        }
    }
}
