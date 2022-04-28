using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Utility;
using SyncClipboard.Module;
using SyncClipboard.Service.Command;
#nullable enable
namespace SyncClipboard.Service
{
    internal class CommandService : Service
    {
        private const string COMMAND_FILE = "Command.json";

        private readonly CancellationTokenSource _cancelSource = new();
        private bool _isError = false;

        protected override void StartService()
        {
            if (UserConfig.Config.CommandService.SwitchOn)
            {
                StartServiceAsync(_cancelSource.Token);
            }
        }

        protected override void StopSerivce()
        {
            _cancelSource?.Cancel();
        }

        private async void StartServiceAsync(CancellationToken cancelToken)
        {
            while (this.Enabled)
            {
                if (cancelToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    CommandInfo command = await GetRemoteCommand().ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(command.CommandStr))
                    {
                        await ResetRemoteCommand(new CommandInfo()).ConfigureAwait(false);
                        await ExecuteCommand(command);
                    }

                    _isError = false;
                }
                catch (System.Exception ex)
                {
                    if (!_isError)
                    {
                        System.Console.WriteLine(ex.Message);
                        Global.Notifyer.ToastNotify("CommandService failed", ex.ToString());
                        _isError = true;
                    }
                }

                await Task.Delay(System.TimeSpan.FromSeconds(UserConfig.Config.Program.IntervalTime)).ConfigureAwait(false);
            }
            Log.Write("Command serivce exited");
        }

        private static async Task<CommandInfo> GetRemoteCommand()
        {
            CommandInfo? command;
            try
            {
                command = await Global.WebDav.GetJson<CommandInfo>(COMMAND_FILE);
                System.ArgumentNullException.ThrowIfNull(command);
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
