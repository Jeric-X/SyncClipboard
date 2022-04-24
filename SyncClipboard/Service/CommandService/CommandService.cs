using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Utility;
using SyncClipboard.Module;
using System.Text.Json;
#nullable enable
namespace SyncClipboard.Service
{
    internal class CommandService : Service
    {
        private const string COMMAND_FILE = "Command.json";
        public class Command
        {
            public string CommandStr { get; set; } = "";
            public string Time { get; set; } = "";
        }

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
                    Command command = await GetRemoteCommand().ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(command.CommandStr))
                    {
                        await ResetRemoteCommand(new Command()).ConfigureAwait(false);
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

                await Task.Delay(UserConfig.Config.Program.IntervalTime, cancelToken).ConfigureAwait(false);
            }
            Log.Write("Command serivce exited");
        }

        private static async Task<Command> GetRemoteCommand()
        {
            Command? command;
            try
            {
                string str = await Global.WebDav.GetTextAsync(COMMAND_FILE).ConfigureAwait(false);
                command = JsonSerializer.Deserialize<Command>(str);
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

        private static Task ExecuteCommand(Command command)
        {
            return Task.Run(() =>
            {
                if (command.CommandStr == "shutdown")
                {
                    var shutdownTime = UserConfig.Config.CommandService.Shutdowntime;

                    var process = new System.Diagnostics.Process();
                    process.StartInfo.FileName = "cmd";
                    process.StartInfo.Arguments = $@"/k shutdown.exe /s /t {shutdownTime} /c ""use [ shutdown /a ] in {shutdownTime}s to undo shutdown.""";
                    process.Start();
                }
            });
        }

        private static async Task ResetRemoteCommand(Command command)
        {
            try
            {
                await Global.WebDav.PutTextAsync(COMMAND_FILE, JsonSerializer.Serialize(command)).ConfigureAwait(false);
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
