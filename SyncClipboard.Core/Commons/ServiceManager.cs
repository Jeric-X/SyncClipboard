using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Commons
{
    public class ServiceManager
    {
        private readonly IEnumerable<IService> _services;

        public ServiceManager(IEnumerable<IService> services, UserConfig userConfig, UserConfig2 userConfig2)
        {
            _services = services;
            userConfig.ConfigChanged += LoadAllService;
            userConfig2.ConfigChanged += LoadAllService;
        }

        public void StartUpAllService()
        {
            foreach (IService service in _services)
            {
                service.Start();
            }

            foreach (IService service in _services)
            {
                service.RegistEventHandler();
            }
        }

        public void LoadAllService()
        {
            foreach (IService service in _services)
            {
                service.Load();
            }
        }

        public void StopAllService()
        {
            foreach (IService service in _services)
            {
                service.Stop();
            }
        }
    }
}
