using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Utility;

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

        protected override void StartService()
        {
            _cancelSource = new CancellationTokenSource();
            _cancelToken = _cancelSource.Token;
            StartServiceAsync(_cancelToken);
        }

        protected override void StopSerivce()
        {
            _cancelSource.Cancel();
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
                    await ResetRemoteCommand(new Command()).ConfigureAwait(false);
                    ExecuteCommand(command);
                }
                catch (System.Exception ex)
                {
                    Program.notifyer.ToastNotify("CommandService failed", ex.ToString());
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
                string str = await Program.webDav.GetTextAsync(COMMAND_FILE, 0, 0).ConfigureAwait(false);
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
                System.Diagnostics.Process open = new System.Diagnostics.Process();
                open.StartInfo.FileName = "cmd";
                open.StartInfo.Arguments = @"/k shutdown.exe /s /t 60 /c ""use [ shutdown /a ] in 60s to undo shutdown.""";
                open.Start();
            }
        }

        private async Task ResetRemoteCommand(Command command)
        {
            try
            {
                await Program.webDav.PutTextAsync(COMMAND_FILE, Json.Encode(command), 0, 0).ConfigureAwait(false);
            }
            catch
            {
                Log.Write("Reset command failed");
                throw new System.Exception("Reset command failed");
            }
            Log.Write($"Command [{command.CommandStr}] has reset");
        }
    }
}
