namespace SyncClipboard.Desktop
{
    internal static class Env
    {
        public const string AppId = "61f529eb-8b6b-4da0-8c56-e3e28dc8b226";

        public static bool IsWayland =>
            System.OperatingSystem.IsLinux() &&
            string.Equals(System.Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", System.StringComparison.OrdinalIgnoreCase);
    }
}
