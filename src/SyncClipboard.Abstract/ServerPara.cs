namespace SyncClipboard.Abstract;

public record class ServerPara(
    ushort Port,
    string Path,
    string UserName,
    string Password,
    bool Passive,
    bool EnableHttps,
    string CertificatePemPath,
    string CertificatePemKeyPath,
    bool EnableCustomConfigurationFile,
    string CustomConfigurationFilePath,
    bool DiagnoseMode,
    IServiceProvider Services
);
