namespace SyncClipboard.Core.Utilities
{
    public sealed class Counter
    {
        private readonly Timer timer;
        private uint counted;
        private readonly ulong endTime;
        private event Action<uint> Tick;
        private readonly AutoResetEvent autoResetEvent = new(false);
        private bool sucess = true;
        private readonly uint _step = 1000;

        public Counter(Action<uint> callback, ulong end)
        {
            counted = 0;
            endTime = end;
            Tick += callback;
            timer = new Timer(InvokeTick, counted, 0, _step);
        }

        public Counter(Action<uint> callback, int step, ulong end)
        {
            counted = 0;
            endTime = end;
            _step = (uint)step;
            Tick += callback;
            timer = new Timer(InvokeTick, counted, 0, step);
        }

        ~Counter()
        {
            timer.Dispose();
        }

        private bool Wait()
        {
            autoResetEvent.WaitOne();
            return sucess;
        }

        public Task<bool> WaitAsync()
        {
            return Task.Run(
                () => Wait()
            );
        }

        public void Cancle()
        {
            sucess = false;
            timer.Dispose();
            autoResetEvent.Set();
        }

        private void InvokeTick(object? _)
        {
            Tick?.Invoke(counted);
            counted++;
            if (counted * _step > endTime)
            {
                autoResetEvent.Set();
                timer.Dispose();
            }
        }
    }
}
