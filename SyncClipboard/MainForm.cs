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
using Microsoft.Win32;

       
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
            try
            {
                remoteURL = Properties.Settings.Default.URL;
                user = Properties.Settings.Default.USERNAME;
                password = Properties.Settings.Default.PASSWORD;
            }
            catch { MessageBox.Show("配置文件出错","初始化默认配置",MessageBoxButtons.YesNo); Application.Exit(); return; }
            if(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Program.softName, null) == null)
            {
                this.开机启动ToolStripMenuItem.Checked = false;
            }
            else
            {
                this.开机启动ToolStripMenuItem.Checked = true;
            }
        }
        private void setLog(bool notify,bool notifyIconText,string title,string content,string contentSimple)
        {
            if(notify)
            {
                this.notifyIcon1.ShowBalloonTip(5, title, content, ToolTipIcon.None);
            }
            if (notifyIconText)
            {
                this.notifyIcon1.Text = Program.softName + "\n" + title + "\n" + contentSimple;
            }
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
                if (statusErroFlag || timeoutFlag)
                {
                    setLog(true, true, "连接服务器成功", "正在同步", "正在同步");
                    timeoutFlag = false;
                }
                if (httpWebResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    erroTimes += 1;
                    if (erroTimes < retryTimes)
                    {
                        setLog(true, true, "服务器状态错误：" + httpWebResponse.StatusCode.ToString(), "重试次数:" + erroTimes.ToString(), "重试次数:" + erroTimes.ToString());
                    }
                    else
                    {
                        setLog(false, true, "服务器状态错误：" + httpWebResponse.StatusCode.ToString(), "重试次数:" + erroTimes.ToString(), "重试次数:" + erroTimes.ToString());
                    }
                    statusErroFlag = true;
                }
                statusErroFlag = false;
            }
            catch(Exception ex)
            {
                getTimeoutTimes += 1;
                timeoutFlag = true;
                if (getTimeoutTimes < retryTimes)
                {
                    Console.WriteLine(ex.ToString());
                    setLog(true, true, ex.Message.ToString(), url + "\n重试次数:" + getTimeoutTimes.ToString(),"重试次数:" + getTimeoutTimes.ToString());
                }
                else
                {
                    Console.WriteLine(ex.ToString());
                    setLog(false, true, ex.Message.ToString(), url + "\n重试次数:" + getTimeoutTimes.ToString(), "重试次数:" + getTimeoutTimes.ToString());
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
                setLog(true,false, "剪切板同步成功", msgString, null);  
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
                setLog(true, false,"连接服务器超时", "未同步：" + msgString,null);
                return;
            }
            if (httpWebResponse.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                //this.notifyIcon1.ShowBalloonTip(5, "剪切板同步到云", httpWebResponse.StatusCode.GetHashCode().ToString(), ToolTipIcon.None);
            }
            else
            {
                setLog(true, false,"剪切板同步失败", httpWebResponse.StatusCode.GetHashCode().ToString(),null);
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

        private void 开机启动ToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            try 
            { 
                if(this.开机启动ToolStripMenuItem.Checked == true)
                {
                    Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Program.softName, Application.ExecutablePath);
                }
                else
                {
                    Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true).DeleteValue(Program.softName, false);
                }
            }
            catch
            {
                setLog(true, false, "设置启动项失败", "设置启动项失败", null);
            }
        }
    }
   
    class ConvertJson
    {
        public string platform { get; set; }
        public string str { get; set; }
    }
}
