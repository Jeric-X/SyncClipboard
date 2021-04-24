namespace SyncClipboard.Service
{
    internal class ServiceManager
    {
        private IService[] _services = { 
            new CommandService()
        };

        internal void StartUpAllService()
        {
            foreach (IService service in _services)
            {
                service.Start();
                service.RegistEvent();
            }

            foreach (IService service in _services)
            {
                service.Start();
                service.RegistEventHandler();
            }
        }

        internal void LoadAllService()
        {
            foreach (IService service in _services)
            {
                service.Load();
            }
        }

        internal void StopAllService()
        {
            foreach (IService service in _services)
            {
                service.Stop();
            }
        }
    }
}
