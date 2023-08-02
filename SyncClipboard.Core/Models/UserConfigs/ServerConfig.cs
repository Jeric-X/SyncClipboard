namespace SyncClipboard.Core.Models.UserConfigs;

public record class ServerConfig
{
    public bool SwitchOn { get; set; } = false;
    public ushort Port { get; set; } = 5033;
    public string UserName { get; set; } = "admin";
    public string Password { get; set; } = "admin";
}
