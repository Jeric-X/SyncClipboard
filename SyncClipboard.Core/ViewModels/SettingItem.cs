namespace SyncClipboard.Core.ViewModels
{
    public class SettingItem
    {
        public string Name { get; set; }
        public string Tag { get; set; }

        public SettingItem(string settingName, string tag)
        {
            Name = settingName;
            Tag = tag;
        }
    }
}