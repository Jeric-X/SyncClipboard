namespace SyncClipboard
{
    internal static class Env
    {
        public const string VERSION = "1.3.6";
        private const string PATH_SLASH = @"\";
        internal static readonly string Directory = System.Windows.Forms.Application.StartupPath;

        internal static string PathConcat(params string[] values)
        {
            return string.Join(PATH_SLASH, values);
        }

        internal static string FullPath(string relativePath)
        {
            return PathConcat(Directory, relativePath);
        }
    }
}