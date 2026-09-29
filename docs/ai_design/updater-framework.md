# 独立更新辅助程序框架

本阶段只新增框架、独立 Avalonia NativeAOT 程序和 CI 打包。主程序的检查更新、下载、按钮、通知和安装行为均未接入，也未引入实际 EXE/ZIP/DMG/AppImage 安装实现。

## 项目边界

| 项目 / 文件 | 职责 |
| --- | --- |
| `SyncClipboard.Updater.Core` | 无 UI、无主程序依赖的任务协议和策略执行框架 |
| `UpdateRequest` / `UpdateResult` / `UpdateProgress` | 输入任务、最终结果、辅助窗口阶段与进度 |
| `IUpdateStrategy` / `UpdateStrategyFactory` | 显式注册策略，按包类型选取一个策略实例 |
| `UpdateRunner` | 校验输入、执行所选策略、将异常或取消转成结果 |
| `UpdateTaskFile` / `UpdateJsonContext` | 读写任务和结果，使用 NativeAOT 兼容的 JSON 源生成 |
| `SyncClipboard.Updater` | 独立进程，显示进度、成功退出、失败保留窗口 |
| `NativeLibraries` | 从程序自身所在目录加载随包携带的本地库 |
| `SyncClipboard.Updater.Tests` | 协议、工厂、执行结果和任务文件保护测试 |
| `build/updater/package.py` | NativeAOT 发布、ZIP 打包、解压后启动验证、体积报告 |

工厂不参与转发调用：`UpdateRunner` 获取策略实例后直接调用它。当前宿主注册列表为空，真实任务会生成 `Failed` 结果，不会假装安装成功。未来具体策略放入策略目录，解压等专用操作由对应策略负责。

辅助窗口的 `UpdatePhase` 只用于显示当前阶段，不是主程序的更新状态机。Windows EXE 直接启动安装器的方式不需要进入这个文件替换辅助进程，因此目前协议不包含 EXE。

## 任务协议

调用方式：`SyncClipboard.Updater --task <request.json 绝对路径>`。无参数启动只显示使用说明。

任务文件放在独立任务目录中，后续主程序集成负责创建目录及写入请求；不能把任务目录放到将被替换的安装内容中。

```json
{
  "ProtocolVersion": 1,
  "PackageKind": "LinuxAppImage",
  "TargetVersion": "3.4.0",
  "PackagePath": "/home/user/.cache/SyncClipboard/update-123/package.AppImage",
  "InstallationPath": "/home/user/Applications/SyncClipboard.AppImage",
  "ParentProcessId": 12345,
  "Language": "zh-CN"
}
```

本版本验证协议号、包类型、非空版本、绝对路径和进程 ID，不会验证或打开安装包。哈希校验、真实进程退出等待、备份、替换、恢复及重启将在具体安装实现接入时添加。`ParentProcessId` 在此阶段只保留为协议字段。

辅助程序用 `run.lock` 的独占句柄防止同一任务并发执行，结束时在请求目录写 `result.json`。结果先写临时文件再移动，已有结果不覆盖，重试需要新任务目录。失败结果包含原因，失败窗口保持打开。读取或反序列化任务失败也会记录失败结果，此时无法可靠确定目标版本，结果中的 `TargetVersion` 为 `null`，主程序不得将这种结果当作可按版本清理的成功任务。窗口关闭会发出取消信号并结束进程，不保证强制退出后仍能写入结果；实际文件替换策略尚未实现。

当前框架不删除任何任务。后续主程序启动清理时，需等待辅助进程退出，并按目标版本小于等于自身版本的规则处理可清理任务；失败日志保留策略在主程序集成时实现。

## 打包和验证

`updater-package.yml` 对 `auto-update` 的相关 PR 和分支提交运行，也可手动触发或作为 reusable workflow 调用。矩阵包括 Windows、macOS、Linux 的 x64 / arm64，各自在对应系统和架构上发布。

CI 产物 `SyncClipboard.Updater-<rid>.zip` 包含可执行文件和同目录本地库。未来主程序只需将整个 ZIP 解压到独立任务目录；Unix 平台应显式设置可执行权限。辅助程序没有内嵌二次解压或自我重启流程。

为控制体积：NativeAOT、完整裁剪、Size 优化、软件渲染、不引入主题或字体；Windows 不携带未使用的 ANGLE，macOS 将通用本地库裁切为目标架构并重新 ad-hoc 签名。这是 CI 构建产物，不包含 Windows 发布签名、macOS Developer ID 签名或公证；正式分发需后续接入。

打包脚本在 CI 中解压实际 ZIP 到带中文和空格的目录，校验各文件 SHA-256，从不同工作目录运行 `--self-test`（源生成 JSON）和 `--smoke-test`（真实窗口及本地库加载）。这些模式不会执行安装。输出报告记录原始字节数、ZIP 大小、文件清单和 SHA-256；体积实测后评估，不承诺所有平台压缩后都小于 10 MB。

本地可读代码，不需要启动辅助程序；当前任务的构建、测试和窗口检查都由远端 CI 执行。
