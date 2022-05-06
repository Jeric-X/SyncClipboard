using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Utility;
using SyncClipboard.Module;
using SyncClipboard.Service.Command;
using System;
using SyncClipboard.Utility.Notification;
#nullable enable
namespace SyncClipboard.Service
{
    internal class CommandService : Service
    {
        private event Action<bool>? SwitchChanged;
        private const string COMMAND_FILE = "Command.json";
        private const string LOG_TAG = "Command";
        private readonly CancellationTokenSource _cancelSource = new();
        private bool _isError = false;

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
                Log.Write(LOG_TAG, "Command serivce exited");
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
                            Toast.SendText("CommandService failed", ex.ToString());
                            _isError = true;
                        }
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(UserConfig.Config.Program.IntervalTime), cancelToken);
            }
        }

        private static async Task<CommandInfo> GetRemoteCommand()
        {
            CommandInfo? command;
            try
            {
                command = await Global.WebDav.GetJson<CommandInfo>(COMMAND_FILE);
                ArgumentNullException.ThrowIfNull(command);
            }
            catch
            {
                Log.Write("Get command failed");
                throw;
            }
            Log.Write($"Command is [{command.CommandStr}]");
            return command;
        }

        private static Task ExecuteCommand(CommandInfo command)
        {
            return Task.Run(() =>
            {
                if (command.CommandStr == "shutdown")
                {
                    return new TaskShutdown(command).ExecuteAsync();
                }
                return Task.CompletedTask;
            });
        }

        private static async Task ResetRemoteCommand(CommandInfo command)
        {
            try
            {
                await Global.WebDav.PutJson(COMMAND_FILE, command);
            }
            catch
            {
                Log.Write("Reset command failed");
                throw;
            }
            Log.Write($"Command [{command.CommandStr}] has reset");
        }
    }
}
