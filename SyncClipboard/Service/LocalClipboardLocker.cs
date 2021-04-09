using System.Threading;

namespace SyncClipboard
{
    public static class LocalClipboardLocker
    {
        static Mutex mutex = new Mutex();

        static private int times = 0;
        public static void Lock()
        {
            mutex.WaitOne();
            times++;
        }

        public static void Unlock()
        {
            mutex.ReleaseMutex();
            times--;
        }
    }
}
