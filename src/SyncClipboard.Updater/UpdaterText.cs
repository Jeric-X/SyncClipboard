using SyncClipboard.Updater.Core;

namespace SyncClipboard.Updater;

internal static class UpdaterText
{
    public static string Phase(UpdatePhase phase, bool chinese) => (phase, chinese) switch
    {
        (UpdatePhase.Preparing, true) => "正在准备更新",
        (UpdatePhase.WaitingForExit, true) => "正在等待程序退出",
        (UpdatePhase.BackingUp, true) => "正在备份",
        (UpdatePhase.Installing, true) => "正在安装更新",
        (UpdatePhase.Restoring, true) => "正在恢复旧版本",
        (UpdatePhase.Starting, true) => "正在启动程序",
        (UpdatePhase.Preparing, false) => "Preparing update",
        (UpdatePhase.WaitingForExit, false) => "Waiting for application exit",
        (UpdatePhase.BackingUp, false) => "Backing up",
        (UpdatePhase.Installing, false) => "Installing update",
        (UpdatePhase.Restoring, false) => "Restoring previous version",
        (UpdatePhase.Starting, false) => "Starting application",
        _ => "SyncClipboard Update"
    };
}
