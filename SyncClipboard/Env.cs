namespace SyncClipboard
{
    static class Env
    {
        const string PATH_SLASH = @"\";
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