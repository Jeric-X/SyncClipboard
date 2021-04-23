using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Utility;

namespace SyncClipboard.Service
{
    class CommandService : Service
    {
        private const string COMMAND_FILE = "Command.json";
        public class Command 
        {
            public string CommandStr = "";
            public string Time = "";
        }

        CancellationTokenSource _cancelSource;
        CancellationToken _cancelToken;

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

                Command command = await GetRemoteCommand();
                await ResetRemoteCommand(new Command());
                ExecuteCommand(command);

                await Task.Delay(UserConfig.Config.Program.IntervalTime);
            }
            Log.Write("Command serivce exit");
        }

        private async Task<Command> GetRemoteCommand()
        {
            Command command = new Command();
            try
            {
                string str = await Program.webDav.GetTextAsync(COMMAND_FILE, 0, 0);
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
                open.StartInfo.Arguments = @"/k shutdown.exe /s /t 120 /c ""use [ shutdown /a ] in 120s to undo shutdown.""";
                open.Start();
            }
        }

        private async Task ResetRemoteCommand(Command command)
        {
            try
            {
                await Program.webDav.PutTextAsync(COMMAND_FILE, Json.Encode(command), 0, 0);
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
