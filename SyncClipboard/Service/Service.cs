namespace SyncClipboard.Service
{
    public interface IService
    {
        bool Enabled { get; }

        void Start();
        void Stop();
        void Load();
        void RegistEvent();
        void RegistEventHandler();
    }

    public abstract class Service : IService
    {
        public bool Enabled { get; set; } = false;

        protected abstract void StartService();
        protected abstract void StopSerivce();
        public virtual void Load() { }
        public virtual void RegistEvent() { }
        public virtual void RegistEventHandler() { }

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
