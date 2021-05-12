namespace SyncClipboard.Service
{
    public interface IService
    {
        bool Enabled { get; }

        void Start();
        void Stop();
        void Load();
        void RegistEvent();
        void UnRegistEvent();
        void RegistEventHandler();
        void UnRegistEventHandler();
    }

    public abstract class Service : IService
    {
        public bool Enabled { get; set; } = false;

        protected abstract void StartService();
        protected abstract void StopSerivce();
        public virtual void Load() { }
        public virtual void RegistEvent() { }
        public virtual void UnRegistEvent() { }
        public virtual void RegistEventHandler() { }
        public virtual void UnRegistEventHandler() { }

        public void Start()
        {
            if (!Enabled)
            {
                Enabled = true;
                this.StartService();
                this.RegistEvent();
            }
        }

        public void Stop()
        {
            if (Enabled)
            {
                Enabled = false;
                this.UnRegistEventHandler();
                this.UnRegistEvent();
                this.StopSerivce();
            }
        }
    }
}
