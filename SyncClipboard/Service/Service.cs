namespace SyncClipboard.Service
{
    public interface IService
    {
        bool Enabled { get; }

        void Start();
        void Stop();
        void Load();
    }

    public abstract class Service : IService
    {
        public bool Enabled { get; set; } = false;

        protected abstract void StartService();
        protected abstract void StopSerivce();
        public virtual void Load() { }

        public void Start()
        {
            if (!Enabled)
            {
                Enabled = true;
                this.StartService();
            }
        }

        public void Stop()
        {
            if (Enabled)
            {
                Enabled = false;
                this.StopSerivce();
            }
        }

    }
}
