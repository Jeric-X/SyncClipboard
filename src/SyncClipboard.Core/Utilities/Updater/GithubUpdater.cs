using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using System.Net.Http.Json;

namespace SyncClipboard.Core.Utilities.Updater;

public class GithubUpdater
{
    public string Version => _appConfig.AppVersion;
    private string UpdateApiUrl => _appConfig.UpdateApiUrl;

    private readonly IHttp _http;
    private readonly IAppConfig _appConfig;
    private readonly ConfigManager _config;

    public GithubUpdater(IHttp http, IAppConfig appConfig, ConfigManager configManager)
    {
        _http = http;
        _appConfig = appConfig;
        _config = configManager;
    }

    public async Task<(bool, GitHubRelease)> Check()
    {
        var latestRelease = await GetlatestRelease();

        var nowAppVersion = AppVersion.Parse(Version);
        var newAppVersion = AppVersion.Parse(latestRelease.TagName!);

        if (newAppVersion > nowAppVersion)
        {
            return (true, latestRelease);
        }
        return (false, latestRelease);
    }

    private async Task<GitHubRelease> GetlatestRelease()
    {
        var allowPrerelease = _config.GetConfig<ProgramConfig>().CheckUpdateForBeta;
        for (int i = 1; ; i++)
        {
            var releaseList = await _http.GetHttpClient().GetFromJsonAsync<List<GitHubRelease>>($"{UpdateApiUrl}?page={i}");
            ArgumentNullException.ThrowIfNull(releaseList);
            foreach (var release in releaseList)
            {
                if (!release.Prerelease || allowPrerelease)
                {
                    return release;
                }
            }
        }

        throw new Exception(I18n.Strings.FailToParseVersion);
    }
}
