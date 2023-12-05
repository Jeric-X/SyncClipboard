using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Image;

namespace SyncClipboard.Core.UserServices;

public class ConvertService : ClipboardHander
{
    #region override ClipboardHander

    public override string SERVICE_NAME => I18n.Strings.ImageCompatibility;
    public override string LOG_TAG => "COMPATIBILITY";

    protected override IContextMenu? ContextMenu => _serviceProvider.GetRequiredService<IContextMenu>();
    protected override IClipboardChangingListener ClipboardChangingListener
                                                  => _serviceProvider.GetRequiredService<IClipboardChangingListener>();
    protected override ILogger Logger => _logger;
    protected override bool SwitchOn
    {
        get => _clipboardConfig.ConvertSwitchOn;
        set
        {
            _clipboardConfig.ConvertSwitchOn = value;
            _configManager.SetConfig(_clipboardConfig);
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
            var newPath = await CompatibilityCast(_serviceProvider, file, cancellationToken);
            new ImageProfile(newPath, _serviceProvider).SetLocalClipboard(false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Write(LOG_TAG, ex.Message);
            return;
        }
    }

    #endregion

    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly IClipboardFactory _clipboardFactory;
    private readonly IServiceProvider _serviceProvider;

    private ClipboardAssistConfig _clipboardConfig;

    public ConvertService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>();

        _clipboardConfig = _configManager.GetConfig<ClipboardAssistConfig>();
        _configManager.ListenConfig<ClipboardAssistConfig>(config => _clipboardConfig = config);
    }

    private bool NeedAdjust(ClipboardMetaInfomation metaInfo)
    {
        if (_clipboardConfig.EasyCopyImageSwitchOn == false)
        {
            return false;
        }

        if (metaInfo.Files is null)
        {
            return false;
        }

        if (metaInfo.Files.Length != 1)
        {
            return false;
        }

        if ((metaInfo.Effects & DragDropEffects.Move) == DragDropEffects.Move)
        {
            return false;
        }

        if (!ImageHelper.IsComplexImage(metaInfo.Files[0]))
        {
            return false;
        }

        return true;
    }

    internal static async Task<string> CompatibilityCast(IServiceProvider services, string localPath, CancellationToken ctk)
    {
        var filename = Path.GetFileName(localPath);
        var notification = services.GetRequiredService<INotification>();
        var progressBar = notification.CreateProgressNotification(filename[..Math.Min(filename.Length, 50)]);
        try
        {
            progressBar.IsIndeterminate = true;
            progressBar.ProgressValueTip = "Converting";
            progressBar.Image = new Uri(localPath);
            progressBar.ShowSilent();
            return await ImageHelper.CompatibilityCast(localPath, Env.TemplateFileFolder, ctk);
        }
        catch
        {
            throw;
        }
        finally
        {
            progressBar.Remove();
        }
    }
}