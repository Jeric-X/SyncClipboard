namespace SyncClipboard.Core.UserServices;

public record class ServerConfig
{
    public bool SwitchOn { get; set; } = false;
    public short Port { get; set; } = 5033;
    public string UserName { get; set; } = "admin";
    public string Password { get; set; } = "admin";
}
