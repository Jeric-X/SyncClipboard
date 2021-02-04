using System.Threading;
using SyncClipboard.Utility;

namespace SyncClipboard
{
    public static class LocalClipboardLocker
    {
        static Mutex mutex = new Mutex();

        static private int times = 0;
        public static void Lock()
        {
            Log.Write("lock local " + times.ToString());
            mutex.WaitOne();
            times++;
        }

        public static void Unlock()
        {
            mutex.ReleaseMutex();
            times--;
            Log.Write("unlock local " + times.ToString());
        }
    }
}
