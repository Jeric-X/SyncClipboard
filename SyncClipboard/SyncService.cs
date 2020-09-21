using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using SyncClipboard.Control;
using System.Drawing;

namespace SyncClipboard
{
    public class SyncService
    {
        private Control.MainController mainController;
        private Thread pullThread;
        private String oldString = "";

        public bool isStop = false;

        private bool isTimeOut = false;
        private bool isFirstTime = true;
        private String pushErrorMessage;

        private int errorTimes = 0;
        private int pullTimeoutTimes = 0;
        private PushService pushService = null;

        public SyncService(MainController mf)
        {
            this.mainController = mf;
            pushService = new PushService(this.mainController.setLog);
        }
        public void Start()
        {
            pullThread = new Thread(PullLoop);
            pullThread.SetApartmentState(ApartmentState.STA);
            pullThread.Start();
        }

        public void Stop()
        {
            isStop = true;
            try
            {
                pullThread.Abort();
            }
            catch { }
        }

        private void PullLoop()
        {
            while (!this.isStop)
            {
                if (Config.IfPull)
                    this.PullFromRemote();
                Thread.Sleep((int)Config.IntervalTime);
            }
        }
        private void PullFromRemote()
        {
            String url = Config.GetProfileUrl();
            String auth = Config.GetHttpAuthHeader();

            Console.WriteLine (auth +"dd");
            HttpWebResponse httpWebResponse = GetPullResponse(url, auth);
            if (this.isTimeOut)
            {
                try { httpWebResponse.Close(); }
                catch { }
                return;
            }
            errorTimes = this.pullTimeoutTimes = 0;
            StreamReader objStrmReader = new StreamReader(httpWebResponse.GetResponseStream());
            String strReply = objStrmReader.ReadToEnd();
            Profile profile = new Profile(strReply);

            if (profile.Text != this.oldString)
            {
                Clipboard.SetData(DataFormats.Text, profile.Text);
                this.oldString = profile.Text;
                this.mainController.setLog(true, false, "剪切板同步成功", oldString, null, "info");
            }
            this.mainController.setLog(false, true, "服务器连接成功", null, "正在同步", "info");
            try { httpWebResponse.Close(); }
            catch { }
        }
        private HttpWebResponse GetPullResponse(String url, String auth)
        {
            HttpWebResponse httpWebResponse = null;
            try
            {
                Console.WriteLine("pull start " + DateTime.Now.ToString());
                httpWebResponse = HttpWebResponseUtility.CreateGetHttpResponse(url, Config.TimeOut, auth, !this.isTimeOut);
                Console.WriteLine("pull end " + DateTime.Now.ToString());
                this.isTimeOut = false;
            }
            catch (Exception ex)
            {
                this.pullTimeoutTimes += 1;
                isTimeOut = true;
                Console.WriteLine(ex.ToString());
                this.mainController.setLog(false, true, ex.Message.ToString(), url + "\n重试次数:" + this.pullTimeoutTimes.ToString(), "重试次数:" + this.pullTimeoutTimes.ToString(), "erro");
            }
            return httpWebResponse;
        }
    }
}
