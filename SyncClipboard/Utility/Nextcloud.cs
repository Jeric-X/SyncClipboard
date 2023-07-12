using SyncClipboard.Control;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SyncClipboard.Utility;

public static class Nextcloud
{
    public static async void LogWithNextcloud()
    {
        WebDavCredential nextcloudInfo = await SignInFlowAsync();
        if (nextcloudInfo is null)
        {
            return;
        }

        Global.UserConfig.Config.SyncService.UserName = nextcloudInfo.Username;
        Global.UserConfig.Config.SyncService.Password = nextcloudInfo.Password;
        Global.UserConfig.Config.SyncService.RemoteURL = nextcloudInfo.Url;
        Global.UserConfig.Save();
    }

    public static async Task<WebDavCredential> SignInFlowAsync()
    {
        try
        {
            return await SignInFlow();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<WebDavCredential> SignInFlow()
    {
        string server = InputBox.Show("Please input Nextcloud server address", $"https://[请在确定后{NextcloudLogInFlow.VERIFICATION_LIMITED_TIME / 1000}秒内完成网页认证]");
        ArgumentNullException.ThrowIfNull(server);

        var loginFlow = new NextcloudLogInFlow(server, Global.Http);
        var userloginUrl = await GetLoginUrl(loginFlow);

        Sys.OpenWithDefaultApp(userloginUrl);

        var credential = await WaitUserLogin(loginFlow);

        var path = InputBox.Show("Please input syncClipboard folder path");
        ArgumentNullException.ThrowIfNull(path);

        return credential with
        {
            Url = $"{credential.Url}/{path}"
        };
    }

    private static async Task<string> GetLoginUrl(NextcloudLogInFlow loginFlow)
    {
        try
        {
            return await loginFlow.GetUserLoginUrl();
        }
        catch (HttpRequestException)
        {
            MessageBox.Show("认证中发生错误：" + Environment.NewLine + "Can not connect to the server", "Error");
            throw;
        }
        catch (UriFormatException)
        {
            MessageBox.Show("认证中发生错误：" + Environment.NewLine + "URL format is wrong", "Error");
            throw;
        }
        catch (Exception ex)
        {
            MessageBox.Show("认证中发生错误：" + Environment.NewLine + ex.Message, "Error");
            throw;
        }
    }

    private static async Task<WebDavCredential> WaitUserLogin(NextcloudLogInFlow loginFlow)
    {
        try
        {
            return await loginFlow.WaitUserLogin();
        }
        catch
        {
            MessageBox.Show("认证中发生错误：" + Environment.NewLine + "Out of time", "Error");
            throw;
        }
    }
}