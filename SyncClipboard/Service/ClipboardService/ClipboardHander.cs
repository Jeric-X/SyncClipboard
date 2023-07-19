using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Threading;
#nullable enable

namespace SyncClipboard.Service
{
    abstract public class ClipboardHander : Core.Interfaces.Service
    {
        protected abstract bool SwitchOn { get; set; }
        protected abstract ILogger Logger { get; }
        public abstract string SERVICE_NAME { get; }
        public abstract string LOG_TAG { get; }

        protected ToggleMenuItem? ToggleMenuItem { get; set; }
        protected abstract IContextMenu? ContextMenu { get; }

        protected override void StartService()
        {
            Logger.Write(LOG_TAG, $"Service: {SERVICE_NAME} started");
            ToggleMenuItem = new ToggleMenuItem(SERVICE_NAME, false, (status) => SwitchOn = status);
            ContextMenu?.AddMenuItem(ToggleMenuItem);
            Load();
        }

        public void CancelProcess()
        {
            lock (_cancelSourceLocker)
            {
                if (_cancelSource?.Token.CanBeCanceled ?? false)
                {
                    _cancelSource.Cancel();
                }
                _cancelSource = null;
            }
        }

        public override void Load()
        {
            if (ToggleMenuItem is not null)
            {
                ToggleMenuItem.Checked = SwitchOn;
            }
        }

        protected override void StopSerivce()
        {
            CancelProcess();
            Logger.Write(LOG_TAG, $"Service: {SERVICE_NAME} stopped");
        }

        private CancellationTokenSource? _cancelSource;
        private readonly object _cancelSourceLocker = new();

        protected virtual CancellationToken StopPreviousAndGetNewToken()
        {
            lock (_cancelSourceLocker)
            {
                if (_cancelSource?.Token.CanBeCanceled ?? false)
                {
                    _cancelSource.Cancel();
                }
                _cancelSource = new();
                return _cancelSource.Token;
            }
        }

        private void ClipBoardChangedHandler()
        {
            if (SwitchOn)
            {
                CancellationToken cancelToken = StopPreviousAndGetNewToken();
                HandleClipboard(cancelToken);
            }
        }

        protected abstract void HandleClipboard(CancellationToken cancelToken);

        public override void RegistEventHandler()
        {
            Event.RegistEventHandler(ClipboardService.CLIPBOARD_CHANGED_EVENT_NAME, ClipBoardChangedHandler);
        }

        public override void UnRegistEventHandler()
        {
            Event.UnRegistEventHandler(ClipboardService.CLIPBOARD_CHANGED_EVENT_NAME, ClipBoardChangedHandler);
        }
    }
}