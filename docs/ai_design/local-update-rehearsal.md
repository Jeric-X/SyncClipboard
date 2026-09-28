# macOS 本地升级演练

`build/macos/TestUpdate.py` 从当前源码制作一套独立的 `0.0.1 → 0.0.2` 升级环境。
“虚假”的只有版本和发布来源；下载、SHA-256 校验、DMG 校验、备份、替换、重启和清理均使用真实实现。
无需发布 GitHub Release，也不会添加正式客户端的更新源覆盖入口。

## 准备与运行

在仓库根目录执行（需要 Python 3.11+、项目要求的 .NET SDK 和 macOS workload）：

```sh
python3 build/macos/TestUpdate.py prepare
```

输出默认放在 `build/output/update-test-日期时间/`，已经被 Git 忽略。
命令会复制源码，在副本中调整更新地址、数据目录名称和启动检查默认值，然后分别构建两个 Debug 版本。
两个版本都使用临时签名；新版封装成 DMG，生成包含真实 SHA-256 的 GitHub Release 格式 JSON。
原仓库的 C# 文件和当前构建产物保持原样。

双击输出目录中的 `开始升级演练.command`：

1. Terminal 启动只监听 `127.0.0.1` 的更新服务，默认端口 `18765`。
2. 启动 `installed/SyncClipboard.app`，在“关于”确认版本为 `0.0.1`。
3. 点击“检查更新”→“下载更新”→“安装更新”。默认以 8 MiB/s 传输，方便观察下载进度。
4. 下载完成提示为“新版本已下载”。点击安装后，macOS 安装辅助进程打开另一个 Terminal 窗口，主程序退出，执行备份和替换。
5. 安装成功后仅安装窗口自动关闭；本地 HTTP 服务窗口继续运行。失败时安装窗口保留错误与日志路径。
6. 同一安装路径重新启动，确认“关于”版本为 `0.0.2`，再次检查更新应显示已经是最新版。

可先复制一段文本，升级后确认历史保留。首次启动可能需要重新授予系统权限。
测试版有独立的数据目录和进程互斥名称，但 macOS bundle ID 保持原值以经过真实身份校验。
建议手动退出日常使用的实例，避免两个实例同时监听剪贴板；不要设置测试版开机启动。

只启动更新源、不启动 App：

```sh
python3 build/macos/TestUpdate.py serve build/output/update-test-日期时间
```

可加 `--mbps 0` 取消限速。准备时可用 `--port` 选择其他端口；端口写入测试程序，运行时不能随意更换。
HTTP 服务按 Ctrl+C 停止，服务停止不会关闭已经启动的测试 App。

## 断点和现场

测试源码在输出目录的 `source/` 内。附加调试器到测试进程，或直接由调试器启动：

```text
installed/SyncClipboard.app/Contents/MacOS/SyncClipboard.Desktop.MacOS
```

关键断点：`UpdateChecker.CheckNewVersion`、`DownloadUpdatePackage`、`InstallUpdateCore`、
`MacDmgInstaller.PrepareAsync` 和 `UpdateTaskCoordinator.StartAsync`。
进程退出后的安装在 shell 中执行，查看辅助窗口及任务目录中的 `install.log`、`progress`、`task.json`。
主程序退出使原调试会话结束是预期行为；查看新进程需要重新附加。

任务目录使用正式逻辑，由测试程序路径的哈希隔离。
成功任务会在新版启动、辅助进程结束后自动清理；需要保留的日志应在过程中复制。
失败任务保留原有失败恢复信息。再次执行 `prepare` 会创建新的环境，不覆盖旧现场。

该演练验证本机可写安装目录、当前 CPU 架构和临时签名包的更新过程。
系统目录提权、Developer ID 签名及公证、另一种 CPU 架构仍需单独验证。
