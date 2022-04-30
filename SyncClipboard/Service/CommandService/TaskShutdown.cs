using System.Threading.Tasks;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Notification;
using SyncClipboard.Module;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
using System;

#nullable enable
namespace SyncClipboard.Service.Command
{
    public sealed class TaskShutdown
    {
        private readonly int shutdownTime;
        private readonly string tagName = "";
        private const string GROUP_NAME = "Command";
        private Counter? counter;
        private bool canceled = false;
        private static bool IsWorking = false;
        private static readonly object IsWorkingLocker = new();

        public TaskShutdown(CommandInfo info)
        {
            tagName = info.ToString() + DateTime.Now;
            shutdownTime = UserConfig.Config.CommandService.Shutdowntime;
        }

        public async Task ExecuteAsync()
        {
            lock (IsWorkingLocker)
            {
                if (IsWorking)
                {
                    return;
                }
                IsWorking = true;
            }

            var progressBar = InitToastProgressBar();

            counter = new Counter(
                (count) => progressBar.ForceUpdate((double)count / shutdownTime, $"{shutdownTime - count} 秒后关机"),
                (ulong)shutdownTime * 1000
            );

            await counter.WaitAsync();
            progressBar.Buttons.Clear();
            progressBar.ProgressValueTip = canceled ? "已取消" : "正在关机";
            if (!canceled)
            {
                progressBar.IsIndeterminate = true;
                ShutdowntPc();
            }

            progressBar.Show();
            lock (IsWorkingLocker)
            {
                IsWorking = false;
            }
        }

        private ProgressBar InitToastProgressBar()
        {
            var progressBar = new ProgressBar("远程命令")
            {
                Group = GROUP_NAME,
                Tag = tagName,
                ProgressStatus = "当前状态",
                ProgressTitle = "正在关机",
                ProgressValue = 0,
                ProgressValueTip = "准备开始倒计时"
            };
            progressBar.Buttons.Add(
                new Button("取消关机", new Callbacker($"{tagName}Cancel", _ => { canceled = true; counter?.Cancle(); })));
            progressBar.Buttons.Add(
                new Button("立刻关机", new Callbacker($"{tagName}RightNow", _ => counter?.Cancle())));
            return progressBar;
        }

        private static async void ShutdowntPc()
        {
            await Task.Run(() =>
            {
                // var shutdownTime = UserConfig.Config.CommandService.Shutdowntime;
                // var process = new System.Diagnostics.Process();
                // process.StartInfo.FileName = "cmd";
                // process.StartInfo.Arguments = @"/k shutdown.exe /s /t 5 /c ""use [ shutdown /a ] in 5s to undo shutdown.""";
                // process.Start();
                System.Windows.Forms.MessageBox.Show("临时关机", "临时关机2");
            });
        }
    }
}