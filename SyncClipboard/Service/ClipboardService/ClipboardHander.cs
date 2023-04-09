using System;
using System.Threading;
using SyncClipboard.Module;
using SyncClipboard.Utility;
#nullable enable

namespace SyncClipboard.Service
{
    abstract public class ClipboardHander : Service
    {
        private event Action<bool>? SwitchChanged;
        protected bool SwitchOn = true;
        public abstract string SERVICE_NAME { get; }
        public abstract string LOG_TAG { get; }
        protected override void StartService()
        {
            Log.Write(LOG_TAG, $"Service: {SERVICE_NAME} started");
            SwitchChanged += Global.Menu.AddMenuItemGroup(
                new string[] { SERVICE_NAME },
                new Action<bool>[] {
                    (check) => {
                        SwitchOn = check;
                        MenuItemChanged(check);
                        UserConfig.Save();
                    }
                }
            )[0];
            Load();
        }

        protected abstract void MenuItemChanged(bool check);
        protected abstract void LoadFromConfig(Action<bool> switchOn);

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
            LoadFromConfig((check) => SwitchOn = check);
            SwitchChanged?.Invoke(SwitchOn);
        }

        protected override void StopSerivce()
        {
            Log.Write(LOG_TAG, $"Service: {SERVICE_NAME} stopped");
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