namespace SyncClipboard.Core.Interfaces
{
    public interface IService
    {
        bool Enabled { get; }

        void Start();
        Task StopAsync();
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
        protected virtual void StopSerivce() { }
        protected virtual Task StopSerivceAsync()
        {
            StopSerivce();
            return Task.CompletedTask;
        }
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

        public async Task StopAsync()
        {
            if (Enabled)
            {
                Enabled = false;
                this.UnRegistEventHandler();
                this.UnRegistEvent();
                await this.StopSerivceAsync().ConfigureAwait(false);
            }
        }

        public void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }
}
