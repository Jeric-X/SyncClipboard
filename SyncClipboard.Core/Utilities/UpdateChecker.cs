using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SyncClipboard.Core.Utilities;

public class UpdateChecker
{
    public const string Version = Env.VERSION;
    private const string GITHUB_JSON_VERSION_TAG = "name";
    private const int VersionPartNumber = 3;
    private const string UpdateApiUrl = "https://api.github.com/repos/Jeric-X/SyncClipboard/releases/latest";
    public const string ReleaseUrl = "https://github.com/Jeric-X/SyncClipboard/releases/latest";

    private const string GITHUB_VERSION_PATTERN = @"^v\d+\.\d+\.\d+$";      // v2.250.666

    private readonly IHttp _http;

    public UpdateChecker(IHttp http)
    {
        _http = http;
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
            throw new Exception("从Github解析version失败");
        }

        return versionStr;
    }

    private static bool NeedUpdate(string newVersionStr)
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
        }
        return false;
    }
}
