# .NET 10 与主要依赖升级执行记录

执行计划：[分步升级计划](DotNet10-And-Dependencies-Upgrade-Plan.md)。本记录不替代计划中的任何验证门槛。

## 当前状态

- 分支：`codex/upgrade-dotnet10-dependencies`，基线 `cc7289d1bc7528ef1a8a63af4ccc7f8f55a6fd6a`。
- 当前为步骤 1：统一工具链并补齐非 UI CI。目标框架和中央依赖版本均未改变。
- PR：[#419](https://github.com/Jeric-X/SyncClipboard/pull/419)。步骤 1 首次提交 `6dcae944`；当前阶段的全部 CI、产物检查及评审收敛尚未完成，不允许进入步骤 2。
- 步骤 2–18（包括每个带字母的子步骤）：全部待执行；不得把本阶段 CI 通过解释为整体升级完成。
- 不执行 UI 验证，不要求解锁后补测，不将 UI 排除项计作通过。

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
