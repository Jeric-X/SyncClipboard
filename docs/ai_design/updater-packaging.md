# 独立辅助程序的打包集成

本 PR 只新增独立 NativeAOT 辅助程序及打包集成：Windows/macOS 使用控制台程序，Linux AppImage 使用 Avalonia 窗口程序。当前只输出或显示说明，没有更新框架、任务协议、策略工厂、文件替换、进程退出等待或重启逻辑；主程序也尚未调用它。

## 产物范围

| 包类型 | 携带辅助程序 |
| --- | --- |
| Windows 便携 ZIP（全部运行库变体） | 是 |
| Windows Inno Setup EXE | 否 |
| macOS DMG（直接下载和 Homebrew 共用） | 是 |
| Linux AppImage（两种 .NET 运行库变体） | 是 |
| Linux deb / rpm、Server | 否 |

辅助程序每个 RID 构建一次：win-x64、win-arm64、osx-x64、osx-arm64、linux-x64、linux-arm64。同架构各运行库变体使用相同的 NativeAOT 辅助程序，不需要额外的 .NET 运行时。

同一项目按目标 RID 决定是否引用 Avalonia；未指定 RID 时按构建主机系统选择。只有 Linux 编译窗口代码并引用 Avalonia，Windows/macOS 不携带 Avalonia、SkiaSharp 或 HarfBuzz 本地库。

## 构建流程

PR 与常规分支/tag CI 均调用 `updater-package.yml`，在目标系统和架构上发布辅助程序。主程序的打包任务依赖该流程完成，通过同一次 workflow run 的 artifact 获取辅助产物。普通本地主程序编译不会自动发布 NativeAOT 辅助程序。

CI 直接运行 `dotnet publish` 发布辅助程序，通过复制命令收集可执行文件与本地库。不额外维护打包脚本、文件哈希清单或 JSON 报告。

交付产物保持目录形式，放在 `artifacts/updater/delivery` 中。Windows 使用控制台子系统；macOS 只需对 NativeAOT 可执行文件做 ad-hoc 签名，无需裁切图形本地库。Linux 保留 Avalonia 软件渲染及所需的 `.so` 文件。

CI 将整个目录复制到含空格和中文的独立目录，从不同工作目录启动 `--smoke-test`。Windows/macOS 检查控制台输出 `CONSOLE_SMOKE=PASS`；Linux 通过 xvfb 启动窗口，显示一秒后输出 `GUI_SMOKE=PASS` 并退出。步骤超时由 GitHub Actions 控制，日志保留在 CI 输出中。Linux 使用框架默认的本地库加载机制，不注册自定义加载器。

不带参数启动时，Windows/macOS 当前输出说明后退出，Linux 显示说明窗口并等待用户关闭。macOS 控制台程序本身不会主动打开 Terminal 窗口；后续主程序若需要展示终端，须在启动辅助进程时安排，当前 PR 尚未接入该流程。

主程序打包时，`actions/download-artifact` 按 RID 选择同一次 workflow run 的辅助产物，直接下载到应用的 `Updater` 目录。macOS/Linux 随后用 `chmod` 恢复可执行文件权限。Windows 仅便携 ZIP 分支下载，Linux 仅 AppImage 分支下载；EXE/deb/rpm 不执行这一步。

Windows/Linux 的应用文件目录中存放：

```text
Updater/
  SyncClipboard.Updater[.exe]
  本地库（仅 Linux）
```

macOS 同样使用普通可执行文件，目录为 `SyncClipboard.app/Contents/Resources/Updater/`。主程序最终的 ZIP、DMG 或 AppImage 统一负责压缩。GitHub artifact 的传输封装不作为内嵌文件放入主程序。

macOS 的顺序是：辅助程序签名 → 放入主程序 Resources → BundleTool 重新 ad-hoc 签名主 .app → 制作 DMG。Homebrew 继续使用相同 DMG，不增加专用产物。复制时显式恢复辅助程序的 Unix 执行权限。

Windows 便携 ZIP 使用 `7z a -tzip -mm=Deflate -mx=9`，以最高 Deflate 压缩级别打包所有架构和运行库变体，保持标准 ZIP 格式。其他格式沿用原有打包流程，不解包检查最终安装包。

## 后续接入

将来主程序需要将整个 `Updater` 目录复制到独立任务目录、保留执行权限，再启动辅助程序，避免依赖正在被替换的安装目录。更新协议、状态、安装策略、数据保护及重启行为留待后续 PR；本 PR 不提供安装能力。

当前 macOS 交付使用 ad-hoc 签名，没有 Developer ID 签名或公证。Linux 仍依赖目标系统的本地桌面库；各平台 CI 的启动结果不代表已经覆盖全部系统版本。实际分发体积以最终主程序包为准。
