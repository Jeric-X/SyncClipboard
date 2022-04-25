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
        private readonly CommandInfo commandInfo;
        private readonly ulong shutdownTime;
        private string tagName = "";
        private const string GROUP_NAME = "Command";
        private Counter? counter;
        private bool canceled = false;
        private readonly ToastNotifier notifer;

        public TaskShutdown(CommandInfo info)
        {
            tagName = info.ToString() + DateTime.Now;
            shutdownTime = (ulong)UserConfig.Config.CommandService.Shutdowntime;
            commandInfo = info;
            notifer = ToastNotificationManager.CreateToastNotifier(Global.AppUserModelId);
        }

        public async Task ExecuteAsync()
        {
            counter = new Counter(UpdateToast, shutdownTime);
            await counter.WaitAsync();
            if (canceled)
            {
                return;
            }
            await Task.Run(() =>
            {
                var shutdownTime = UserConfig.Config.CommandService.Shutdowntime;
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "cmd";
                process.StartInfo.Arguments = @"/k shutdown.exe /s /t 5 /c ""use [ shutdown /a ] in 5s to undo shutdown.""";
                process.Start();
            });
        }

        public void CancelCallback(string _)
        {
            canceled = true;
            counter?.Cancle();
            SetFinishToast();
        }
        public void RightNowCallback(string _)
        {
            counter?.Cancle();
            SetFinishToast();
        }

        private void SetFinishToast()
        {
            new ToastContentBuilder()
                .AddText("远程任务")
                .AddVisualChild(new AdaptiveProgressBar()
                {
                    Title = "电脑即将关机",
                    Value = canceled ? 0 : AdaptiveProgressBarValue.Indeterminate,
                    ValueStringOverride = canceled ? "已取消" : "正在关机",
                    Status = "当前状态"
                })
                .Show(toast =>
                {
                    toast.Tag = tagName;
                    toast.Group = GROUP_NAME;
                }
            );
        }

        public void SetToast(uint count)
        {
            tagName = commandInfo.ToString() + DateTime.Now.Millisecond;
            var content = new ToastContentBuilder()
                .SetToastScenario(ToastScenario.Reminder)
                .AddText("远程任务")
                .AddVisualChild(new AdaptiveProgressBar()
                {
                    Title = "电脑即将关机",
                    Value = new BindableProgressBarValue("progressValue"),
                    ValueStringOverride = new BindableString("progressValueString"),
                    Status = "当前状态"
                })
                .AddButton(new ToastButton().SetContent("取消关机"), $"{tagName}Cancel", CancelCallback)
                .AddButton(new ToastButton().SetContent("立刻关机"), $"{tagName}RightNow", RightNowCallback)
                .GetToastContent();

            var toast = new ToastNotification(content.GetXml())
            {
                Tag = tagName,
                Group = GROUP_NAME,
                Data = new NotificationData()
            };
            toast.Data.Values["progressValue"] = $"{(double)count / shutdownTime}";
            toast.Data.Values["progressValueString"] = $"{shutdownTime - count} 秒后关机";
            toast.Data.SequenceNumber = count;
            notifer.Show(toast);
        }

        public void UpdateToast(uint count)
        {
            var data = new NotificationData()
            {
                SequenceNumber = count
            };
            if (count == shutdownTime)
            {
                SetFinishToast();
                return;
            }
            else
            {
                data.Values["progressValueString"] = $"{shutdownTime - count} 秒后关机";
            }
            data.Values["progressValue"] = $"{(double)count / shutdownTime}";

            var rval = notifer.Update(data, tagName, GROUP_NAME);
            if (rval is NotificationUpdateResult.NotificationNotFound)
            {
                SetToast(count);
            }
        }
    }
}