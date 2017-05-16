using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.Net;
using System.IO;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;

       
namespace SyncClipboard
{
    public partial class MainForm : Form
    {
        [DllImport("user32.dll")]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        private static int WM_CLIPBOARDUPDATE = 0x031D;

        private String remoteURL;
        private String user;
        private String password;
        bool ifsettingsFormExist = false;
        
        private String stringOld = "";
        private Thread pullThread;
        private int timeLoop = 3000;
        private int erroTimes = 0;
        private int getTimeoutTimes = 0;
        private int retryTimes = 3;
        bool statusErroFlag = false;
        bool timeoutFlag = false;
        private bool stopFlag = false;
        private bool firstFlag = true;
        public MainForm()
        {
            InitializeComponent();
            this.TopLevel = false;
            AddClipboardFormatListener(this.Handle);
            this.LoadConfig();
            pullThread = new Thread(PullLoop);
            pullThread.SetApartmentState(ApartmentState.STA);
            pullThread.Start();
        }
        public void LoadConfig()
        {
            remoteURL = Properties.Settings.Default.URL;
            user = Properties.Settings.Default.USERNAME;
            password = Properties.Settings.Default.PASSWORD;
        }

        private void 退出ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RemoveClipboardFormatListener(this.Handle);  
            stopFlag = true;
            pullThread.Abort();
            Application.Exit();
        }

        private void PullLoop()
        {
            while (!stopFlag)
            {
                PullFromRemote();
                Thread.Sleep(timeLoop);
            }
        }
        private void PullFromRemote()
        {
            String url = this.remoteURL + "ios.json";
            String auth = "Authorization: Basic " + this.user;
            HttpWebResponse httpWebResponse = null;
            try
            {
                httpWebResponse = HttpWebResponseUtility.CreateGetHttpResponse(url, 5000, null, auth, null);
                if (timeoutFlag)
                {
                    this.notifyIcon1.ShowBalloonTip(5, "重新连接服务器成功", "正在同步", ToolTipIcon.None);
                    timeoutFlag = false;
                }
                if (httpWebResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    erroTimes += 1;
                    if (erroTimes > retryTimes)
                    {
                        this.notifyIcon1.ShowBalloonTip(5, "服务器状态错误：" + httpWebResponse.StatusCode.ToString(), "重试次数" + erroTimes.ToString(), ToolTipIcon.None);
                    }
                    statusErroFlag = true;
                }
            }
            catch(Exception ex)
            {
                getTimeoutTimes += 1;
                timeoutFlag = true;
                if (getTimeoutTimes > retryTimes)
                {
                    Console.WriteLine(ex.ToString());
                    this.notifyIcon1.ShowBalloonTip(5, ex.Message.ToString() , url + "\n重试次数" + getTimeoutTimes.ToString(), ToolTipIcon.None);
                }
            }

            if (statusErroFlag || timeoutFlag)
            {
                try { httpWebResponse.Close(); }
                catch { }
                return;
            }
            erroTimes = getTimeoutTimes = 0;
            StreamReader objStrmReader = new StreamReader(httpWebResponse.GetResponseStream());
            String strReply = objStrmReader.ReadToEnd();
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            var p1 = serializer.Deserialize<ConvertJson>(strReply);
            if (p1.str != stringOld)
            {
                if(firstFlag)
                {
                    firstFlag = false;
                    stringOld = p1.str;
                    return;
                }
                Clipboard.SetData(DataFormats.Text, p1.str);
                stringOld = p1.str;
                string msgString;
                try
                {
                    msgString = stringOld.Substring(0, 20) + "...";
                }
                catch
                {
                    msgString = stringOld;
                }
                this.notifyIcon1.ShowBalloonTip(5, "剪切板同步成功", msgString, ToolTipIcon.None);  
            }
            try { httpWebResponse.Close(); }
            catch { }
        }

        private void PushToRemote()
        {
            bool timeoutFlag = false;
            IDataObject iData = Clipboard.GetDataObject();
            if (!iData.GetDataPresent(DataFormats.Text))
            {
                return;
            }
            string str = (String)iData.GetData(DataFormats.Text);
            ConvertJson convertJson = new ConvertJson();
            convertJson.platform = "Windows";
            convertJson.str = str;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string jsonString = serializer.Serialize(convertJson);

            String url = this.remoteURL + "Windows.json";
            String auth = "Authorization: Basic " + this.user;
            HttpWebResponse httpWebResponse = null;
            try
            {
                httpWebResponse = HttpWebResponseUtility.CreatePutHttpResponse(url, str, 5000, null, auth, null, null);
            }
            catch
            {
                timeoutFlag = true;
            }
            string msgString;
            try
            {
                msgString = str.Substring(0, 20) + "...";
            }
            catch
            {
                msgString = str;
            }
            if (timeoutFlag)
            {
                this.notifyIcon1.ShowBalloonTip(5, "连接服务器超时", "未同步：" + msgString, ToolTipIcon.None);
                return;
            }
            if (httpWebResponse.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                //this.notifyIcon1.ShowBalloonTip(5, "剪切板同步到云", httpWebResponse.StatusCode.GetHashCode().ToString(), ToolTipIcon.None);
            }
            else
            {
                this.notifyIcon1.ShowBalloonTip(5, "剪切板同步失败", httpWebResponse.StatusCode.GetHashCode().ToString(), ToolTipIcon.None);
            }
        }
        protected override void DefWndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                PushToRemote();
            }
            else
            {
                base.DefWndProc(ref m);
            }
        }

        private void 设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ifsettingsFormExist)
            {
                ifsettingsFormExist = true;
                new SettingsForm(this).ShowDialog();
                ifsettingsFormExist = false;
            }
        }
    }
   
    class ConvertJson
    {
        public string platform { get; set; }
        public string str { get; set; }
    }
}
