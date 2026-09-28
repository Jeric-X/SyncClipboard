# 点击安装更新

官方 GitHub 发布包下载并通过 SHA-256 校验后，EXE 安装版、ZIP 便携版、DMG 和 AppImage 显示“安装更新”。自动下载不触发安装；deb/rpm 和包管理器渠道保留原流程。

## 代码结构

`UpdateInstallerFactory` 根据渠道、包格式及平台创建安装实现。DI 将选定的 `IUpdateInstaller` 注册为单例，`UpdateChecker` 在整个生命周期内直接使用这个实例；准备和启动阶段不会再按 `Kind` 分派。

```text
UpdateChecker → IUpdateInstaller（由 Factory 创建一次）
                 ├── WindowsExeInstaller：直接校验并运行 EXE 安装器
                 ├── FileReplacementUpdater：共用文件替换更新流程
                 │    └── IFileReplacementStrategy
                 │         ├── WindowsZipReplacementStrategy
                 │         ├── MacDmgReplacementStrategy
                 │         └── LinuxAppImageReplacementStrategy
                 └── UnsupportedUpdateInstaller：保留手动更新入口
```

`IUpdateInstaller` 提供能力判断、准备和启动方法，并说明交接成功后是否需要主程序退出。`WindowsExeInstaller` 由安装器自行关闭应用；`FileReplacementUpdater` 准备下载快照和任务清单，委托已选定的文件替换策略准备 payload，启动辅助进程并完成 `ready` / `commit` / `cancel` 交接。其自身不修改更新状态，也不退出主程序。

格式策略集中在 `Strategies/`，实现 `IFileReplacementStrategy`：ZIP 负责安全解压、数据保护和 PE 校验；DMG 负责挂载、身份/架构/签名校验和 bundle 暂存；AppImage 负责原文件定位、ELF 校验和辅助副本。策略提供安装目录、辅助进程启动参数，以及必要的清理约束。较重的 AppImage 辅助执行逻辑独立为 `AppImageInstallWorker`；内嵌脚本位于 `Strategies/Scripts/`。

`UpdateChecker` 在调用安装实现之前进入 `Installing`。准备或交接失败时切换到 `Failed`，取消时回到下载完成状态；交接成功后按安装实现的退出约定调用 `AppCore.ExitAsync()`。`AppCore` 启动时独立调用 `UpdateTaskCleaner` 清理成功任务，清理不属于安装接口。

`UpdateInstallTask` 保存任务清单；`UpdateFileSystem` 提供路径、空间及内嵌脚本释放工具；`UpdatePackageVerifier` 校验哈希和包元数据；`UpdateServiceRegistration` 装配 Factory 与安装实例。任务 JSON 字段和辅助进程协议保持不变。

## 主程序更新状态

下载完成后根据安装能力进入两个明确的状态：`Downloaded` 固定提供“打开文件夹”，`ReadyToInstall` 固定提供“安装更新”。安装期间统一使用 `Installing`，禁止新的检查或重复安装；安装失败复用现有 `Failed` 和通用错误展示，不增加独立的失败按钮分支。授权取消或 EXE 安装器关闭而主程序仍在运行时，重新判断安装能力并回到相应的下载完成状态。

## 安装交接与文件位置

Windows EXE 校验后直接启动原安装包，不传递静默安装参数，不释放或执行辅助脚本。关闭应用、安装位置、权限和完成后启动应用均由原有安装向导处理。应用不主动退出；安装器运行期间禁用重复更新操作，安装器关闭后如果应用仍在运行则恢复“安装更新”按钮。`setup.iss` 保持原样。

ZIP、DMG、AppImage 在安装期间阻止其他检查、下载和安装请求。准备工作在后台线程运行。下载快照、新版暂存、旧版备份、AppImage 辅助副本和日志都位于用户 LocalApplicationData 下的 `SyncClipboardUpdater/<安装路径哈希>/<任务 ID>`，安装位置旁边不生成暂存或备份。

任务通过 JSON 和独立 `.txt` 参数文件传递，不执行动态生成的命令文本。交接顺序为：辅助程序就绪 → `ready` → 主程序写 `commit` 并正常退出 → 等待旧进程退出（最多 60 秒）→ 备份 → 复制到原位置 → `installed` / `completed` → 启动应用。准备或授权最长等待 5 分钟；取消写入 `cancel`。旧进程不退出时停止，不强杀。

备份成功后才覆盖程序。替换失败时恢复旧文件并尝试启动旧版；恢复失败则保留备份和日志。窗口中显示失败原因及日志位置。不承诺强制终止或系统断电时能够自动回滚。

## 界面和各平台行为

- Windows ZIP：打开 PowerShell 窗口显示按文件数量计算的备份、安装和恢复进度。需要提升权限时使用 UAC，原用户的监督进程负责重新启动应用。只覆盖发布包包含的文件，保留配置、历史、自定义数据和其他文件；先备份，再通过逐文件记录恢复。拒绝通过链接写出安装目录。
- macOS DMG：生成专用的 `install.terminal` 会话配置，通过 Terminal 执行内嵌的 `OpenMacUpdate.command`。会话设置 `shellExitAction=1`，使用 `exec` 保留安装脚本的退出码：成功后自动关闭本次安装窗口，失败时保留窗口和错误信息，不修改 Terminal 的默认配置。监督脚本显示当前阶段及依据已复制磁盘占用估算的百分比；需要时通过系统授权运行替换进程，完成后由原用户启动应用。使用 `ditto` 复制 bundle 并验证签名，不在目标旁边重命名暂存。只读镜像运行、App Translocation、bundle 内存放用户数据或有链接的安装位置退回手动安装。
- Linux AppImage：优先使用 `APPIMAGE` 定位原文件，兼容 `ARGV0` 与 `OWD`。把当前完整 AppImage 复制到任务目录，传入 `--install-update <任务文件>` 启动。`Desktop.Default.Program` 在单实例检查之前进入 `UpdateHelperApplication`，只加载独立的 Avalonia 更新窗口，不构建 AppCore、启动同步服务或加载历史数据库。`AppImageInstallWorker` 校验新文件、等待主程序退出、备份并复制到原路径，报告真实字节进度，保留执行权限，然后启动新版。启动副本和新版时清除旧 AppImage 的挂载与加载器环境变量。普通关闭窗口会请求取消并尝试恢复；强制结束进程时只保留已写出的记录。目标不可写时退回手动安装。

AppImage 复用现有程序集和图形依赖，没有单独的 NativeAOT 或 GUI 发布包。临时磁盘需要同时容纳下载包、新版暂存、当前 AppImage 的辅助副本及旧版备份。

## 由主程序清理成功任务

主程序正常启动后后台清理，必须同时满足：

1. 任务有 `completed`，没有 `failed`、`canceled` 或 `restored`。
2. 任务的**目标版本小于等于当前运行版本**。
3. 记录的辅助进程及替换进程都已退出。

辅助进程仍运行时后台等待，不阻塞应用启动；等待最多 5 分钟，之后留到下次正常启动。删除前再次检查结果。成功后删除整个任务目录，包括辅助副本、暂存文件、备份和日志。失败、不完整、版本高于自身、进程信息缺失或无法解析的任务全部保留，不改写失败日志。

例如目标为 3.4 的任务，3.3 无权清理，3.4 或更高版本可以清理成功任务。辅助进程无需等待新版发出启动确认；程序替换成功但新版本身无法启动时，任务及备份会留到后续满足条件的正常启动。

## 验证

自动测试覆盖格式／渠道识别、状态互斥、哈希、ZIP 路径与数据保护，AppImage 更新进度、取消与失败恢复，真实 Unix 脚本的备份复制、签名保留和退出超时，以及目标版本边界、失败保留、等待辅助进程退出等清理条件。独立更新窗口有 Avalonia Headless 测试。Windows PowerShell 文件替换测试仅在 Windows 执行。

发布前仍需在真实系统用独立安装副本验证：Windows 用户级／系统级 EXE 和 ZIP；macOS 两种架构的 Terminal、授权与签名；Linux 两种架构 AppImage 的挂载、更新窗口、重新启动和清理。测试不得覆盖开发者当前使用的应用。
