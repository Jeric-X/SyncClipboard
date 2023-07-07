using System;
using System.Threading;
using SyncClipboard.Utility;
using SyncClipboard.Module;
using SyncClipboard.Utility.Image;
using static SyncClipboard.Service.ProfileFactory;
using SyncClipboard.Core.Utilities.Notification;
#nullable enable

namespace SyncClipboard.Service
{
    public class ConvertService : ClipboardHander
    {
        public override string SERVICE_NAME => "图片兼容性优化";
        public override string LOG_TAG => "COMPATIBILITY";

        protected override bool SwitchOn
        {
            get => UserConfig.Config.ClipboardService.ConvertSwitchOn;
            set
            {
                UserConfig.Config.ClipboardService.ConvertSwitchOn = value;
                UserConfig.Save();
            }
        }


        private readonly NotificationManager _notificationManager;

        public ConvertService(NotificationManager notificationManager)
        {
            _notificationManager = notificationManager;
        }

        protected override async void HandleClipboard(CancellationToken cancellationToken)
        {
            var profile = CreateFromLocal(out var localClipboard, _notificationManager);
            if (profile.GetProfileType() != ProfileType.ClipboardType.File || !NeedAdjust(localClipboard))
            {
                return;
            }

            try
            {
                var file = localClipboard.Files![0];
                var newPath = await ImageHelper.CompatibilityCast(file, SyncService.LOCAL_FILE_FOLDER, cancellationToken);
                new ImageProfile(newPath, _notificationManager).SetLocalClipboard(cancellationToken, false);
            }
            catch (Exception ex)
            {
                Log.Write(LOG_TAG, ex.Message);
                return;
            }
        }

        private static bool NeedAdjust(LocalClipboard localClipboard)
        {
            if (localClipboard.Files is null)
            {
                return false;
            }

            if (localClipboard.Files.Length != 1)
            {
                return false;
            }

            if (!ImageHelper.IsComplexImage(localClipboard.Files[0]))
            {
                return false;
            }

            return true;
        }
    }
}