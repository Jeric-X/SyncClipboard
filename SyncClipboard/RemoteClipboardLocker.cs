using System.Threading;

namespace SyncClipboard
{
    public static class RemoteClipboardLocker
    {
        static Mutex mutex = new Mutex();

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
