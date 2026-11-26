namespace SyncClipboard.Shared;

public record class ServerPara(
    ushort Port,
    string Path,
    bool EnableHttps,
    string CertificatePemPath,
    string CertificatePemKeyPath,
    bool EnableCustomConfigurationFile,
    string CustomConfigurationFilePath,
    bool DiagnoseMode
);
