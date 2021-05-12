using System.Threading;

namespace SyncClipboard
{
    public static class LocalClipboardLocker
    {
        private static readonly Mutex mutex = new Mutex();

        public static void Lock()
        {
            mutex.WaitOne();
        }

        public static void Unlock()
        {
            mutex.ReleaseMutex();
        }
    }
}
