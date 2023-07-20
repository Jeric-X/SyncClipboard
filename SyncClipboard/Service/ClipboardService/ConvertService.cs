using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.Image;
using System;
using System.Threading;
#nullable enable

namespace SyncClipboard.Service
{
    public class ConvertService : ClipboardHander
    {
        #region override ClipboardHander

        public override string SERVICE_NAME => "图片兼容性优化";
        public override string LOG_TAG => "COMPATIBILITY";

        protected override IContextMenu? ContextMenu => _serviceProvider.GetRequiredService<IContextMenu>();
        protected override IClipboardChangingListener ClipboardChangingListener
                                                      => _serviceProvider.GetRequiredService<IClipboardChangingListener>();
        protected override ILogger Logger => _logger;
        protected override bool SwitchOn
        {
            get => _userConfig.Config.ClipboardService.ConvertSwitchOn;
            set
            {
                _userConfig.Config.ClipboardService.ConvertSwitchOn = value;
                _userConfig.Save();
            }
        }

        protected override async void HandleClipboard(ClipboardMetaInfomation metaInfo, CancellationToken cancellationToken)
        {
            var clipboardProfile = _clipboardFactory.CreateProfile(metaInfo);
            if (clipboardProfile.Type != ProfileType.File || !NeedAdjust(metaInfo))
            {
                return;
            }

            try
            {
                var file = metaInfo.Files![0];
                var newPath = await ImageHelper.CompatibilityCast(file, SyncService.LOCAL_FILE_FOLDER, cancellationToken);
                new ImageProfile(newPath, _serviceProvider).SetLocalClipboard();
            }
            catch (Exception ex)
            {
                _logger.Write(LOG_TAG, ex.Message);
                return;
            }
        }

        #endregion

        private readonly ILogger _logger;
        private readonly UserConfig _userConfig;
        private readonly IClipboardFactory _clipboardFactory;
        private readonly IServiceProvider _serviceProvider;

        public ConvertService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _logger = _serviceProvider.GetRequiredService<ILogger>();
            _userConfig = _serviceProvider.GetRequiredService<UserConfig>();
            _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>(); ;
        }

        private static bool NeedAdjust(ClipboardMetaInfomation metaInfo)
        {
            if (metaInfo.Files is null)
            {
                return false;
            }

            if (metaInfo.Files.Length != 1)
            {
                return false;
            }

            if (!ImageHelper.IsComplexImage(metaInfo.Files[0]))
            {
                return false;
            }

            return true;
        }
    }
}