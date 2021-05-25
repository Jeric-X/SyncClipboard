using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Utility;
using SyncClipboard.Module;

namespace SyncClipboard.Service
{
    internal class CommandService : Service
    {
        private const string COMMAND_FILE = "Command.json";
        public class Command
        {
            public string CommandStr = "";
            public string Time = "";
        }

        private CancellationTokenSource _cancelSource;
        private CancellationToken _cancelToken;
        private bool _isError = false;

        protected override void StartService()
        {
            if (UserConfig.Config.CommandService.switchOn)
            {
                _cancelSource = new CancellationTokenSource();
                _cancelToken = _cancelSource.Token;
                StartServiceAsync(_cancelToken);
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
                        ExecuteCommand(command);
                    }

                    _isError = false;
                }
                catch (System.Exception ex)
                {
                    if (!_isError)
                    {
                        Global.Notifyer.ToastNotify("CommandService failed", ex.ToString());
                        _isError = true;
                    }
                }

                await Task.Delay(UserConfig.Config.Program.IntervalTime).ConfigureAwait(false);
            }
            Log.Write("Command serivce exited");
        }

        private async Task<Command> GetRemoteCommand()
        {
            Command command;
            try
            {
                string str = await Global.WebDav.GetTextAsync(COMMAND_FILE).ConfigureAwait(false);
                command = Json.Decode<Command>(str);
            }
            catch
            {
                Log.Write("Get command failed");
                throw new System.Exception("Get command failed");
            }
            Log.Write($"Command is [{command.CommandStr}]");
            return command;
        }

        private void ExecuteCommand(Command command)
        {
            if (command.CommandStr == "shutdown")
            {
                var shutdownTime = UserConfig.Config.CommandService.Shutdowntime;

                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "cmd";
                process.StartInfo.Arguments = $@"/k shutdown.exe /s /t {shutdownTime} /c ""use [ shutdown /a ] in {shutdownTime}s to undo shutdown.""";
                process.Start();
            }
        }

        private async Task ResetRemoteCommand(Command command)
        {
            try
            {
                await Global.WebDav.PutTextAsync(COMMAND_FILE, Json.Encode(command)).ConfigureAwait(false);
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
