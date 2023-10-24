using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SyncClipboard.Core.Utilities;

public class UpdateChecker
{
    public string Version => _appConfig.AppVersion;
    private const string GITHUB_JSON_VERSION_TAG = "name";
    private const int VersionPartNumber = 3;
    private string UpdateApiUrl => _appConfig.UpdateApiUrl;
    public string ReleaseUrl => _appConfig.UpdateUrl;

    private const string GITHUB_VERSION_PATTERN = @"^v\d+\.\d+\.\d+$";      // v2.250.666

    private readonly IHttp _http;
    private readonly IAppConfig _appConfig;

    public UpdateChecker(IHttp http, IAppConfig appConfig)
    {
        _http = http;
        _appConfig = appConfig;
    }

    public async Task<(bool, string)> Check()
    {
        var newVersion = await GetNewestVersion();
        return (NeedUpdate(newVersion), newVersion);
    }

    private async Task<string> GetNewestVersion()
    {
        string gitHubReply = await _http.GetHttpClient().GetStringAsync(UpdateApiUrl);
        string versionStr = JsonNode.Parse(gitHubReply)?[GITHUB_JSON_VERSION_TAG]?.GetValue<string>() ?? "";

        if (!Regex.IsMatch(versionStr, GITHUB_VERSION_PATTERN))
        {
            throw new Exception(I18n.Strings.FailToParseVersion);
        }

        return versionStr;
    }

    private bool NeedUpdate(string newVersionStr)
    {
        newVersionStr = newVersionStr[1..];         // 去除v1.0.0中的v
        string[] newVersion = newVersionStr.Split('.');
        string[] oldVersion = Version.Split('.');

        for (int i = 0; i < VersionPartNumber; i++)
        {
            int newVersionNum = Convert.ToInt32(newVersion[i]);
            int oldVersionNum = Convert.ToInt32(oldVersion[i]);
            if (newVersionNum > oldVersionNum)
            {
                return true;
            }
            if (newVersionNum < oldVersionNum)
            {
                return false;
            }
        }
        return false;
    }
}
