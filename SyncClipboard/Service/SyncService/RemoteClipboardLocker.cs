using System.Threading;

namespace SyncClipboard
{
    public static class RemoteClipboardLocker
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
