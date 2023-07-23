using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.UserServices.Command;
using SyncClipboard.Core.Utilities.Notification;

namespace SyncClipboard.Core.UserServices;

public class CommandService : Service
{
    private const string COMMAND_FILE = "Command.json";
    private const string LOG_TAG = "Command";
    private readonly CancellationTokenSource _cancelSource = new();
    private bool _isError = false;
    private readonly ToggleMenuItem _toggleMenuItem;

    private readonly NotificationManager _notificationManager;
    private readonly ILogger _logger;
    private readonly UserConfig _userConfig;
    private readonly IWebDav _webDav;
    private readonly IContextMenu _contextMenu;

    private bool SwitchOn
    {
        get => _userConfig.Config.CommandService.SwitchOn;
        set
        {
            _userConfig.Config.CommandService.SwitchOn = value;
            _userConfig.Save();
        }
    }

    public CommandService(IServiceProvider serviceProvider)
    {
        _notificationManager = serviceProvider.GetRequiredService<NotificationManager>();
        _logger = serviceProvider.GetRequiredService<ILogger>();
        _userConfig = serviceProvider.GetRequiredService<UserConfig>();
        _webDav = serviceProvider.GetRequiredService<IWebDav>();
        _contextMenu = serviceProvider.GetRequiredService<IContextMenu>();
        _toggleMenuItem = new ToggleMenuItem(
            "Remote Command",
            _userConfig.Config.CommandService.SwitchOn,
            (status) => SwitchOn = status
        );
    }

    protected override void StartService()
    {
        _contextMenu.AddMenuItem(_toggleMenuItem);

        try
        {
            StartServiceAsync(_cancelSource.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.Write(LOG_TAG, "Command serivce exited");
        }
    }

    public override void Load()
    {
        _toggleMenuItem.Checked = SwitchOn;
    }

    protected override void StopSerivce()
    {
        _cancelSource?.Cancel();
    }

    private async void StartServiceAsync(CancellationToken cancelToken)
    {
        while (true)
        {
            if (SwitchOn)
            {
                try
                {
                    CommandInfo command = await GetRemoteCommand();
                    if (!string.IsNullOrEmpty(command.CommandStr))
                    {
                        await ResetRemoteCommand(new CommandInfo());
                        await ExecuteCommand(command);
                    }
                    _isError = false;
                }
                catch (Exception ex)
                {
                    if (!_isError)
                    {
                        Console.WriteLine(ex.Message);
                        _notificationManager.SendText("CommandService failed", ex.ToString());
                        _isError = true;
                    }
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(_userConfig.Config.Program.IntervalTime), cancelToken);
        }
    }

    private async Task<CommandInfo> GetRemoteCommand()
    {
        CommandInfo? command;
        try
        {
            command = await _webDav.GetJson<CommandInfo>(COMMAND_FILE);
            ArgumentNullException.ThrowIfNull(command);
        }
        catch
        {
            _logger.Write("Get command failed");
            throw;
        }
        _logger.Write($"Command is [{command.CommandStr}]");
        return command;
    }

    private Task ExecuteCommand(CommandInfo command)
    {
        return Task.Run(() =>
        {
            if (command.CommandStr == "shutdown")
            {
                return new TaskShutdown(command, _notificationManager, _userConfig).ExecuteAsync();
            }
            return Task.CompletedTask;
        });
    }

    private async Task ResetRemoteCommand(CommandInfo command)
    {
        try
        {
            await _webDav.PutJson(COMMAND_FILE, command);
        }
        catch
        {
            _logger.Write("Reset command failed");
            throw;
        }
        _logger.Write($"Command [{command.CommandStr}] has reset");
    }
}
