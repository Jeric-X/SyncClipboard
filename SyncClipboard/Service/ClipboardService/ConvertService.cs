using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Image;
using SyncClipboard.Core.Utilities.Notification;
using System;
using System.Threading;
using static SyncClipboard.Service.ProfileFactory;
#nullable enable

namespace SyncClipboard.Service
{
    public class ConvertService : ClipboardHander
    {
        public override string SERVICE_NAME => "图片兼容性优化";
        public override string LOG_TAG => "COMPATIBILITY";

        protected override bool SwitchOn
        {
            get => _userConfig.Config.ClipboardService.ConvertSwitchOn;
            set
            {
                _userConfig.Config.ClipboardService.ConvertSwitchOn = value;
                _userConfig.Save();
            }
        }


        private readonly NotificationManager _notificationManager;
        private readonly ILogger _logger;
        private readonly UserConfig _userConfig;

        public ConvertService(NotificationManager notificationManager, ILogger logger, UserConfig userConfig) : base(logger)
        {
            _notificationManager = notificationManager;
            _logger = logger;
            _userConfig = userConfig;
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
                new ImageProfile(newPath, _notificationManager, _logger, _userConfig).SetLocalClipboard(cancellationToken, false);
            }
            catch (Exception ex)
            {
                _logger.Write(LOG_TAG, ex.Message);
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