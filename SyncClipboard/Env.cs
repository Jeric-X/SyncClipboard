namespace SyncClipboard
{
    internal static class Env
    {
        public const string VERSION = "1.3.8";
        private const string PATH_SLASH = @"\";
        internal static readonly string Directory = System.Windows.Forms.Application.StartupPath;

        internal static string PathConcat(params string[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = values[i].TrimEnd(new char[]{'\\', '/'});
            }
            return string.Join(PATH_SLASH, values);
        }

        internal static string FullPath(string relativePath)
        {
            return PathConcat(Directory, relativePath);
        }
    }
}