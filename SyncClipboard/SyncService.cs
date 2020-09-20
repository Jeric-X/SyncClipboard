using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SyncClipboard.Control;
using System.Drawing;

namespace SyncClipboard
{
    class SyncService
    {
        private Control.MainController mainController;
        private Thread pullThread;
        private Thread pushThread;
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
        public void StartPush()
        {
            if (pushThread != null)
                pushThread.Abort();
            pushThread = new Thread(PushLoop);
            pushThread.SetApartmentState(ApartmentState.STA);
            pushThread.Start();
        }

        public void Stop()
        {
            isStop = true;
            try
            {
                pullThread.Abort();
                pushThread.Abort();
            }
            catch { }
        }

        public void OpenURL()
        {
            if(this.oldString.Length < 4)
                return;
            if(this.oldString.Substring(0,4) == "http")
                System.Diagnostics.Process.Start(this.oldString);  
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
            String auth = "Authorization: Basic " + Config.Auth;

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
                if (this.isFirstTime)
                {
                    this.isFirstTime = false;
                    this.oldString = profile.Text;
                    return;
                }
                Clipboard.SetData(DataFormats.Text, profile.Text);
                this.oldString = profile.Text;
                this.mainController.setLog(true, false, "剪切板同步成功", this.SafeMessage(oldString), null, "info");
            }
            else
            {
                this.mainController.setLog(false, true, "服务器连接成功", null, "正在同步", "info");
            }
            try { httpWebResponse.Close(); }
            catch { }
        }
        private HttpWebResponse GetPullResponse(String url, String auth)
        {
            HttpWebResponse httpWebResponse = null;
            try
            {
                Console.WriteLine("pull start " + DateTime.Now.ToString());
                httpWebResponse = HttpWebResponseUtility.CreateGetHttpResponse(url, Config.TimeOut, null, auth);
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
        public void PushLoop()
        {
            Console.WriteLine("Push start "+ DateTime.Now.ToString());
            IDataObject ClipboardData = Clipboard.GetDataObject();
            if (!ClipboardData.GetDataPresent(DataFormats.Text) && !ClipboardData.GetDataPresent(DataFormats.Bitmap))
            {
                return;
            }
            string str = Clipboard.GetText();
            Image image = Clipboard.GetImage();
            bool isImage = Clipboard.ContainsImage();

            for (int i = 0; i < Config.RetryTimes; i++)
            {
                if(this.isStop || (!Config.IfPush))
                {
                    return;
                }

                try
                {
                    if (isImage)
                    {
                        pushService.PushImage(image);
                    }
                    pushService.PushProfile(str, isImage);
                }
                catch(Exception ex)
                {
                    this.pushErrorMessage = ex.Message.ToString();
                    continue;
                }

                Console.WriteLine("Push end " + DateTime.Now.ToString());
                return;
            }
            this.mainController.setLog(true, false, this.pushErrorMessage, "未同步：" + this.SafeMessage(str), null, "erro");
        }

        private String SafeMessage(String str)
        {
            string msgString;
            try
            {
                msgString = str.Substring(0, 40) + "...";
            }
            catch
            {
                msgString = str;
            }
            return msgString;
        }
    }
}
