# .NET 10 与主要依赖升级执行记录

执行计划：[分步升级计划](DotNet10-And-Dependencies-Upgrade-Plan.md)。本记录沿用计划中的非 UI 验证门槛；所有需要 UI 的检查均列为范围排除，不执行，也不作为进入下一步的前置条件。

## 当前状态

- 分支：`codex/upgrade-dotnet10-dependencies`，基线 `cc7289d1bc7528ef1a8a63af4ccc7f8f55a6fd6a`。
- 步骤 1–4 已通过，当前执行步骤 5：将 Shared/Core/Server.Core/Core 测试统一 .NET 10；中央依赖版本保持基线。
- PR：[#419](https://github.com/Jeric-X/SyncClipboard/pull/419)。步骤 4 验证通过提交为 `00c7c3737fc89716d73a89a31da0e57562d24373`；步骤 5 尚待当前改动的 PR 验证，不允许进入步骤 6。
- 步骤 6–18（包括每个带字母的子步骤）：全部待执行；不得把本阶段 CI 通过解释为整体升级完成。
- 不执行 UI 验证，不要求解锁后补测，不将 UI 排除项计作通过。

验证范围统一遵循计划第 3.2 节：保留 UI 项目的编译、静态检查、包内容检查及经审查的非 UI 测试；需要创建窗口/控件、初始化 UI 框架或使用 UI 调度线程的检查均排除，包括隐藏窗口和无头 UI 测试。每步记录具体排除项及原因；仅这些 UI 项未验证不阻塞下一步，范围内检查仍须独立通过，CI 相关改动仍须提交 PR 并监控问题。阶段或最终结果仅表示非 UI 验证通过。

各步骤及子步骤的记录统一使用“非 UI 验证通过”“范围内验证待完成/失败”和“UI 范围排除”区分结果。UI 排除项不列为待补测，不要求人工操作或 macOS 解锁；必要的非 UI 检查及当前提交的 PR CI/问题监控完成前，仍不得开始下一步。

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

### 步骤 2 最终通过证据

验证通过提交：`8d0eb50fec2ba4d16bef61d4233502da45bff058`。[PR build 34756063481](https://github.com/Jeric-X/SyncClipboard/actions/runs/34756063481)、[push build 34756061780](https://github.com/Jeric-X/SyncClipboard/actions/runs/34756061780)、[CodeQL 34756063226](https://github.com/Jeric-X/SyncClipboard/actions/runs/34756063226) 及 CodeFactor 完成。最终 103 项成功、8 项预期发布任务跳过，无失败或运行中检查；现有 4 个评审线程已解决，当前 head 的 Codex 评审完成，无新增可处理问题。

下载全部 47 个 artifact；五份 TRX 由仓库校验器实际解析：Core 346、三平台 Desktop 各 4、WinUI3 net10.0 为 4，共 362 通过、0 失败/跳过。38 个 Windows/Linux 构建和包组合通过完整性、框架、自包含模式、资源和原生 CPU 架构检查；报告 `/tmp/syncclipboard-pr419-8d0e-package-audit.json`，退出码 0。Windows 产物声明 net10.0，非自包含框架最低版本 10.0.0，自包含实际携带 .NET/ASP.NET Core 10.0.11；Linux 保留 net8。两个 macOS dmg 的主程序、每包 19 个 dylib、资源及严格签名通过，已卸载；下载的服务器 DLL 两轮隔离 API/持久化冒烟通过。全程未启动桌面产物。

步骤 2 通过，允许进入步骤 3；这不表示后续依赖升级或 UI 验收完成。

## 步骤 3：Avalonia 桌面层、Default 入口与测试升级 .NET 10

前置步骤通过提交：`8d0eb50fec2ba4d16bef61d4233502da45bff058`。状态：已通过（见本节最终证据）。

Desktop、Test.Desktop 改为 net10.0，Default 改为 `net10.0-windows10.0.17763.0;net10.0`，MacOS 保持 net10.0-macos；Avalonia 11.3.18、FluentAvalonia 2.3.0 及全部中央依赖版本不变。同步 Linux 发布 TFM、备用 Windows 入口 CI、调试配置及 AGENTS/CLAUDE。ReleaseAva 调试任务原来发布共享类库并寻找其可执行文件，随入口路径同步修正为 Default 项目，显式选择 net10.0。

本步骤的门槛为 Desktop 非 UI 测试、Default 双 TFM 编译、macOS 双架构发布及真实 PR 的原平台打包矩阵；以下保留实施与修复过程，最终结论见本节末尾。

Linux 安装文档明确 .NET 10 的 glibc >= 2.27 和 OpenSSL >= 1.1.1 要求。首次尝试把版本约束及 libssl 备选表达式写入包配置，被 PupNet 1.8.0 的 SafeNoSpace/安全字符校验拒绝；该工具只支持包名。当前修复保留 RPM Requires 与 deb Recommends 的原策略，添加显式 glibc 并将旧 libssl 名称改为 libssl3，不声称包管理器已强制上述版本下限。未把运行时包名写成强制包管理器依赖，避免拒绝通过官方脚本安装的运行时；中英文安装说明要求 no-dotnet-runtime 包安装匹配架构的 ASP.NET Core 10 运行时。依据：[支持系统/libc 表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)、[OpenSSL 变更](https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/openssl-version-requirement)、[Ubuntu 原生依赖](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install)，2026-09-13 核对。新 deb/rpm 元数据须随 PR 产物实际检查。

本机 SDK 10.0.302 下 Desktop net10 非 UI 测试 4 通过、0 失败/跳过；Linux x64 自包含发布、Default Windows TFM 编译、macOS arm64 发布均退出 0，日志分别为 `/tmp/syncclipboard-stage3-desktop-test.log`、`/tmp/syncclipboard-stage3-linux-publish.log`、`/tmp/syncclipboard-stage3-default-windows-build.log`、`/tmp/syncclipboard-stage3-macos-publish.log`。资产确认 Avalonia/FluentAvalonia 仍为原版本；Linux 运行框架为 net10.0，本机自包含运行时为 10.0.10。格式检查新增 IDE0330：将轮询监听器的私有同步锁改为 System.Threading.Lock，保留三处临界区；最终复验结果待补充。

最终本地复验：Desktop net10 非 UI 4 通过、0 失败/跳过（`/tmp/syncclipboard-stage3-final-results/`）；macOS arm64 发布退出 0（`/tmp/syncclipboard-stage3-macos-final.log`）；仓库完整格式检查退出 0（`/tmp/syncclipboard-stage3-format-final.log`，跨平台工作区加载警告保留，最终全量结论待 CI）。工作流 YAML、AGENTS/CLAUDE 指令一致性及 `git diff --check` 通过。步骤 3 尚待新 head 的 CI、评审和产物证据。

步骤 3 首轮 Linux 打包失败原因已定位为上述 PupNet 配置语法限制，相关取消和跳过均不计通过；本步需在修复后的新 head 重新验证完整打包矩阵。源码依据：[v1.8.0 配置读取器](https://github.com/kuiperzone/PupNet-Deploy/blob/v1.8.0/PupNet/ConfigurationReader.cs)，原工具版本继续保留，步骤 17c 再核定升级。

PupNet 修复的本地验证使用与 CI 相同的 1.8.0 工具：旧配置副本通过 --upgrade-conf 命令复现拒绝，修正后的 deb/rpm/AppImage 副本均成功解析（退出码 0），报告目录 `/private/tmp/syncclipboard-pupnet-config-check/`。此检查仅证明配置解析，不能替代真实 Linux 打包；新 head 仍须重新跑 PR。

补充非 UI 协议验证：临时 SDK.Web net10 宿主直接编译现有服务器 Program.cs 并引用原 Server.Core；实际 OfficialAdapter/WebDavAdapter 命令行探针分别以 net8/net10 编译。四种客户端/宿主组合均通过认证、Profile/文件完整性、历史查询、SignalR Profile/历史推送、显式断开重连及取消。重复内容更名后按服务器返回的 canonical DataName 下载，符合服务器复用既有历史记录的协议；不把上传临时文件名当作最终引用。报告 `/tmp/syncclipboard-stage3-protocol-matrix.json`，探针 `/private/tmp/syncclipboard-protocol-probe/`，宿主 `/private/tmp/syncclipboard-stage3-headless-host/`，驱动 `/private/tmp/syncclipboard_protocol_matrix.py`。这不覆盖网络中断后的自动重连、代理/HTTPS 或外部 S3/WebDAV 服务；后续相应依赖步骤仍需专门回归。产品 Server TFM 未提前变更。


### 步骤 3 最终证据

验证通过提交：`a588ea95ac67e0dd91e60d22b5dbf78630d61952`。[PR build 34758064136](https://github.com/Jeric-X/SyncClipboard/actions/runs/34758064136)、[push build 34758062022](https://github.com/Jeric-X/SyncClipboard/actions/runs/34758062022)、[CodeQL 34758063806](https://github.com/Jeric-X/SyncClipboard/actions/runs/34758063806) 及 CodeFactor 完成。最终 103 项成功、8 项预期发布任务跳过，无失败或运行中检查；当前 head 的 Codex 评审完成，4 个原评审线程均已解决，无新增可处理评论。

下载全部 47 个 artifact。五份 TRX 经校验共 362 通过、0 失败/跳过；38 个 Windows/Linux 构建目录和包组合的完整性、net10 运行框架、自包含模式、资源及原生库架构检查通过，报告 `/tmp/syncclipboard-pr419-a588-package-audit.json`（SHA-256 `90c756b9c6bcec2d199721761ed924e6f35e1fc4672d8c8f6c4dec489b1ddc3b`）。4 个 deb、2 个 rpm 的名称、版本、架构与修复后的依赖元数据通过，报告 `/tmp/syncclipboard-pr419-a588-linux-metadata.json`。两个 macOS dmg 的主程序、各 19 个 dylib、资源及严格签名检查通过，已卸载。服务器产物仍为 net8，两轮 API/持久化冒烟通过。

步骤 3 通过，允许进入步骤 4。UI 排除项不计通过，此结论不表示剩余升级已完成。

## 步骤 4：独立服务器与容器升级 .NET 10

前置步骤通过提交：`a588ea95ac67e0dd91e60d22b5dbf78630d61952`。状态：已通过；以下保留过程记录，最终证据见本节末尾。

Server 改为 net10.0，Server.Core/Shared 暂留 net8.0；同步 server-build 产物路径、中英文服务器运行时说明、两份调试配置及 AGENTS/CLAUDE。server-release 消费 `Server/publish/*`，与外层产物目录保持一致，无需修改，也不执行发布。

Docker 使用 `mcr.microsoft.com/dotnet/sdk:10.0.302-noble` 与 `mcr.microsoft.com/dotnet/aspnet:10.0-noble`，明确 Ubuntu 24.04 Noble，SDK 与 global.json 对齐；运行时标签保留同一主版本的补丁更新。依据：[官方默认基础系统变更](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/10.0/default-images-use-ubuntu)、[官方镜像标签](https://github.com/dotnet/dotnet-docker/blob/main/README.aspnet.md)。本机未配置 Docker；标签实际可拉取、发行版、运行时、架构和冒烟结果须由当前 PR 的 amd64/arm64 原生容器作业证明，不能以文档或 Dockerfile 文本代替。

旧版服务器产物已生成隔离数据基线 `/private/tmp/syncclipboard-server-net8-fixture/baseline`，包含两条历史、收藏/置顶、时间与文件；测试仅使用副本。临时 net10 宿主已验证两次重启后 schema、所有记录字段、文件哈希、排序和收藏查询保持一致，并验证副本清理成功、原基线未改。此准备性检查不替代本步骤实际 Server net10 产物的 R3/R4，后者须在发布后重新执行。

本机实际 Server net10 Release 发布成功（`/tmp/syncclipboard-stage4-publish.log`）；两轮认证/API/文件/持久化冒烟及正常退出通过（`/tmp/syncclipboard-stage4-smoke.log`）。新旧客户端与实际 net8/net10 服务器四组互通通过（`/tmp/syncclipboard-stage4-protocol-matrix.json`），覆盖 Official/WebDAV 协议、SignalR Profile/历史推送、显式重连、取消及数据完整性；自动重连、代理/HTTPS、外部 S3/WebDAV 仍需后续专门回归。实际新服务器读取旧库副本、两次重启、全部字段/时间/排序/收藏/置顶、文件哈希及清理通过，原库保留（`/tmp/syncclipboard-stage4-persistence.json`）。

容器 CI 增加 Ubuntu 24.04 和主机架构一致性检查，并记录实际运行时。冒烟检查现在要求进程/容器正常退出，容器在检查退出码与 OOM 状态后清理；超时强杀或非零退出均失败。本机真实服务器正常退出通过，退出码 23 的命令行进程被校验器正确拒绝。Docker 分支待真实 CI 验证。

仓库格式检查退出 0（`/tmp/syncclipboard-stage4-format.log`，保留跨平台工作区加载警告）；YAML/Python 语法、AGENTS/CLAUDE 一致性及框架边界检查通过。中央 NuGet 版本未变，SQLitePCLRaw 已知漏洞仍待步骤 7 处理。步骤 4 尚待新 head 的 CI、评审、实际容器与产物验证，不允许进入步骤 5。


### 步骤 4 首轮结果及首次启动修复

`27b2f0de19152ff15ed66a9d518c7722c7d34920` 的 [PR build 34759199481](https://github.com/Jeric-X/SyncClipboard/actions/runs/34759199481)、[push build 34759197877](https://github.com/Jeric-X/SyncClipboard/actions/runs/34759197877) 与 [CodeQL 34759199232](https://github.com/Jeric-X/SyncClipboard/actions/runs/34759199232) 完成：103 成功、8 预期发布任务跳过，当前 head 的评审完成、无新增未解决问题。47 个 artifact 已下载；362 项非 UI 测试、38 项 Windows/Linux 包组合、6 项 Linux 元数据、两 macOS dmg 的签名/资源/架构检查均通过。报告目录前缀 `/tmp/syncclipboard-pr419-27b2-`。容器实测 Ubuntu 24.04.5 LTS、SDK 10.0.302、最终 ASP.NET Core/.NET 10.0.12，amd64/arm64 均通过挂载、认证、端口、持久化和正常退出检查。

该 PR 服务器产物另通过旧数据副本验证和 HTTPS 两轮冒烟：TLSv1.2、主机名/证书链校验、拒绝未信任证书、认证、SignalR 协商、文件完整性与重启后持久化通过，未改系统证书信任（`/tmp/syncclipboard-pr419-27b2-https.json`）。实际适配器 HTTP 代理检查观察到 18 条连接，覆盖 Profile/文件/历史 API 与 SignalR 协商、WebSocket CONNECT；普通 HTTP 连接被测试代理显式关闭以观察每条路由（`/tmp/syncclipboard-stage4-proxy.json`）。这不表示自动重连或 S3 集成已验证。

追加的 Production 空配置目录检查发现首次启动问题：复制默认 appsettings.json 后追加 JSON provider，使命令行端口被文件内的 5033 覆盖。已有相同源码路径也存在该问题；上述 CI 成功不足以覆盖此场景，因此步骤 4 尚未通过。修复改为复制后重新加载现有 provider，保留默认配置优先级；依据：[ASP.NET Core 配置优先级](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0)。

新增 `build/verification/server_startup_smoke.py`，由 Server 发布及两架构容器 CI 执行：空目录首次启动复制默认配置，分别验证命令行和环境变量覆盖端口，环境变量认证生效、默认账号被拒绝、Production 不暴露 Swagger，并重启检查持久化和正常退出。容器使用不同于默认值的 5034 内部端口，映射至宿主 loopback 随机端口；不发布镜像或操作 UI。

修复后的实际服务器本地四轮首次启动/重启场景与原两轮冒烟全部通过；发布与格式检查退出 0（`/tmp/syncclipboard-stage4-startup-fixed.log`、`/tmp/syncclipboard-stage4-fixed-smoke.log`、`/tmp/syncclipboard-stage4-fixed-format.log`）。YAML/Python 语法及 diff 检查通过。新增 CI 门槛必须在修复后的 head 实际通过后，才允许进入步骤 5。

后续 Core/EF 数据验证基线已准备：当前 Core net8/EF 9 实际生成 65 条客户端历史、32 个文件哈希，包含时间、收藏/置顶、同步/删除状态和 50+15 两页查询。基线 `/private/tmp/syncclipboard-client-db-baseline`，只读保留包 `/private/tmp/syncclipboard-client-net8-data-baseline.zip`，含源码、程序集指纹和期望结果；后续仅在副本上验证。产品 Core TFM 尚未改动。


### 步骤 4 最终证据

验证通过提交：`00c7c3737fc89716d73a89a31da0e57562d24373`。[PR build 34760281019](https://github.com/Jeric-X/SyncClipboard/actions/runs/34760281019)、[push build 34760278342](https://github.com/Jeric-X/SyncClipboard/actions/runs/34760278342)、[CodeQL 34760280696](https://github.com/Jeric-X/SyncClipboard/actions/runs/34760280696) 及 CodeFactor 完成，103 成功、8 预期发布任务跳过。当前 head 的 Codex 评审完成，原 4 个评审线程已解决，无新增可处理问题。

新增首次启动场景在 Server、amd64 容器、arm64 容器中各实际执行 4 轮并通过；逐一读取三个 CI 日志确认命令行/环境变量端口、认证、Production Swagger 关闭、重启与正常退出均被覆盖。下载的新服务器产物本机复验亦通过，旧数据库副本全部字段、时间、排序、收藏/置顶、文件哈希及清理保持正确，原基线未改。47 个 artifact 下载完成，五份 TRX 共 362 通过、0 失败/跳过；38 项 Windows/Linux 包组合、6 项 Linux 元数据及两 macOS dmg 的资源/原生架构/严格签名检查通过。日志和报告前缀 `/tmp/syncclipboard-pr419-00c7-`。

旧客户端数据另在独立 net10 宿主中使用当前 Core net8 验证，65 条记录、50+15 分页、时间/状态/收藏及 32 个文件哈希与基线完全一致（`/tmp/syncclipboard-client-db-net10-host.log`）；该证据不替代下一步实际 Core net10 的验证。步骤 4 通过，允许进入步骤 5，UI 排除项不计为通过。

## 步骤 5：共享库与 Core 测试统一 .NET 10

前置步骤通过提交：`00c7c3737fc89716d73a89a31da0e57562d24373`。状态：进行中。

Shared、Core、Server.Core、Test 改为 net10.0，至此所有产品及测试项目均使用 .NET 10 对应目标。中央 NuGet 版本保持基线；SDK 仍由 global.json 选择 10.0.302 并允许同 feature band 的补丁滚动。CI 移除为旧项目目标显式安装的 8/9 SDK；Linux 打包保留 .NET 8 以运行 PupNet 1.8.0，待步骤 17c 处理。同步 AGENTS/CLAUDE；AppVeyor 路径属于先前记录的非活动遗留配置，不作为当前验证依据。

升级前七个 RID 的条件符号已实际查询并保存 `/tmp/syncclipboard-core-rid-baseline.json`：win-x86/x64/arm64 仅 WINDOWS，linux-x64/arm64 仅 LINUX，osx-x64/arm64 仅 MACOS。本步须在实际 Core net10 上复核这些符号，完成范围内测试、各入口构建、旧客户端数据副本及新旧协议互通，并通过当前 head 的真实 PR 矩阵、评审和 R1–R5 后才允许步骤 6。


### 步骤 5 本地适配与验证

目标切换后格式检查新增 IDE0330、IDE0031 与 CA2263：22 个私有锁字段改用 `System.Threading.Lock`，逐字段核对没有 Monitor 调用或对象外传，保留原锁体；4 个事件退订采用空条件赋值，仍随后清空字段；服务器枚举筛选使用泛型 `Enum.GetValues<ProfileType>()`。另移除锁迁移后不再需要的 HubConnection 初值，以及 SDK 隐式导入的重复 using。未屏蔽诊断，未升级中央 NuGet 包。

macOS arm64 本机 Release 验证结果：

| 检查 | 结果与证据 |
| --- | --- |
| Core 与 Desktop 非 UI 测试 | 346 + 4 通过、0 失败/跳过；仓库 TRX 校验器按最低 350 项核验通过，`/private/tmp/syncclipboard-stage5-verified-results/` |
| 框架与平台条件 | 11 个项目均为 net10 对应目标；7 个 RID 的 WINDOWS/MACOS/LINUX 与基线一致，`/tmp/syncclipboard-stage5-frameworks.json`、`/tmp/syncclipboard-stage5-core-rids.json` |
| 入口编译/发布 | Server、Linux x64 自包含、Desktop.Default Windows TFM 编译及 macOS arm64 发布成功，日志 `/tmp/syncclipboard-stage5-final-*.log`；未启动桌面产物 |
| 格式与静态检查 | 最终 format 退出 0，保留跨平台 workspace 加载警告；YAML 可解析，AGENTS/CLAUDE 共同说明一致，diff 检查通过，中央版本文件与基线一致 |
| 实际 Core net10 读取旧客户端数据 | 65 条记录、50+15 分页、时间/排序/状态/收藏及 32 个文件哈希与旧库一致，`/tmp/syncclipboard-stage5-client-db-verify.log`；基线未修改 |
| 实际 Server/Core net10 读取旧服务器数据 | 两次重启后 schema、全部字段、时间/排序/收藏/置顶和文件哈希保持一致；副本清理通过、原库保留，`/tmp/syncclipboard-stage5-final-persistence.json` |
| 启动与 API | Production 命令行/环境变量各首次启动与重启共 4 轮通过；另两轮认证、API、文件、持久化和正常退出通过，`/tmp/syncclipboard-stage5-final-startup.log`、`/tmp/syncclipboard-stage5-final-smoke.log` |
| 新旧协议 | 实际 net8/net10 客户端与旧 net8/新 net10 服务器四组互通通过，包含 Official/WebDAV、SignalR Profile/历史推送、显式停止再连接、预取消及完整性，`/tmp/syncclipboard-stage5-protocol-matrix.json` |
| HTTP 代理 | 实际 Core net10 适配器通过 loopback 隔离代理，观察到 18 条 API/SignalR 协商及 WebSocket CONNECT 连接，无代理错误，`/tmp/syncclipboard-stage5-proxy.json` |
| 服务器 HTTPS | 临时证书链/主机名验证、拒绝未信任证书、TLSv1.2、两轮认证/API/SignalR 协商、文件持久化及正常退出通过；未修改系统信任，`/tmp/syncclipboard-stage5-https.json` |

以上 HTTPS 检查使用独立 TLS 测试客户端，不代表 OfficialAdapter 全部证书场景；显式断开再连接也不代表网络故障自动重连。自动重连、实际外部 S3/WebDAV 等专门回归仍按后续依赖步骤完成。Windows 原生构建/测试、完整平台及发行包矩阵须由当前提交的 PR 实际验证；步骤 5 尚未通过，不允许进入步骤 6。UI 排除项不执行、不计通过、不安排补测。
