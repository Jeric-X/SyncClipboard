using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Notification;
using SyncClipboard.Module;
using SyncClipboard.Service.Command;
using System;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace SyncClipboard.Service
{
    internal class CommandService : Core.Interfaces.Service
    {
        private event Action<bool>? SwitchChanged;
        private const string COMMAND_FILE = "Command.json";
        private const string LOG_TAG = "Command";
        private readonly CancellationTokenSource _cancelSource = new();
        private bool _isError = false;

        private readonly NotificationManager _notificationManager;
        private readonly ILogger _logger;

        public CommandService(NotificationManager notificationManager, ILogger logger)
        {
            _notificationManager = notificationManager;
            _logger = logger;
        }

        protected override void StartService()
        {
            SwitchChanged += Global.Menu.AddMenuItemGroup(
                new string[] { "Remote Command" },
                new Action<bool>[] {
                    (switchOn) => {
                        UserConfig.Config.CommandService.SwitchOn = switchOn;
                        UserConfig.Save();
                    }
                }
            )[0];
            SwitchChanged?.Invoke(UserConfig.Config.CommandService.SwitchOn);

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
            SwitchChanged?.Invoke(UserConfig.Config.CommandService.SwitchOn);
        }

        protected override void StopSerivce()
        {
            _cancelSource?.Cancel();
        }

        private async void StartServiceAsync(CancellationToken cancelToken)
        {
            while (true)
            {
                if (UserConfig.Config.CommandService.SwitchOn)
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

                await Task.Delay(TimeSpan.FromSeconds(UserConfig.Config.Program.IntervalTime), cancelToken);
            }
        }

        private async Task<CommandInfo> GetRemoteCommand()
        {
            CommandInfo? command;
            try
            {
                command = await Global.WebDav.GetJson<CommandInfo>(COMMAND_FILE);
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
                    return new TaskShutdown(command, _notificationManager).ExecuteAsync();
                }
                return Task.CompletedTask;
            });
        }

        private async Task ResetRemoteCommand(CommandInfo command)
        {
            try
            {
                await Global.WebDav.PutJson(COMMAND_FILE, command);
            }
            catch
            {
                _logger.Write("Reset command failed");
                throw;
            }
            _logger.Write($"Command [{command.CommandStr}] has reset");
        }
    }
}
