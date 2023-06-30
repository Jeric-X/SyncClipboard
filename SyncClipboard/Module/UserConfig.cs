using System;
using static SyncClipboard.Core.Commons.UserConfig;

namespace SyncClipboard.Module
{
    internal static class UserConfig
    {
        internal static event Action ConfigChanged;
        private static Core.Commons.UserConfig _userConfig;

        internal static void InitializeUserConfig(Core.Commons.UserConfig userConfig)
        {
            _userConfig = userConfig;
            userConfig.ConfigChanged += () => ConfigChanged?.Invoke();
        }

        internal static Configuration Config => _userConfig?.Config ?? new();

        internal static void Save()
        {
            _userConfig.Save();
        }

        internal static void Load()
        {
            _userConfig.Load();
        }

        internal static void AddMenu()
        {
            _userConfig.AddMenuItems();
        }
    }
}