# 独立辅助程序的打包集成

本 PR 只新增独立 Avalonia NativeAOT 窗口程序及打包集成。辅助程序当前只显示说明窗口，没有更新框架、任务协议、策略工厂、文件替换、进程退出等待或重启逻辑；主程序也尚未调用它。

## 产物范围

| 包类型 | 携带辅助程序 |
| --- | --- |
| Windows 便携 ZIP（全部运行库变体） | 是 |
| Windows Inno Setup EXE | 否 |
| macOS DMG（直接下载和 Homebrew 共用） | 是 |
| Linux AppImage（两种 .NET 运行库变体） | 是 |
| Linux deb / rpm、Server | 否 |

辅助程序每个 RID 构建一次：win-x64、win-arm64、osx-x64、osx-arm64、linux-x64、linux-arm64。同架构各运行库变体使用相同的 NativeAOT 辅助程序，不需要额外的 .NET 运行时。

## 构建流程

PR 与常规分支/tag CI 均调用 `updater-package.yml`，在目标系统和架构上发布辅助程序。主程序的打包任务依赖该流程完成，通过同一次 workflow run 的 artifact 获取辅助产物。普通本地主程序编译不会自动发布 NativeAOT 辅助程序。

`build/updater/package.py` 发布程序及本地库到 `delivery` 目录，不额外压缩成 ZIP。Windows 使用 GUI 子系统；软件渲染省去 ANGLE，macOS 的本地库按目标架构裁切，所有交付的 Mach-O 文件先做 ad-hoc 签名并验证。

辅助程序使用框架默认的本地库加载机制，不注册自定义加载器。CI 将整个目录复制到含空格和中文的独立目录，从不同工作目录启动 `--smoke-test`，验证默认加载机制下窗口能正常启动。该模式只显示窗口一秒后退出，不会执行更新。报告记录 RID、源提交、各文件 SHA-256、原始体积及检查结果。

`build/updater/include-in-package.py` 在包类型已经确定的独立暂存目录中加入 `Updater` 目录，并校验 RID、源提交、文件清单/哈希和 GUI 检查结果。辅助程序缺失或不匹配会使打包失败。EXE/deb/rpm 分支不下载辅助产物，并检查暂存目录中没有 Updater 目录，防止误混入。

Windows/Linux 的应用文件目录中存放：

```text
Updater/
  SyncClipboard.Updater[.exe]
  本平台的本地库
```

macOS 同样使用普通可执行文件，目录为 `SyncClipboard.app/Contents/Resources/Updater/`。主程序最终的 ZIP、DMG 或 AppImage 统一负责压缩。GitHub artifact 的传输封装不作为内嵌文件放入主程序。

macOS 的顺序是：辅助程序签名 → 放入主程序 Resources → BundleTool 重新 ad-hoc 签名主 .app → 制作 DMG。Homebrew 继续使用相同 DMG，不增加专用产物。复制时显式恢复辅助程序的 Unix 执行权限。

`build/updater/verify-package.py` 在 CI 中检查最终 ZIP、DMG、AppImage 内每个辅助程序文件的哈希，并检查最终 deb/rpm 不含 Updater 目录。DMG 还会挂载只读副本验证 app 签名及辅助程序执行权限。AppImage 使用 SquashFS 工具读取内容，不执行目标镜像，因此 x64 打包主机也能检查 arm64 产物。Windows EXE 的排除检查在交给 Inno Setup 的输入目录上执行。

## 后续接入

将来主程序需要将整个 `Updater` 目录复制到独立任务目录、保留执行权限，再启动辅助程序，避免依赖正在被替换的安装目录。更新协议、状态、安装策略、数据保护及重启行为留待后续 PR；本 PR 不提供安装能力。

当前 macOS 交付使用 ad-hoc 签名，没有 Developer ID 签名或公证。Linux 仍依赖目标系统的本地桌面库；各平台 CI 的启动结果不代表已经覆盖全部系统版本。实际分发体积以最终主程序包为准。
