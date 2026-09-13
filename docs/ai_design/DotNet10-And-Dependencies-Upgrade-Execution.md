# .NET 10 与主要依赖升级执行记录

执行计划：[分步升级计划](DotNet10-And-Dependencies-Upgrade-Plan.md)。本记录不替代计划中的任何验证门槛。

## 当前状态

- 分支：`codex/upgrade-dotnet10-dependencies`，基线 `cc7289d1bc7528ef1a8a63af4ccc7f8f55a6fd6a`。
- 步骤 1 已通过，当前执行步骤 2：将 WinUI3 入口及测试改为 .NET 10；中央依赖版本保持基线。
- PR：[#419](https://github.com/Jeric-X/SyncClipboard/pull/419)。步骤 1 验证通过提交为 `f144fa7fc3bef5ee9f55ca636d561fc0cbcca607`；步骤 2 尚待当前改动的 PR 验证，不允许进入步骤 3。
- 步骤 3–18（包括每个带字母的子步骤）：全部待执行；不得把本阶段 CI 通过解释为整体升级完成。
- 不执行 UI 验证，不要求解锁后补测，不将 UI 排除项计作通过。

验证范围统一遵循计划第 3.2 节：保留 UI 项目的编译、静态检查、包内容检查及经审查的非 UI 测试；需要创建窗口/控件、初始化 UI 框架或使用 UI 调度线程的检查均排除，包括隐藏窗口和无头 UI 测试。每步记录具体排除项及原因；仅这些 UI 项未验证不阻塞下一步，范围内检查仍须独立通过，CI 相关改动仍须提交 PR 并监控问题。阶段或最终结果仅表示非 UI 验证通过。

## 步骤 0：基线证据

2026-09-13 核对 master 的 [build run 34751690107](https://github.com/Jeric-X/SyncClipboard/actions/runs/34751690107)：head 与上述基线相同，结论 success。Windows x64/arm64 的运行时和 App SDK 组合、Linux x64/arm64 的现有包格式组合、macOS 两架构、Server、Core 测试和格式检查均通过。非标签提交对应的发布任务跳过，不能计作构建证据。

本机 macOS arm64 使用 SDK 10.0.302 和隔离安装的 .NET/ASP.NET Core 8.0.31 运行基线 Core：346 通过、0 失败、0 跳过。报告位于本机 `/tmp/syncclipboard-upgrade-results/baseline-core.trx`；与上述 CI Core 日志的 346 项结果一致。

原本机仅安装 .NET 10 运行时，net8 测试宿主无法启动；补装到临时工具链后通过。没有通过运行时主版本回滚绕过要求。旧 Magick.NET 14.9.1 的 NuGet 漏洞警告已记录，保持可见，按步骤 11 处理。

基线 CI 没有运行 Desktop/WinUI3 测试，也没有 API 冒烟检查；步骤 1 补齐。既有 Core 自动化和原平台构建结果不能证明全部协议集成、所有原生 API 或 UI 行为已经验证，后续各依赖步骤仍须执行计划列出的专门回归。

## 步骤 1：工具链和验证覆盖

### 改动

- `global.json` 指定 SDK 10.0.302，仅允许同 feature band 的补丁滚动；工作流显式安装该 SDK 并打印 `dotnet --info`。
- 保留尚未升级的 net8/net9 测试及工具所需旧 SDK/运行时。
- macOS 工作负载集固定 10.0.302.1，本机对应 macOS manifest 26.5.10315；打包和格式检查均选择 Xcode 26.6。
- 新增三个 OS 的 Desktop 非 UI 测试、Windows WinUI3 非 UI 测试和 Desktop.Default Windows TFM 编译。保留原平台构建和打包矩阵。
- 保存 TRX，并检查通过数量和执行数量，拒绝空筛选、失败或跳过；Core 下限 346，两个平台服务测试下限各 4。
- Server 发布后执行 HTTP 冒烟；独立 Linux amd64/arm64 原生 runner 构建和运行容器，使用临时账号、数据目录及本机临时端口，不推送镜像。
- 原 push 工作流会发布分支镜像；`codex/` 开发分支现跳过此发布任务。其容器两架构验证由 `server-build` 承担，其他分支和标签的发布行为保持原规则。
- `build/appveyor/appveyor.yml` 使用旧项目路径和 VS 2022，当前 GitHub 工作流没有引用它，基线检查也未出现 AppVeyor。记录为遗留配置，不作为本次升级验证依据；未声称已核实仓库外 AppVeyor 账户设置。

### 非 UI 筛选及排除清单

两个平台测试原来的 `ServiceProvider.ConfigedServices` 各由数据源提供 13 行，混合真实平台服务构造。保留原方法及数据源，标为 `RequiresUI`，本计划排除这些混合运行。以下四个已审查的服务注册另以 `NonUI` 数据行实际解析：`IAppConfig`、`IGlobalDialog`、`IClipboardSetter<TextProfile>`、`IClipboardSetter<FileProfile>`。只构造对象，不调用显示对话框或设置剪贴板的方法；测试结束释放容器。

原 13 行包括 `IServiceProvider`、`ConfigManager`、`IAppConfig`、`ILogger`、`IWebDav`、`IHttp`、`INotificationManager`、`IClipboardFactory`、`IClipboardChangingListener`、文本/文件/图片 setter、`IGlobalDialog`。其中配置、日志等服务的依赖链可能初始化平台通知服务；平台剪贴板服务可能初始化桌面依赖。四个安全注册在白名单中重新覆盖，其他混合构造不执行。每个项目现在发现总数预计 17，运行白名单 4，排除原混合行 13；最终以 CI TRX 和发现结果核定，排除项不记为测试通过或运行时 skip。

### 本地验证

| 检查 | 环境与结果 | 证据 |
| --- | --- | --- |
| Core 非 UI | macOS arm64，Release/net8；346 通过、0 失败、0 跳过 | `/tmp/syncclipboard-stage1-results/stage1-core.trx` |
| Desktop 非 UI | macOS arm64，Release/net8；4 通过、0 失败、0 跳过 | `/tmp/syncclipboard-upgrade-results/stage1-desktop.trx` |
| Server publish | SDK 10.0.302，Release/net8，退出码 0 | `/tmp/syncclipboard-stage1-server-build.log` |
| Server HTTP 冒烟 | 认证拒绝/通过、Swagger JSON、SignalR negotiate、文本/文件上传下载、错误哈希拒绝、历史去重、两轮启动后数据保留均通过 | `/tmp/syncclipboard-stage1-server-smoke.log`；仅握手，不宣称验证 SignalR 重连/推送 |
| macOS publish | SDK 10.0.302，工作负载集 10.0.302.1，Release/osx-arm64，退出码 0 | `/tmp/syncclipboard-stage1-mac-publish.log` |
| macOS 包结构 | `.app` 主程序为 Mach-O arm64，Bundle ID 正确，共享 Desktop 程序集存在，含 19 个 dylib；未启动产物 | 本机发布目录 `src/SyncClipboard.Desktop.MacOS/bin/Release/net10.0-macos/osx-arm64/` |
| 仓库格式检查 | `cd src` 后 `dotnet format --verify-no-changes --severity info --no-restore`，退出码 0；有跨平台工作区加载警告，全量结论待 CI | `/tmp/syncclipboard-stage1-format.log` |
| TRX 门槛 | 真实报告可读；构造的空报告、跳过和失败结果均被拒绝 | `build/verification/check_test_results.py` 本地验证 |
| 工作流语法 | Ruby YAML 解析全部工作流成功；`git diff --check` 通过 | 提交前检查 |

本机未安装 Docker，Windows/Linux 执行及 macOS x64/安装包检查交由本阶段真实 PR CI 和产物检查完成。上述记录不将缺失的平台检查计作通过。

工具链依据：[工作负载集固定方式](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-workload-sets)、[Xcode 26.6 支持说明](https://github.com/dotnet/macios/issues/25658)。

### PR 首轮问题与修复

首轮 [PR run 34753931454](https://github.com/Jeric-X/SyncClipboard/actions/runs/34753931454) 与 [push run 34753921296](https://github.com/Jeric-X/SyncClipboard/actions/runs/34753921296) 暴露容器就绪探测未重试连接重置；修复仅作用于启动探测，正式 API 失败仍立即报告。自动评审另指出默认 root 容器会使临时目录无法由 runner 清理，测试容器改用宿主 UID/GID。

CodeFactor 三项问题分别修复为：HTTPConnection 只允许本机 HTTP 且不自动重定向；TRX 使用固定 defusedxml 0.7.1 并禁止 DTD/实体/外部引用；服务器生命周期从 main 拆分。未关闭或忽略检查。CI 使用 Python 3.12 安装 `build/verification/requirements.txt`。

修复后本机服务器两轮冒烟通过；连接重置仅在就绪期间重试、外部地址拒绝、恶意 XML 拒绝，以及真实 346 项 TRX 和空/跳过/失败报告门槛均通过。容器 UID/GID 及最终清理由两架构 CI 实际验证，结果待新 head 检查。

监控任务 `pr419` 已启用，每 10 分钟检查一次；未完成全部计划前不将阶段结果当作最终交付。

`8274eb57` 的 CodeFactor 和 PR/push 两套 amd64/arm64 容器任务均成功，验证了启动重试与非 root 数据清理。新增评审继续补强 TRX 总体结果及各项计数的一致性、明确条件判断在 Python 优化模式下仍生效，并将 Linux 发布示例改为当前 net8。相关失败分支和 `python -O` 完整服务器冒烟已在本机通过。

此 head 的 PR 格式任务在命令启动后以 137 退出，未产生格式错误诊断；根因尚未确认，保持原检查，在新 head 上复核。全部阶段门槛仍未通过。

### 步骤 1 最终通过证据

验证通过提交：`f144fa7fc3bef5ee9f55ca636d561fc0cbcca607`。2026-09-13 再次读取 PR 当前 head、checks、reviews、行内线程和普通评论：103 项成功，8 项预期发布任务跳过，无运行中或失败检查；4 个评审线程均已解决，最新 Codex 评审完成且没有新增问题。旧格式任务退出 137 未复现，同一格式命令在本 head 的 PR/push 检查均通过，没有关闭或削弱检查。

- [PR build 34754612208](https://github.com/Jeric-X/SyncClipboard/actions/runs/34754612208)、[push build 34754608964](https://github.com/Jeric-X/SyncClipboard/actions/runs/34754608964)、[CodeQL 34754611822](https://github.com/Jeric-X/SyncClipboard/actions/runs/34754611822) 均完成；CodeFactor 成功。跳过项为服务器镜像发布、三个平台 release、两个 GitHub release、Homebrew 和 Winget 发布，必要构建并未跳过。
- 下载全部 47 个 artifact，五份 TRX 由仓库校验器解析：Core 346、三平台 Desktop 各 4、WinUI3 4，共 362 通过，0 失败/跳过。UI 排除项未计入此数。
- Windows/Linux 共 38 个原始构建目录和安装/便携包组合通过非 UI 检查：8 个 Windows 目录、4 个 Linux 目录、8 个 ZIP、8 个 Inno EXE、4 个 AppImage、4 个 deb、2 个 rpm。核对主程序集、资源、runtimeconfig/deps、自包含模式与选定原生库 CPU 架构；ZIP CRC 与 Inno 数据完整性检查通过。报告位于本机 `/tmp/syncclipboard-pr419-f144-package-audit.json`，脚本退出码 0。
- 两个 macOS dmg 只读挂载后核对主程序和每包 19 个 dylib 的架构、资源及严格签名，均通过并卸载。Bundle 版本为 3.2.0；安装包版本来源 `feature.txt` 为 3.2.1，保留基线的两种版本用途。未启动任何桌面程序或图形安装向导。
- 下载的 Server DLL 在本机隔离 .NET 8.0.31 下两轮 HTTP/持久化冒烟通过；Linux amd64/arm64 容器由当前 CI 原生 runner 实际构建、启动及验证清理。

阶段结论：步骤 1 的工具链、非 UI 测试及构建/打包门槛已通过，允许进入步骤 2。此结论不替代后续依赖步骤的专门协议、数据库、原生 API 回归，也不表示 UI 已验收。

## 步骤 2：WinUI3 入口与测试升级 .NET 10

前置步骤通过提交：`f144fa7fc3bef5ee9f55ca636d561fc0cbcca607`。状态：进行中。

将 `SyncClipboard.WinUI3` 和 `SyncClipboard.Test.WinUI3` 的 TFM 从 `net9.0-windows10.0.19041.0` 改为 `net10.0-windows10.0.19041.0`；Windows SDK 目标、最低系统声明及全部中央 NuGet 版本保持不变。同步活动 Windows CI 复制路径、本地安装包脚本、两份 VS Code 调试配置与 AGENTS/CLAUDE。Core/Shared/Test 仍为 net8.0；保留旧运行时安装，后续按实际工具需求处理。

仓库搜索剩余 net9 路径仅为计划中的历史基线和已记录的 AppVeyor 遗留配置；后者并非当前验证依据。Windows 构建与非 UI 测试须由当前步骤的真实 PR CI 验证，本机 macOS 无法替代 Windows MSBuild/XAML 编译。现有 x64/arm64、.NET 运行时和 Windows App SDK 携带组合均须通过，下载新产物验证 net10 运行框架及 R5 后才允许进入步骤 3。

本机 SDK 10.0.302 已成功还原两个 WinUI3 项目，资产包含 net10 的 win-x86/x64/arm64 目标；WindowsAppSDK 仍解析为 1.8.260529003。MSBuild 属性查询确认两个 TFM 和原最低系统声明；工作流 YAML 解析通过，AGENTS/CLAUDE 除各自标题和工具介绍外指令一致。旧 Magick.NET 14.9.1 与 SQLitePCLRaw.lib.e_sqlite3 2.1.10 的漏洞警告保持可见，分别在步骤 11 和步骤 7 处理，最终交付前必须核验处理结果。

仓库格式检查发现 WinUI3 默认语言版本变化后两处 IDE0031；仅将 HistoryWindow 中两处空值检查赋值改为条件赋值。沙箱首次因命名管道权限失败，沙箱外修复后运行 `cd src && dotnet format --verify-no-changes --severity info --no-restore` 退出码 0，日志 `/tmp/syncclipboard-stage2-format-final.log`。仍有跨平台工作区加载警告，Windows 全量编译结论待 CI。`git diff --check` 通过。
