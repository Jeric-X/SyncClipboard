using System;

namespace SyncClipboard.Utility
{
    static class Log
    {
        public static void Write(string str)
        {
            Console.WriteLine(string.Format("[{0}] {1}", DateTime.Now, str));
        }
    }
}
