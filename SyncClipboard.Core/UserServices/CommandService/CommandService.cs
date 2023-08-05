using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
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
    private readonly UserConfig2 _userConfig;
    private readonly IWebDav _webDav;
    private readonly IContextMenu _contextMenu;

    private CommandConfig _commandConfig;

    private bool SwitchOn
    {
        get => _commandConfig.SwitchOn;
        set
        {
            _commandConfig.SwitchOn = value;
            _userConfig.SetConfig(ConfigKey.Command, _commandConfig);
        }
    }

    private uint ShutdownDelay => _commandConfig.Shutdowntime;
    private uint IntervalTime => _commandConfig.IntervalTime;

    public CommandService(IServiceProvider serviceProvider)
    {
        _notificationManager = serviceProvider.GetRequiredService<NotificationManager>();
        _logger = serviceProvider.GetRequiredService<ILogger>();
        _userConfig = serviceProvider.GetRequiredService<UserConfig2>();
        _webDav = serviceProvider.GetRequiredService<IWebDav>();
        _contextMenu = serviceProvider.GetRequiredService<IContextMenu>();

        _commandConfig = _userConfig.GetConfig<CommandConfig>(ConfigKey.Command) ?? new();

        _toggleMenuItem = new ToggleMenuItem(
            "Remote Command",
            _commandConfig.SwitchOn,
            (status) => SwitchOn = status
        );

        _userConfig.ListenConfig<CommandConfig>(ConfigKey.Command,
            (config) =>
            {
                _commandConfig = (config as CommandConfig) ?? new();
                _toggleMenuItem.Checked = SwitchOn;
            });
    }

    protected override void StartService()
    {
        _contextMenu.AddMenuItem(_toggleMenuItem);

        try
        {
            _ = StartServiceAsync(_cancelSource.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.Write(LOG_TAG, "Command serivce exited");
        }
    }

    protected override void StopSerivce()
    {
        _cancelSource?.Cancel();
    }

    private async Task StartServiceAsync(CancellationToken cancelToken)
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
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await ResetRemoteCommand(new CommandInfo());
                }
                catch (Exception ex) when (!_isError)
                {
                    Console.WriteLine(ex.Message);
                    _notificationManager.SendText("CommandService failed", ex.ToString());
                    _isError = true;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(IntervalTime), cancelToken);
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
                return new TaskShutdown(command, _notificationManager, ShutdownDelay).ExecuteAsync();
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
