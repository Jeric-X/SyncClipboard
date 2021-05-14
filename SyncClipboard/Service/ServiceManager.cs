namespace SyncClipboard.Service
{
    internal class ServiceManager
    {
        private readonly IService[] _services = {
            new CommandService(),
            new ClipboardService(),
            new UploadService(),
            new DownloadService()
        };

        internal void StartUpAllService()
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
