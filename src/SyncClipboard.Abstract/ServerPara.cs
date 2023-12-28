namespace SyncClipboard.Abstract;

public record class ServerPara(
    ushort Port,
    string Path,
    string UserName,
    string Password,
    bool Passive,
    bool DiagnoseMode,
    IServiceProvider Services
);
