namespace SyncClipboard.Updater;

// Keep translations in the executable; no culture data or satellite assemblies are needed.
internal sealed class UpdaterText
{
    private static readonly UpdaterText English = new(false);
    private static readonly UpdaterText Chinese = new(true);
    private static readonly AsyncLocal<UpdaterText?> CurrentLanguage = new();
    private readonly bool isChinese;

    private UpdaterText(bool isChinese)
    {
        this.isChinese = isChinese;
    }

    public static UpdaterText Current
    {
        get => CurrentLanguage.Value ?? English;
        set => CurrentLanguage.Value = value;
    }

    public static UpdaterText ForLanguage(string language)
        => language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? Chinese : English;

    public static UpdaterText FromArguments(string[] args)
    {
        var index = Array.IndexOf(args, "--language");
        return index >= 0 && index + 1 < args.Length ? ForLanguage(args[index + 1]) : English;
    }

    public string Help => $"""
        {Title}
        SyncClipboard.Updater [--smoke-test]
        --package-path <zip|dmg|AppImage>
        --digest sha256:<hash>
        --target <installation path>
        --process-id <pid>
        --work-dir <workspace>
        [--language <language>]
        [--app-elevated <true|false>]
        [--protect-path <path> ...]
        """;

    public string Title => isChinese
        ? "SyncClipboard 更新助手"
        : "SyncClipboard Updater";

    public string StartFromApplication => isChinese
        ? "请从 SyncClipboard 启动更新。"
        : "Start updates from SyncClipboard.";

    public string UnsupportedPlatform => isChinese
        ? "此平台尚不支持自动安装更新。"
        : "Automatic installation is not supported on this platform yet.";

    public string DetachImage => isChinese ? "卸载磁盘映像: " : "Detach disk image: ";
    public string CommandFailed => isChinese ? "命令执行失败: " : "Command failed: ";
    public string InvalidAppImage => isChinese ? "无效或架构不匹配的 AppImage。" : "Invalid AppImage or incompatible architecture.";
    public string MissingAppImage => isChinese ? "待更新的 AppImage 文件不存在。" : "The installed AppImage is missing.";
    public string InvalidAppBundle => isChinese ? "无效的 SyncClipboard 应用包。" : "Invalid SyncClipboard app bundle.";
    public string DataInsideBundle => isChinese
        ? "请先将用户数据移出应用包，再安装更新: "
        : "Move user data outside the app bundle before installing the update: ";

    public string Preparing => isChinese
        ? "复制并校验更新包"
        : "Copy and verify the update package";

    public string Waiting => isChinese
        ? "等待程序退出"
        : "Wait for the application to exit";

    public string BackingUp => isChinese
        ? "备份程序文件"
        : "Back up application files";

    public string Installing => isChinese
        ? "安装更新"
        : "Install the update";

    public string Restoring => isChinese
        ? "恢复旧版本"
        : "Restore the previous version";

    public string ConfirmForceExit => isChinese
        ? "主程序仍未退出。是否强制退出并继续更新？"
        : "SyncClipboard has not exited. Force it to exit and continue updating?";

    public string Yes => isChinese
        ? "是"
        : "Yes";

    public string No => isChinese
        ? "否"
        : "No";

    public string Cancel => isChinese ? "取消" : "Cancel";

    public string ConfirmClose => isChinese
        ? "更新正在进行，强行终止可能导致安装异常"
        : "An update is in progress. Forcing it to stop may leave the installation incomplete.";

    public string Retry => isChinese
        ? "重试"
        : "Retry";

    public string Abort => isChinese
        ? "终止"
        : "Abort";

    public string Rollback => isChinese
        ? "回滚，恢复旧版本"
        : "Roll back to the previous version";

    public string AbortKeepBackup => isChinese
        ? "终止，不回滚，保留备份"
        : "Abort without rollback; keep backups";

    public string CleanupIncomplete => isChinese
        ? "更新已完成，但临时文件或旧备份未清理完毕。"
        : "The update completed, but temporary files or old backups could not be fully removed.";

    public string DesktopUserUnavailable => isChinese
        ? "无法获取普通权限的桌面用户，不能以原权限启动主程序。请手动启动 SyncClipboard。"
        : "An unelevated desktop user is unavailable. Start SyncClipboard manually to keep its original permissions.";

    public string Failed => isChinese
        ? "更新失败。"
        : "Update failed.";

    public string BackupDirectory => isChinese
        ? "备份目录: "
        : "Backup directory: ";

    public string Workspace => isChinese
        ? "日志及工作目录: "
        : "Log and workspace: ";

    public string Close => isChinese
        ? "关闭"
        : "Close";

    public string Aborted => isChinese
        ? "更新已终止，未执行回滚。"
        : "Update terminated without rollback.";

    public string RollbackFailed => isChinese
        ? "更新回滚失败。"
        : "Update rollback failed.";

    public string PrepareBackup => isChinese
        ? "准备备份: "
        : "Prepare backup: ";

    public string BackUp => isChinese
        ? "备份: "
        : "Back up: ";

    public string CheckFreeSpace => isChinese
        ? "检查空间: "
        : "Check free space: ";

    public string Replace => isChinese
        ? "替换: "
        : "Replace: ";

    public string Restore => isChinese
        ? "恢复: "
        : "Restore: ";

    public string RemoveDirectory => isChinese
        ? "删除目录: "
        : "Remove directory: ";

    public string RolledBack => isChinese
        ? "更新已回滚。"
        : "Update rolled back.";

    public string RemoveTemporaryFile => isChinese
        ? "删除临时文件: "
        : "Remove temporary file: ";

    public string LockInstallation => isChinese
        ? "锁定安装目录: "
        : "Lock installation: ";

    public string CheckWriteAccess => isChinese
        ? "检查目录写入权限: "
        : "Check directory write access: ";

    public string PreparePackage => isChinese
        ? "准备更新包: "
        : "Prepare package: ";

    public string Canceled => isChinese
        ? "已取消更新。"
        : "Update canceled.";

    public string ForceExitFailed => isChinese
        ? "无法强制退出主程序。"
        : "Could not force SyncClipboard to exit.";

    public string StartCleanup => isChinese
        ? "启动清理: "
        : "Start cleanup: ";

    public string RemoveWorkspace => isChinese
        ? "删除工作目录: "
        : "Remove workspace: ";

    public string UnsafeDestination => isChinese
        ? "不安全的更新目标: "
        : "Unsafe update destination: ";

    public string RollbackRequested => isChinese
        ? "已请求回滚。"
        : "Rollback requested.";

    public string UnknownArgument => isChinese
        ? "未知或不完整的更新器参数: "
        : "Unknown or incomplete updater argument: ";

    public string DuplicateArgument => isChinese
        ? "重复的更新器参数: "
        : "Duplicate updater argument: ";

    public string MissingArgument => isChinese
        ? "缺少更新器参数: "
        : "Missing updater argument: ";

    public string InvalidDigest => isChinese
        ? "需要有效的 SHA256 摘要。"
        : "Expected a SHA256 digest.";

    public string InvalidProcessId => isChinese
        ? "主程序进程 ID 无效。"
        : "Invalid parent process ID.";

    public string InvalidAppElevated => isChinese
        ? "--app-elevated 必须为 true 或 false。"
        : "--app-elevated must be true or false.";

    public string MissingTarget => isChinese
        ? "安装目录不存在。"
        : "The installation directory is missing.";

    public string ProtectedTarget => isChinese
        ? "安装目录位于受保护的数据目录内。"
        : "The installation directory is inside a protected data directory.";

    public string WorkspaceInsideTarget => isChinese
        ? "更新器工作目录必须位于安装目录之外。"
        : "The updater workspace must be outside the installation directory.";

    public string HashMismatch => isChinese
        ? "更新包 SHA256 与下载版本的摘要不一致。"
        : "The update package SHA256 does not match the downloaded release.";

    public string DuplicateArchiveEntry => isChinese
        ? "压缩包条目重复: "
        : "Duplicate archive entry: ";

    public string InvalidArchivePath => isChinese
        ? "压缩包路径无效。"
        : "Invalid archive path.";

    public string UnsafeArchiveEntry => isChinese
        ? "不安全的压缩包条目: "
        : "Unsafe archive entry: ";

    public string UnsafeWindowsArchiveEntry => isChinese
        ? "不安全的 Windows 压缩包条目: "
        : "Unsafe Windows archive entry: ";

    public string PackageMismatch => isChinese
        ? "更新包与当前安装不匹配。"
        : "The update package does not match this installation.";

    public string IncompatibleArchitecture => isChinese
        ? "更新程序的架构不兼容: "
        : "The update executable has an incompatible architecture: ";

    public string InsufficientSpace => isChinese
        ? "可用空间不足，无法更新。"
        : "Not enough free space for the update.";

    public string WorkspaceNotPrepared => isChinese
        ? "主程序必须先准备更新器工作目录。"
        : "The main application must prepare the updater workspace.";

    public string OutsideWorkspace => isChinese
        ? "更新器必须从自己的工作目录运行。"
        : "The update worker must run from its own workspace.";

    public string ElevatedStartFailed => isChinese
        ? "无法启动管理员权限更新器。"
        : "Could not start the elevated updater.";

    public string InstallationLockedByOtherUser => isChinese
        ? "其他用户已锁定此安装目录进行更新。"
        : "Another user has locked this installation for update.";

    public string InstallationLocked => isChinese
        ? "另一个更新器正在使用此安装目录。"
        : "Another updater is using this installation.";

    public string RestartFailed => isChinese
        ? "无法重新启动 SyncClipboard。"
        : "Could not restart SyncClipboard.";

    public string CleanupStartFailed => isChinese
        ? "无法启动更新器清理程序。"
        : "Could not start updater cleanup.";

    public string InvalidWorkspace => isChinese
        ? "更新器工作目录无效。"
        : "Invalid updater workspace.";

    public string RequestingElevation => isChinese
        ? "正在请求安装目录的管理员权限。"
        : "Requesting administrator permission for the installation directory.";

    public string CleaningUp => isChinese
        ? "更新完成，正在删除临时文件和旧备份。"
        : "Update completed. Removing staging files and old backups.";

    public string RestartError => isChinese
        ? "无法重新启动主程序: "
        : "Could not restart the application: ";
}
