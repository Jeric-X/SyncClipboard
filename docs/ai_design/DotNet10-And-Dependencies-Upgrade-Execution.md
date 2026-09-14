# .NET 10 与主要依赖升级执行记录（仅非 UI 验证）

执行计划：[分步升级计划](DotNet10-And-Dependencies-Upgrade-Plan.md)。本记录沿用计划中的非 UI 验证门槛；所有需要 UI 的检查均列为范围排除，不执行，也不作为进入下一步的前置条件。

后续追加记录也必须遵守此范围，无论 macOS 是否锁屏：遇到需要 UI 的验证建议，记录为“范围排除：按用户要求不验证”，不新增 UI 测试或补测待办；相关代码兼容性问题仍通过源码分析、编译和可隔离的非 UI 检查处理。每一步仍须独立完成必要的非 UI 验证，涉及 CI 的内容仍须提交 PR 并监控、处理问题后，才能进入下一步。

**2026-09-14 最新产物范围：** 完全信任 CI 编译与打包结果；从当前步骤 14 起，不再下载远程编译产物或复用已下载产物进行二进制、程序集/原生库、签名、包结构、元数据或哈希审计。55 份 artifact 下载、38 个组合及 macOS 双架构 DMG 审计不再是待办或通过条件。若确有必要额外验证二进制，只检查本地编译的 macOS 版本；不例行重复。历史记录保留原事实，其中要求继续下载/审计远程产物的待办自此取消。仍独立核对非 UI 测试、当前 head 的 CI 与评审反馈。

## 当前状态

- 分支：`codex/upgrade-dotnet10-dependencies`，基线 `cc7289d1bc7528ef1a8a63af4ccc7f8f55a6fd6a`。
- 步骤 1–17b（含全部子步骤和评审修复）已通过，当前执行步骤 17c：PupNet 与打包工具运行时。
- PR：[#419](https://github.com/Jeric-X/SyncClipboard/pull/419)。步骤 17b 验证通过提交为 `cc2022c0aaa67b4e507d5467850995b67a97eaf9`；步骤 17c 未通过前不进入 18。
- 步骤 18：待执行；不得把本阶段 CI 通过解释为整体升级完成。
- 不执行 UI 验证，不要求解锁后补测，不将 UI 排除项计作通过。

### 验证范围与阶段门槛

所有步骤（包括最终验收）统一遵循计划第 3.2 节，以下规则同时适用于本地、CI 和评审修复：

| 内容 | 处理方式 |
| --- | --- |
| UI 项目的 C#/XAML 编译、源码兼容性分析、依赖与 CI 打包结果 | 保留 |
| 下载远程编译产物或做二进制审计 | 取消；完全信任 CI，不作为后续阶段门槛 |
| 隔离的业务逻辑、ViewModel、属性通知、命令、集合和 mock 测试 | 确认初始化、执行、清理全程不依赖 UI 后运行 |
| 图片编解码、像素/透明通道及纯 Bitmap 数据转换 | 保留；不打开预览、不创建控件、不访问真实剪贴板 |
| 桌面启动、窗口/控件、运行时 XAML、UI 框架或 UI 调度线程 | 范围排除；隐藏窗口、无头模式和虚拟显示器也不执行 |
| Quartz 更新任务的真实 UI 调度、更新界面及交互回调 | 范围排除；替身只能记录回调交接，不执行回调，不把记录成功视为 UI 验证通过 |
| 真实剪贴板、拖拽、托盘、热键、全局 hook、输入设备、模拟按键、权限提示 | 范围排除；仅保留不访问真实桌面的静态或 mock 检查 |
| 真实 TaskDialog、UI Automation 客户端、通知服务、图形安装向导、Swagger UI | 范围排除；不以命令行入口作为非 UI 的判断依据 |
| UI 截图、视觉比较、人工交互及解锁后补测 | 范围排除；不列入待办或通过条件 |

每步独立记录必要非 UI 检查的结果，完成当前提交的 PR CI 与问题监控后再进入下一步。Core、Desktop、WinUI3 测试统一使用经审查的 `--filter "TestCategory=NonUI"` 正向筛选，记录实际执行数及 UI 排除项；不直接执行未筛选的整个测试项目，筛选后零项不能算通过。无法隔离 UI 操作的用例直接排除，定时任务测试同样适用。

需要 UI 的内容统一记录为“范围排除：按用户要求不验证”，实际执行数量为 0，不计作通过、不列为阻塞、不安排补测。范围内检查仍有失败或缺少证据时，当前步骤保持未通过。仅缺少 UI 验证时，不影响已满足其余门槛的步骤继续推进。

后续阶段记录和 PR 描述沿用同一表述，不把 UI 排除项写成“待验证”“待解锁”或“待人工验收”；本次文档调整本身不执行任何 UI 检查。

历史记录及最终交付中的“已通过”“全部检查”“全平台验证”均限定在上述非 UI 范围内。编译、mock 和包检查不能证明实际 UI 行为已验收；本次范围调整不改变已有验证结果。

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

前置步骤通过提交：`00c7c3737fc89716d73a89a31da0e57562d24373`。状态：已通过；以下保留过程记录，最终证据见本节末尾。

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


### 步骤 5 最终证据

验证通过提交：`38f925cb5d15c688e147f96a64bc247b658d7d0e`。[PR build 34762087310](https://github.com/Jeric-X/SyncClipboard/actions/runs/34762087310)、[push build 34762085899](https://github.com/Jeric-X/SyncClipboard/actions/runs/34762085899)、[CodeQL 34762087083](https://github.com/Jeric-X/SyncClipboard/actions/runs/34762087083) 及 CodeFactor 均完成，103 项成功、8 项预期发布任务跳过。当前提交的 Codex 评审完成，无新增可处理问题，原 4 个线程全部解决。

47 个 artifact 全部下载；5 份 TRX 共 362 项非 UI 测试通过、0 失败/跳过。38 项 Windows/Linux 包组合及 6 项 Linux 元数据检查通过；两架构 macOS 包每包 19 个 dylib、主程序、资源及严格签名通过并卸载。除 runtimeconfig/deps 外，新增仅解析 PE 元数据的检查：每个包的入口、Core、Shared、Server.Core 及适用的 Desktop 程序集，其 TargetFrameworkAttribute 均为 .NETCoreApp,Version=v10.0；实际旧 net8 Core 被该检查正确拒绝。没有执行桌面产物。

分别读取 Server、amd64、arm64 CI 日志，均实际通过 4 轮 Production 首次启动/重启和 2 轮 API 冒烟；容器运行于 Ubuntu 24.04.5 LTS、ASP.NET Core/.NET 10.0.12。下载的新 Server 产物在本机复验首次启动、旧数据副本及 HTTPS 通过。报告前缀 `/tmp/syncclipboard-pr419-38f9-`。全部 11 个项目 TFM 统一为 .NET 10 对应目标，中央 NuGet 保持基线；步骤 5 非 UI 验证通过，允许进入步骤 6。

## 步骤 6：升级微软基础库

前置步骤通过提交：`38f925cb5d15c688e147f96a64bc247b658d7d0e`。状态：已通过；以下保留过程记录，最终证据见本节末尾。先核定 DI、Logging.Console、SignalR.Client、System.Net.Http.Json 的稳定 10.0 补丁及传递依赖，再作本步改动；EF、Swagger 等按后续独立步骤处理。


2026-09-13 首次核定本步四个包的候选稳定版本均为 10.0.12：
[DI](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.12)、
[Logging.Console](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Console/10.0.12)、
[SignalR.Client](https://www.nuget.org/packages/Microsoft.AspNetCore.SignalR.Client/10.0.12)、
[Http.Json](https://www.nuget.org/packages/System.Net.Http.Json/10.0.12)。10.0.12 为 2026-09-08 发布的稳定补丁；11.0 仍为预发行，不纳入本计划。首次仅修改中央版本文件中这四项，随后按下文处理冗余引用；还原前 assets 快照保存在 `/private/tmp/syncclipboard-stage6-baseline/`，还原后比较实际依赖图。

按[官方 .NET 10 兼容性清单](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10) 核对 DI keyed service、配置 null、Console 日志及 JSON 行为。当前客户端自动恢复由 OfficialEventDrivenServer/TestAliveHelper 管理，不能仅测试 HubConnection 的显式重建；本步另须通过实际断网/恢复模拟验证。内置服务器直接通过独立测试宿主调用 Web.StartAsync，替代真实托盘与通知等 UI 服务。


还原后的 NU1510 指出 Http.Json 直接引用冗余。实际 net10 assets 中该包 compile/runtime 都是 `_._`，发布产物没有独立 Http.Json DLL 或 deps 运行资产；它已经使用 .NET 10 共享框架实现。因此按[官方 NU1510 处理要求](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nu1510-pruned-references) 移除 Core 唯一直接引用及对应中央版本项，保留所有 Http.Json API 调用，由部署运行时供给实现。最终本步三个独立 NuGet 包为 10.0.12，Http.Json 不再有单独包版本；不以屏蔽警告或虚报包版本处理。


### 步骤 6 本地验证

最终组合在 macOS arm64 Release 下验证：Core 346、Desktop NonUI 4 项通过，0 失败/跳过，TRX 校验器最低 350 项通过（`/private/tmp/syncclipboard-stage6-final-results/`）。还原图确认相关微软传递组件均解析至 10.0.12，Http.Json 包已移除；无 NU1510、包降级或依赖冲突，EF/SQLite、Swagger 版本未变（`/tmp/syncclipboard-stage6-final-dependency-diff.json`）。Linux x64 自包含、macOS arm64 发布及 Desktop.Default Windows TFM 编译通过，未运行桌面产物。

独立验证宿主调用真实 `Web.StartAsync`，通过 `AppCore.ConfigCommonService` 与 keyed DI 获取实际 OfficialAdapter，并使用实际 OfficialEventDrivenServer/TestAliveHelper；托盘和通知使用 mock，Profile 环境、应用配置和 Core 日志为测试实现；微软 Console 日志提供程序保持真实。服务只监听 loopback，配置/数据库写入临时目录。先验证推送，再停止服务器形成真实连接中断，保留至少 11 秒离线健康检查窗口，重新启动同一端口；未手工调用客户端 StartListening，客户端自行重连并再次通过推送和 HTTP JSON 读取，预取消请求被拒绝。结构化 Console 日志标记仅输出一次，宿主正常退出（`/tmp/syncclipboard-stage6-final-reconnect.json` 与 `.log`）。

实际加载信息：SignalR.Client.Core、DI.Abstractions、Logging.Console 的 ProductVersion 为 10.0.12；Http.Json 来自本机共享运行时 10.0.10。后者跟随部署运行时的补丁版本，不能将移除的 NuGet 候选版本写成实际运行版本。早期临时宿主在服务器运行中重写监听配置触发 Kestrel 热重载，导致自身请求竞态；调整为旧服务器停止后再写重启端口，未为测试问题修改产品代码。最终组合已重新执行通过。

冻结的 net8/微软 9 客户端与当前 net10/微软 10.0.12 客户端，分别连接旧 net8 服务器和步骤 5 的 net10 服务器产物，四组 Official/WebDAV、Profile/文件哈希、历史、SignalR 推送、显式连接重建及预取消通过（`/tmp/syncclipboard-stage6-final-protocol-matrix.json`）。本步独立 Server 的源码和依赖图不受这三个 Core 基础包引用影响；内置服务器加载新依赖的路径由上面的真实宿主覆盖。实际客户端 HTTP 代理检查通过，观察到 18 条普通 API/协商与 WebSocket CONNECT 路由，无代理错误（`/tmp/syncclipboard-stage6-proxy.json`）。

最终格式检查退出 0（`/tmp/syncclipboard-stage6-final-format.log`），保留跨平台工作区加载警告；diff 检查通过。步骤 6 尚待当前提交的 PR 全平台构建/测试与评审；通过前不进入步骤 7。既有 SQLitePCLRaw 与 Magick.NET 漏洞警告保留，分别在步骤 7、11 处理；UI 项持续排除，不安排补测。


### 步骤 6 最终证据

验证通过提交：`d16e70788a26cfb9d69eaa83e5e9db946db64e37`。[PR build 34763804621](https://github.com/Jeric-X/SyncClipboard/actions/runs/34763804621)、[push build 34763802133](https://github.com/Jeric-X/SyncClipboard/actions/runs/34763802133)、[CodeQL 34763804354](https://github.com/Jeric-X/SyncClipboard/actions/runs/34763804354) 及 CodeFactor 完成，103 项成功、8 项预期发布跳过。当前 Codex 评审完成，无新增可处理问题，原 4 个线程已解决。

全部 47 个 artifact 已下载，五份真实 TRX 共 362 项非 UI 测试通过、0 失败/跳过。38 项 Windows/Linux 包组合、6 项 Linux 元数据、两架构 macOS 包的资源/架构/严格签名检查通过。每包实际核验 DI、DI.Abstractions、Console 日志、SignalR.Client.Core、Http.Connections.Client 的 ProductVersion 为 10.0.12；入口和自有共享程序集仍为 .NET 10，deps 中不含冗余 Http.Json 包。自包含 Windows/Linux 的 Http.Json 随运行时为 10.0.11；macOS 为 10.0.10，与 CoreLib 的数字补丁及源码提交一致（CoreLib 额外带 servicing 构建后缀）；非自包含产物由安装的 .NET 10 运行时提供。未将 NuGet 版本误认为所有框架程序集版本。

当前 Server 与两架构容器 CI 日志均确认执行 4 轮首次启动/重启与 2 轮 API 冒烟；下载的服务器产物本机首次启动复验通过。报告前缀 `/tmp/syncclipboard-pr419-d16e-`。步骤 6 非 UI 验证通过，允许进入步骤 7；不代表后续依赖升级已完成。

## 步骤 7：升级 EF Core / SQLite

前置步骤通过提交：`d16e70788a26cfb9d69eaa83e5e9db946db64e37`。状态：非 UI 验证通过；以下保留过程记录，最终证据见本节末尾。先核定稳定 EF Core 10.0 补丁及 SQLite 原生依赖；以旧客户端、旧服务器数据库副本验证迁移、查询、分页、收藏/置顶、删除/清理、时间和文件哈希，保留原始数据基线。不得仅因版本升级生成空迁移或重建数据库。

### 步骤 7 版本与适配

三个直接 EF 包（Core、Sqlite、Design）统一从 9.0.8 升级为 10.0.12，精确版本仅写入中央版本文件。[官方 NuGet 依赖](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Sqlite/10.0.12)要求 SQLitePCLRaw 至少 2.1.12；实际还原为 bundle/core/provider/lib 全套 2.1.12，原生 SQLite 为 3.53.3。本步使用 EF 配套依赖，不额外引入 SQLitePCLRaw 3 的新包分拆。原 2.1.10 漏洞警告消失，未添加漏洞抑制。EF Design 同步带入 Roslyn 5、MSBuild 18 等设计期依赖，完整差异保存在 `/tmp/syncclipboard-stage7-SyncClipboard.Core-dependencies.json` 和对应 Server.Core 报告中。

[EF 10 破坏性变更](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes)包含集合参数翻译和 SQLite 时间读取语义变化。本项目数据库时间使用 DateTime 与既有 UTC 属性处理；未启用旧时间行为开关，未添加空迁移、重建数据库或改动持久化模型。新增 `HistorySqliteTimeTests` 六项非 UI 回归，覆盖客户端/服务端无偏移旧时间、正负偏移时间以及保存后重新读取，均核对 UTC 时刻和 Kind。CI Core 最低通过数由 346 提高为 352，必须由当前 PR 实际执行证明。

### 步骤 7 本地数据库与协议证据

- 冻结的 net8/EF9 客户端数据库副本：65 条记录的全部字段、时间、状态、两页结果及 32 个文件哈希一致；原始基线 226 个文件的哈希未变。报告 `/tmp/syncclipboard-stage7-client-db-verify.log`。
- 使用当前实际 HistoryManager 的独立宿主，平台服务用 mock 且不初始化 UI：旧库排序、游标分页、Unicode 搜索、收藏查询、807 个键的分批精确收藏和版本递增通过；软删除清除记录的文件关联及工作目录；过期清理删除应删项，保留收藏、置顶、已同步和近期项；最终清空数据库通过。报告 `/private/tmp/syncclipboard-stage7-history-manager-output/result.json`。
- 冻结的旧服务器数据库副本：两次重启前后 schema、记录、时间、收藏/置顶及文件哈希不变，清空 API 删除数据和历史文件，原始基线不变。报告 `/tmp/syncclipboard-stage7-server-persistence.json`。
- 另由实际旧 net8 服务器生成 65 条记录，包含相同时间戳、收藏和置顶；新服务器在两轮启动中与旧版的 12 种查询响应逐项一致，包括 50/15/0 分页、类型过滤、Unicode 搜索、访问时间排序和带正负偏移的时间范围，schema 与原始数据保持不变。基线 `/private/tmp/syncclipboard-stage7-server-paging-fixture-v2/baseline`，报告 `/tmp/syncclipboard-stage7-server-paging.json`。首版临时探针误把 UTF-8 字节数作为文本长度，旧服务器正确拒绝上传；按协议改为 UTF-16 长度并另建基线目录，未修改产品校验。
- 新旧客户端分别连接新旧服务器，四组认证、Official/WebDAV、Profile/文件完整性、历史、SignalR 推送、显式重连及取消均通过。报告 `/tmp/syncclipboard-stage7-protocol-matrix.json`。当前服务器四轮 Production 首次启动/重启、命令行/环境配置优先级、认证、端口及正常退出通过，日志 `/tmp/syncclipboard-stage7-server-startup.log`。本步未把这些检查扩大解释为所有外部 S3/WebDAV 服务已经验收。

### 步骤 7 本地产物检查

Linux x64 自包含发布通过；包内 EF 与 Microsoft.Data.Sqlite 程序集均为 10.0.12，SQLitePCLRaw 依赖为 2.1.12，原生 SQLite 字节与该 RID 的 NuGet 包一致，其余既有程序集、运行时和架构检查通过（`/tmp/syncclipboard-stage7-linux-package-audit.json`）。Desktop.Default Windows TFM 编译通过。

macOS 首次增量发布时，输出目录已是 SQLite 3.53.3，但 `.app` 中残留 3.46.1，故该产物未通过；证据 `/tmp/syncclipboard-stage7-macos-incremental-mismatch.json`。执行目标项目 `dotnet clean` 后重新发布，实际应用包更新为 3.53.3。项目在 Build 后复制许可证，原始 publish 包尚需既有 CI 的 BundleTool 重新签名；执行同一脚本后严格签名通过，实际包内 EF/Sqlite 程序集为 10.0.12（`/tmp/syncclipboard-stage7-macos-native-verified.json`）。未启动桌面应用。计划已增加原生依赖升级的干净发布与最终包核验要求。

最终 Core 352 项、Desktop NonUI 4 项通过，0 失败/跳过；Core TRX 位于 `/private/tmp/syncclipboard-stage7-final2-results/core/`，Desktop TRX 位于 `/private/tmp/syncclipboard-stage7-final-results/desktop/`。新增测试初次格式检查提示缺少取消令牌，补齐后重跑 Core 和完整格式检查均通过，格式日志 `/tmp/syncclipboard-stage7-final-format.log`（退出 0，仅保留跨平台工作区加载警告）。步骤 7 尚待当前提交的 PR 全平台 CI、产物及评审验证，未允许进入步骤 8。Magick.NET 的既有漏洞仍在步骤 11 处理，不屏蔽警告。

### 步骤 7 最终证据

验证通过提交：`f476db56241e0b66e4a02ada97c8febb7ec2d4af`。[PR build 34765898062](https://github.com/Jeric-X/SyncClipboard/actions/runs/34765898062)、[push build 34765896549](https://github.com/Jeric-X/SyncClipboard/actions/runs/34765896549)、[CodeQL 34765897850](https://github.com/Jeric-X/SyncClipboard/actions/runs/34765897850) 及 CodeFactor 完成，103 项成功、8 项预期发布跳过。最终再次读取当前 head、checks、reviews、全部线程和评论，当前 Codex 评审完成且无新增问题，原 4 个线程均已解决。

全部 47 个 artifact 下载完成；五份 TRX 经仓库校验器核对共 368 项非 UI 测试通过，0 失败/跳过。38 项 Windows/Linux 构建与安装/便携包组合通过完整性、运行时、资源和原生架构检查；每包实际核验 5 个 EF/Sqlite 程序集为 10.0.12、SQLitePCLRaw 依赖全套为 2.1.12，并对原生 SQLite 与对应 RID 的 NuGet 字节计算哈希比对。报告 `/tmp/syncclipboard-pr419-f476-package-audit.json`，SHA256 `829045a12637892771c143bcce83fc4ee8f178cf83f367678d23391567f41f92`；6 个 Linux deb/rpm 元数据检查通过。

两架构 macOS dmg 的主程序、19 个 dylib、资源和严格签名通过，实际包内微软/EF 程序集为 10.0.12，SQLitePCLRaw 程序集为 2.1.12，原生 SQLite 为 3.53.3，没有本地增量发布时出现的旧库残留。两个只读挂载均已卸载，未执行桌面程序。报告 `/tmp/syncclipboard-pr419-f476-macos-{arm64,x64}.json`。

当前 CI Server、amd64 容器、arm64 容器各确认执行 4 轮 Production 首次启动/重启及 2 轮 API/认证/历史/传输检查。下载的当前 Server 产物在本机再次通过 4 轮启动和旧数据库副本检查：两次重启保持 schema、时间、收藏/置顶、记录和文件哈希，清空后无残留，原库未改动（`/tmp/syncclipboard-pr419-f476-persistence.json`）。步骤 7 非 UI 验证通过，允许进入步骤 8；不代表整个依赖升级已完成。

## 步骤 8：升级 Avalonia 12 与必要配套依赖

前置步骤通过提交：`f476db56241e0b66e4a02ada97c8febb7ec2d4af`。状态：非 UI 验证通过；以下保留过程记录，最终证据见本节末尾。先核定 Avalonia、FluentAvalonia、AsyncImageLoader 与 BreadcrumbBar 的稳定兼容组合，再进行 C#/XAML/API 适配；仅执行约定的非 UI 验证，不初始化 UI 框架或真实桌面。

### 版本与兼容适配

本步固定 Avalonia/Desktop/Themes.Fluent/Fonts.Inter 12.1.2、FluentAvaloniaUI 3.1.0、AsyncImageLoader.Avalonia 3.8.0。三者的目标框架和依赖要求分别核对 [Avalonia 包](https://www.nuget.org/packages/Avalonia/12.1.2)、[FluentAvalonia 包](https://www.nuget.org/packages/FluentAvaloniaUI/3.1.0)、[图片加载器包](https://www.nuget.org/packages/AsyncImageLoader.Avalonia/3.8.0)。FluentAvalonia 3 要求 .NET 10/Avalonia 12，必须在同一验证单元迁移。

移除 Avalonia.Diagnostics，以及仍依赖 FluentAvalonia 2/Avalonia 11 ItemsRepeater 的 FluentAvalonia.BreadcrumbBar 2.0.2；使用 FluentAvalonia 3 内置的 FABreadcrumbBar、FAItemsRepeater 和 FAStackLayout。删除旧面包屑样式引用和已移除依赖的 About 列表项，保留仓库原许可证文件。没有复制第三方控件源码，也没有启用新的诊断工具或 Wayland 后端。

根据实际 NuGet 包中记录的 FluentAvalonia 源码提交 `215ee0481e8b0e6d396cbe2a0f33dc791c5646ab` 核对 [面包屑元素工厂](https://github.com/amwx/FluentAvalonia/blob/215ee0481e8b0e6d396cbe2a0f33dc791c5646ab/src/FluentAvalonia/UI/Controls/BreadcrumbBar/BreadcrumbElementFactory.cs) 和项目中使用的控件 API：新工厂会将条目的 Content 设置为数据对象，因此标题改放在 ContentTemplate 中；点击索引仍传入原 ViewModel 的零基索引。原根节点/中间节点返回、当前节点不跳转及导航参数由新增四项纯 ViewModel 测试覆盖。测试使用记录调用的 IMainWindow 替身，不创建窗口、控件或 UI 调度器；这不证明实际面包屑的显示或交互已通过。

其余必要迁移包括 FluentAvalonia 控件与事件类型的 FA 前缀、窗口装饰 API、MenuItemToggleType、PlaceholderText、Binding.Mode、FocusChangedEventArgs、PNG 编码参数，以及剪贴板扩展方法命名空间；移除 BindingPlugins 旧验证器配置。Avalonia 12 拖拽 API 要求最初的 PointerPressedEventArgs，两处拖拽入口保留按下事件并在取消时清理，继续使用既有阈值、选择逻辑和 DataTransfer。macOS 专属入口的 TryGetFeature 泛型扩展改用 Avalonia 命名空间，平台编译已覆盖这项修正。依据为 [Avalonia 12 迁移说明](https://v11.docs.avaloniaui.net/docs/avalonia12-breaking-changes/) 及所选版本的包内 API 文档。

还原实际解析 SkiaSharp 3.119.4、HarfBuzzSharp 8.3.1.3；FluentAvalonia 的 ColorPicker/DataGrid 为 12.1.0，构建工具 Avalonia.BuildServices 为独立版本 11.3.2。未将这些有效的传递依赖强制改为 Avalonia 主包版本；旧 BreadcrumbBar、ItemsRepeater 11 和 Diagnostics 已不在桌面依赖图中。

### 本地非 UI 验证（2026-09-14）

- 最终 Core 356 项、Desktop NonUI 4 项通过，0 失败/跳过；TRX 位于 `/private/tmp/syncclipboard-stage8-final-results/`，仓库校验器按最低 360 项通过。CI Core 门槛同步提升到 356，五个测试任务预计合计 372 项，最终以当前 PR 报告核定。
- Desktop Release 与 Debug 的 C#/XAML 编译通过；Desktop.Default 的 Windows TFM Release 编译通过。日志分别为 `/tmp/syncclipboard-stage8-build4.log`、`/tmp/syncclipboard-stage8-debug.log`、`/tmp/syncclipboard-stage8-default-windows.log`。Windows 原生打包与运行非 UI 测试仍须由当前 PR 的 Windows runner 验证。
- Linux x64 自包含发布通过，输出 `/private/tmp/syncclipboard-stage8-linux`。检查 runtimeconfig/deps、产品程序集 net10、资源及六个原生文件的架构；新 Avalonia/FluentAvalonia/图片加载器和 Skia/HarfBuzz 的实际程序集版本正确，两个 Linux 图形原生库与对应 RID 的 NuGet 文件哈希一致。沿用并通过前置步骤的 Microsoft 10.0.12、EF 10.0.12、SQLitePCLRaw 2.1.12 检查。报告 `/tmp/syncclipboard-stage8-linux-native-verified.json`。
- macOS arm64 先 clean 后 publish，修复扩展命名空间编译错误后发布通过；随后运行 BundleTool 的资源/重新签名步骤。最终包主程序和 19 个 dylib 架构正确，严格签名、图标、产品程序集 net10、Microsoft/EF/SQLite 版本及八个桌面依赖程序集版本均通过。报告 `/tmp/syncclipboard-stage8-macos-native-verified.json`；没有启动桌面程序。两架构 dmg 与其他平台全部发行产物仍待当前 PR 检查。
- 仓库规定的 `dotnet format --verify-no-changes --severity info --no-restore` 退出 0；日志 `/tmp/syncclipboard-stage8-format.log`。保留跨平台工作区加载警告，完整平台验证由 PR 补齐。

本步骤所有运行时 XAML、视觉、窗口/面包屑交互、真实拖拽/剪贴板/热键/通知检查均为 UI 范围排除，不执行、不计通过、不安排补测。Magick.NET 14.9.1 的既有漏洞警告保持可见，按步骤 11 处理。本步尚未通过当前提交的全平台 PR CI、产物与评审门槛，不允许进入步骤 9。

### 首轮 PR 问题

步骤 8 首次提交 `c0e51291abd900fd86b1f9c96339c5f0e1321cbb` 后，CodeFactor 报告 ServerConfigPage 两处连续空行（SA1507）。最小修复仅删除这两行空白；核对非空白 token 序列未变，仓库格式检查和 diff 检查通过。新提交仍须重新检查全平台 CI、CodeFactor、评审与产物；未忽略检查或提前进入下一步。


### 当前 PR 评审核对

修复提交 `a8d0aca4ae12a818e37d080d4b4e609ba47c6de2` 的 CodeFactor 已通过。当前 Codex 评审提出 [面包屑模板建议](https://github.com/Jeric-X/SyncClipboard/pull/419#discussion_r4000141125)，认为控件工厂会无条件套一层 FABreadcrumbBarItem。核对实际 FluentAvalonia 3.1.0 对应源码 `215ee0481e8b0e6d396cbe2a0f33dc791c5646ab`：BreadcrumbElementFactory 对模板已经返回 FABreadcrumbBarItem 的情况设置 Content 后直接返回，仅其他类型才创建容器；FABreadcrumbBar 的 repeater 使用该工厂。现有模板只返回一个面包屑容器，其 ContentTemplate 返回 TextBlock，所述嵌套路径不成立。保留已编译通过的模板，按不适用建议解决线程；未发送 GitHub 评论，也未运行 UI。详细判断依据已在本节版本适配说明及本地问题日志中保留。

当前五份 CI TRX 已全部核验：Core 356、三平台 Desktop 各 4、WinUI3 4，共 372 项通过、0 失败/跳过，四项新增导航测试均实际执行。两架构 macOS dmg 已通过严格签名、原生架构、资源及实际依赖版本检查；完整 Windows/Linux 安装包矩阵仍在收集，尚未宣布步骤 8 通过。


### 步骤 8 最终证据

验证通过提交：`a8d0aca4ae12a818e37d080d4b4e609ba47c6de2`。[PR build 34768060046](https://github.com/Jeric-X/SyncClipboard/actions/runs/34768060046)、[push build 34768058047](https://github.com/Jeric-X/SyncClipboard/actions/runs/34768058047)、[CodeQL 34768059583](https://github.com/Jeric-X/SyncClipboard/actions/runs/34768059583) 及 CodeFactor 均完成；最终 103 项成功、8 项预期发布跳过，无失败或运行中检查。再次读取当前 head、reviews/decision、行内线程及普通评论，当前 Codex 评审已完成，五个线程全部解决；面包屑建议的源码核对结论见上文。

全部 47 个 artifact 下载完成，五份 TRX 共 372 项非 UI 测试通过，0 失败/跳过。38 个 Windows/Linux 原始构建与 ZIP/Inno/AppImage/deb/rpm 组合通过完整性、资源、net10 框架、自包含模式及原生架构检查；ZIP CRC 和 Inno 每个数据文件完整性均实际核验。14 个 Linux 组合逐包核对八个新桌面依赖程序集版本，Skia/HarfBuzz 原生库与相应 RID NuGet 文件哈希一致；保留 Microsoft/EF/SQLite 的前置检查。报告 `/tmp/syncclipboard-pr419-a8d0-package-audit.json`，SHA256 `60f2f20325529027ac9111761bd9b91e4977313890f055bf73b6dc5da501eaab`。六个 Linux 包元数据检查通过。

macOS arm64/x64 两个 dmg 的主程序、每包 19 个 dylib、资源及严格签名均通过；实际八个桌面依赖程序集版本符合目标，Microsoft/EF/SQLite 及框架组件版本检查通过，两次只读挂载均已卸载。报告 `/tmp/syncclipboard-pr419-a8d0-macos-{arm64,x64}.json`。当前 PR 的 Server、amd64/arm64 容器均从日志确认四轮 Production 启动/重启及两轮 API/认证/历史/传输成功；下载的 Server 产物在本机隔离环境复验同样通过。

步骤 8 非 UI 验证通过，允许进入步骤 9a；未执行 UI 验收，不代表后续依赖升级已完成。

## 步骤 9a：Windows App SDK 与配套 BuildTools

前置步骤通过提交：`a8d0aca4ae12a818e37d080d4b4e609ba47c6de2`。状态：进行中。先核定当前稳定版本和配套要求，保留 WinUIEx、NotifyIcon、Toolkit 版本，独立完成 Windows x64/arm64 构建和 App SDK/.NET 携带矩阵及非 UI 检查后再开始步骤 9b。


### 版本与本地还原

2026-09-14 核定 [Windows App SDK 2.4.0](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.4.0) 和 [Windows SDK BuildTools 10.0.28000.2705](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.28000.2705) 为当前稳定版，中央版本分别从 1.8.260529003 和 10.0.26100.4948 升级。按 [官方 2.x 发布说明](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0) 核对新版本编号、传递包重组和生成入口修复；本项目使用 PackageReference，不适用文档针对 C++ packages.config 的卸载/重加流程。

WinUI3 与 Test.WinUI3 在本机 SDK 10.0.302 下还原成功，没有包降级、冲突或不兼容 TFM 错误；仍为 net10.0-windows10.0.19041.0，保留最低系统声明和 x86/x64/arm64 RID。实际传递依赖包括 Runtime 2.4.0、WinUI 2.3.6、Foundation 2.3.9、Base 2.0.4、InteractiveExperiences 2.1.6、WebView2 1.0.3719.77 和 MSIX BuildTools 1.7.251221100；新增 Search 2.4.4 和 MachineLearning 2.1.74 来自原有 App SDK 聚合包依赖。未额外添加功能，也未提前升级 WinUIEx、NotifyIcon 或 Toolkit。还原日志 `/tmp/syncclipboard-stage9a-winui-restore.log` 与 `/tmp/syncclipboard-stage9a-winui-test-restore.log`，完整依赖差异 `/tmp/syncclipboard-stage9a-resolved-dependency-diff.json`。

macOS 不能替代 Windows MSBuild/XAML 编译、WinUI3 非 UI 测试与 Windows 打包。本步骤最终通过依赖当前提交在真实 PR 的 Windows runner 验证全部 x64/arm64、.NET/App SDK 携带组合，并检查实际运行时包、bootstrap/程序集版本、资源与原生架构；不启动 UI，也不执行图形安装向导。


### 本地验证与待验证范围

Core 356 项、Desktop NonUI 4 项通过，0 失败/跳过；仓库 TRX 校验器按最低 360 项通过，报告 `/private/tmp/syncclipboard-stage9a-results/`。仓库 `dotnet format --verify-no-changes --severity info --no-restore` 退出 0，日志 `/tmp/syncclipboard-stage9a-format.log`，保留跨平台工作区加载警告。`git diff --check` 通过。

Windows App SDK 的 Runtime 包声明 Framework AppX 版本 2.4.0.0；所选 WinUI 组件的 Microsoft.WinUI.dll 程序集版本仍为 3.0.0.0，Bootstrap.Net 为 2.0.0.0，不能只依据程序集主版本判断升级是否生效。后续产物核验须结合 deps 中的包版本、对应 NuGet 文件哈希、Runtime/Bootstrap 及自包含模式；不运行 SDK 的 UI 初始化或安装向导。当前本机无法完成的 Windows 验证交由真实 PR，步骤 9a 未通过前不开始 9b。

### 步骤 9a 最终 PR 验证（2026-09-14）

验证通过提交：`cd59964649814866a0374271cb889a50a7cf5150`。[PR build 34769429108](https://github.com/Jeric-X/SyncClipboard/actions/runs/34769429108)、[push build 34769427181](https://github.com/Jeric-X/SyncClipboard/actions/runs/34769427181)、[CodeQL 34769428698](https://github.com/Jeric-X/SyncClipboard/actions/runs/34769428698) 及 CodeFactor 均完成，103 项成功、8 项预期发布跳过。当前提交 Codex 评审完成，无新增可处理问题，五个线程全部解决；核对全部评审、行内和普通评论后再次确认 head 和检查状态未变。

47 个 artifact 全部下载完成，五份 TRX 共 372 项非 UI 测试通过，0 失败/跳过。38 个 Windows/Linux 原始构建与 ZIP/Inno/AppImage/deb/rpm 组合通过完整性、资源、框架、自包含模式和原生架构检查。24 个 Windows 组合逐包核对 deps 中 AppSDK 2.4.0、WinUI 2.3.6、Foundation 2.3.9，WinUI 与 Bootstrap.Net 文件哈希匹配对应 NuGet；携带 App SDK 时另核对 XAML Controls 与 App Runtime 原生 DLL 哈希，不携带时确认未混入这些文件。完整报告 `/tmp/syncclipboard-pr419-cd59-package-audit.json`，SHA256 `361981fb8de9c38f6e9da757979d1194ea9ea092fbd675f35453604377632adf`。

14 个 Linux 组合保留前置 Avalonia/Skia/HarfBuzz、Microsoft/EF/SQLite 版本与原生哈希检查，六个 Linux 包元数据通过。macOS 两架构 dmg 的资源、每包 19 个 dylib、依赖版本和严格签名通过，只读挂载均已卸载。当前 Server 与两架构容器 CI 各确认四轮 Production 启动/重启及两轮 API/认证/历史/传输检查，下载的服务器产物本机复验同样通过。报告前缀 `/tmp/syncclipboard-pr419-cd59-`，本地详细记录 `docs/ai_design/.local/PR-419-Artifacts-cd599646.md`。

步骤 9a 非 UI 验证通过，允许进入 9b；没有启动桌面程序或图形安装向导，不代表后续升级已完成。

## 步骤 9b：WinUIEx（2026-09-14）

前置步骤通过提交：`cd59964649814866a0374271cb889a50a7cf5150`。状态：进行中；当前步骤通过真实 PR 的 Windows 编译、非 UI 测试、打包与评审前，不开始 9c。

依据 [NuGet 2.9.3 依赖声明](https://www.nuget.org/packages/WinUIEx/2.9.3) 和[上游发布说明](https://github.com/dotMorten/WinUIEx/releases/tag/v2.9.3)，将 WinUIEx 2.3.4 升级为当前稳定版 2.9.3。新包目标 net8.0-windows10.0.19041，要求 Microsoft.WindowsAppSDK.WinUI >= 1.8.250906003；本项目 net10.0-windows10.0.19041.0 与步骤 9a 的 WinUI 2.3.6 满足要求。实际包源码提交为 `72f2975d2a237c0d7ad1113fe617d5894e66feb6`。

只修改中央版本文件中的 WinUIEx；WinUI3 与测试项目还原均退出 0，实际解析差异均只有 WinUIEx 2.3.4 → 2.9.3，没有包降级或 TFM 冲突。依赖差异报告 `/tmp/syncclipboard-stage9b-resolved-dependency-diff.json`，日志 `/tmp/syncclipboard-stage9b-winui{-test,}-restore.log`。WindowManager.Get、MinWidth/MinHeight、窗口句柄/显示/置顶/前台扩展、WindowMessageMonitor 及其事件相关的 18 个旧包文档成员在新包中均存在；报告 `/tmp/syncclipboard-stage9b-api-comparison.json`。这仅为 API 静态核对，完整 C#/XAML 编译由当前 PR 的 Windows runner 验证，不据此宣称运行时窗口行为通过。

本地 Core 356、Desktop NonUI 4 项通过，0 失败/跳过，仓库 TRX 校验器按最低 360 项通过；报告 `/private/tmp/syncclipboard-stage9b-results/`。仓库格式检查退出 0，仅保留跨平台工作区加载警告，日志 `/tmp/syncclipboard-stage9b-format.log`；diff 检查通过。没有新增模拟窗口测试，真实窗口、消息循环、热键与托盘行为均为 UI 范围排除。

新 WinUIEx.dll 的程序集版本为 2.9.3.0，ProductVersion 为 `2.9.3+72f2975d2a237c0d7ad1113fe617d5894e66feb6`，包内 SHA256 为 `d4d66f42b613d50ad6756e10f18600210f5859a1cc74215972e244cc88c9a5d2`。当前 PR 产物检查须逐个 Windows 组合核对 deps 与实际 DLL，保留步骤 9a 的 App SDK 校验；不能把本机 NuGet 文件当作 CI 产物已经通过。

### 步骤 9b 最终 PR 验证（2026-09-14）

验证通过提交：`57f7083e99f6695f0e4d2e99ffad93ebb2828608`。[PR build 34770612292](https://github.com/Jeric-X/SyncClipboard/actions/runs/34770612292)、[push build 34770608924](https://github.com/Jeric-X/SyncClipboard/actions/runs/34770608924)、[CodeQL 34770612038](https://github.com/Jeric-X/SyncClipboard/actions/runs/34770612038) 及 CodeFactor 均完成，103 项成功、8 项预期发布跳过。当前提交 Codex 评审完成，五个线程全部解决，无新增可处理行内或普通评论问题。

全部 47 个 artifact 下载完成，五份真实 TRX 共 372 项非 UI 测试通过，0 失败/跳过。完整 38 个 Windows/Linux 包组合通过；24 个 Windows 组合逐包核对 WinUIEx 2.9.3 的 deps 与实际 DLL 哈希，并保留 App SDK 2.4.0 与前置依赖的检查。完整报告 `/tmp/syncclipboard-pr419-57f7-package-audit.json`，SHA256 `6d7528064aeaf360beb61b093c5832e45446a6709112914c7b358ca9de1c3a7b`。

六个 Linux 包元数据与两架构 macOS dmg 的资源、签名、原生架构和依赖版本检查通过；每个 macOS 包检查 19 个 dylib，挂载已卸载。Server 与 amd64/arm64 容器 CI 均核验四轮启动与两轮 API 冒烟，下载的服务器产物本机复验同样通过。报告前缀 `/tmp/syncclipboard-pr419-57f7-`；本地详细记录 `docs/ai_design/.local/PR-419-Artifacts-57f7083e.md`。步骤 9b 非 UI 验证通过，允许进入 9c，不代表后续升级已完成。

## 步骤 9c：H.NotifyIcon（2026-09-14）

前置步骤通过提交：`57f7083e99f6695f0e4d2e99ffad93ebb2828608`。状态：进行中，当前提交的 PR 构建、非 UI 测试、产物与评审通过前，不开始 9d。

依据 [NuGet 稳定版本与依赖声明](https://www.nuget.org/packages/H.NotifyIcon.WinUI/2.4.1)，将 H.NotifyIcon.WinUI 2.3.0 升级为 2.4.1，未选择 2.5 的预发布版本。新包目标 net10.0-windows10.0.17763，要求 AppSDK >= 1.6.250108002，与本项目 net10.0-windows10.0.19041.0/AppSDK 2.4.0 相容。两个 Windows 项目还原退出 0；实际配套变化为 H.NotifyIcon、H.GeneratedIcons.System.Drawing 2.3.0 → 2.4.1，以及 System.Drawing.Common、Microsoft.Win32.SystemEvents 9.0.1 → 10.0.0，其余包未变。报告 `/tmp/syncclipboard-stage9c-resolved-dependency-diff.json`。

项目保留 SecondWindow 菜单、图标资源、双击/左击命令和效率模式回退语义。新包中相关 20 个旧版公开 API 文档成员仍存在；另外用 PEReader 直接读取新旧 DLL 元数据，确认 ContextMenuFlyout、IsContextMenuVisible、ContextMenuWindowHandle 三个反射属性仍具备私有实例 getter/setter，没有加载或初始化 UI 程序集。所选包源码提交 `bcaaabecf16566dd910a3798fe874014e251b28d` 中，这三个属性类型仍为 MenuFlyout?、bool、nint?。报告 `/tmp/syncclipboard-stage9c-api-comparison.json`、`/tmp/syncclipboard-stage9c-private-metadata.json`；[精确版本的 SecondWindow 源码](https://github.com/HavenDV/H.NotifyIcon/blob/bcaaabecf16566dd910a3798fe874014e251b28d/src/libs/H.NotifyIcon.Shared/TaskbarIcon.ContextMenu.WinUI.SecondWindow.cs)。完整 C#/XAML 编译仍由当前 PR 的 Windows runner 验证。

已核对[上游问题 #271](https://github.com/HavenDV/H.NotifyIcon/issues/271)：报告者在 H.NotifyIcon 2.4.1/AppSDK 2.3.1 的 packaged WinUI 应用中遇到 SetBorderAndTitleBar 异常。本项目为 unpackaged/AppSDK 2.4.0，不能据此判定问题不适用或已修复。所选 2.4.1 与旧 2.3.0 的 SecondWindow 实现均含该调用，当前源码差异仅新增裁剪保留标注及其命名空间；没有证据支持改写产品菜单行为。该运行时 UI 问题记录为范围排除、不计通过、不安排解锁或人工补测，最终结果不声称托盘 UI 无问题。

本地 Core 356、Desktop NonUI 4 项通过，0 失败/跳过，仓库 TRX 校验器按最低 360 项通过；报告 `/private/tmp/syncclipboard-stage9c-results/`。格式检查退出 0，仅保留跨平台工作区加载警告，日志 `/tmp/syncclipboard-stage9c-format.log`；diff 检查通过。既有 Magick.NET 漏洞仍由步骤 11 处理，不屏蔽警告。

已记录三项 H.* 程序集 2.4.1 及四项配套 System.Drawing/SystemEvents 程序集 10.0.0 的实际 ProductVersion 与 NuGet 哈希，报告 `/tmp/syncclipboard-stage9c-package-assemblies.json`。当前 PR 产物须逐个 Windows 包核对这七个 DLL、五项依赖版本和托盘图标资源，并保留前置 WinUIEx/AppSDK 及其他平台检查；本机包元数据不能替代 PR 产物证据。

### 步骤 9c 最终 PR 验证（2026-09-14）

验证通过提交：`83020ce6994a1a8bc537fe8f9063c256bebe646a`。[PR build 34771724806](https://github.com/Jeric-X/SyncClipboard/actions/runs/34771724806)、[push build 34771722518](https://github.com/Jeric-X/SyncClipboard/actions/runs/34771722518)、[CodeQL 34771724519](https://github.com/Jeric-X/SyncClipboard/actions/runs/34771724519) 及 CodeFactor 全部完成，103 项成功、8 项预期发布跳过。当前提交 Codex 评审完成，全部五个线程解决，无新增可处理行内或普通评论问题。

47 个 artifact 全部下载，五份 TRX 共 372 项非 UI 测试通过，0 失败/跳过。完整 38 个 Windows/Linux 包组合通过；24 个 Windows 组合逐包核对七项 NotifyIcon/GeneratedIcons/Drawing/SystemEvents DLL 与对应 NuGet 哈希、五项依赖版本、38 个托盘图标资源，并保留 WinUIEx、AppSDK 及前置依赖检查。报告 `/tmp/syncclipboard-pr419-8302-package-audit.json`，SHA256 `384d98b19e0e21ff8c6d4df13634f6560bbb50b41681dedcff86a2f3f0cac737`。

六个 Linux 包元数据、两架构 macOS dmg 的签名/资源/原生架构及依赖版本均通过；每包检查 19 个 dylib，挂载已卸载。Server 与 amd64/arm64 容器 CI 均执行四轮启动及两轮 API 冒烟，下载的 Server 产物本机复验通过。报告前缀 `/tmp/syncclipboard-pr419-8302-`，本地记录 `docs/ai_design/.local/PR-419-Artifacts-83020ce6.md`。步骤 9c 非 UI 验证通过，允许进入 9d；上游 #271 的运行时 UI 限制继续保留，不宣称其已修复或验证。

## 步骤 9d：WinUI CommunityToolkit（2026-09-14）

前置步骤通过提交：`83020ce6994a1a8bc537fe8f9063c256bebe646a`。状态：进行中；当前 PR 编译、非 UI 测试、产物与评审通过前，不开始步骤 10。

将 SettingsControls 与 Converters 8.2.250402 升级为稳定版 8.2.251219，未选择 8.3 预发布版。依据为[官方发布说明](https://github.com/CommunityToolkit/Windows/releases/tag/v8.2.251219)、[SettingsControls 依赖](https://www.nuget.org/packages/CommunityToolkit.WinUI.Controls.SettingsControls/8.2.251219)及[Converters 依赖](https://www.nuget.org/packages/CommunityToolkit.WinUI.Converters/8.2.251219)。两个 Windows 项目还原成功，实际选择 net9.0-windows10.0.19041 资产，要求 AppSDK >= 1.6.250108002，由当前 .NET 10/AppSDK 2.4.0 满足，没有引入 Uno 资产或依赖。

实际解析差异仅为 SettingsControls、Converters、Extensions、Helpers、Triggers 五包统一到 8.2.251219；CommunityToolkit.Common 8.2.1、MVVM 与其他依赖未变。报告 `/tmp/syncclipboard-stage9d-resolved-dependency-diff.json`；五个实际 DLL 的 ProductVersion 均为 8.2.251219，包内版本/哈希报告 `/tmp/syncclipboard-stage9d-package-assemblies.json`。当前 PR 产物必须逐个 Windows 组合核对这五项依赖和 DLL，保留前置 NotifyIcon 图标/依赖检查及全部其他平台静态检查。

项目实际使用 SettingsCard、SettingsExpander、DispatcherQueue 扩展，以及 BoolNegationConverter 和 BoolToVisibilityConverter。所选源码提交为 `a6b4dc451c0e54dd29f58743894a956100e7f713`；[BoolNegationConverter](https://github.com/CommunityToolkit/Windows/blob/a6b4dc451c0e54dd29f58743894a956100e7f713/components/Converters/src/BoolNegationConverter.cs) 是普通托管类，直接实现 IValueConverter，不创建 UI 对象或使用 UI 调度器。新增 ToolkitConverterTests 的两个 NonUI 数据用例，验证 true/false 在 Convert 和 ConvertBack 中的取反行为，覆盖热键编辑器实际使用的转换器；没有构造窗口或控件。

BoolToVisibilityConverter 继承 BoolToObjectConverter，后者继承 DependencyObject 并注册 DependencyProperty，因此其运行时测试属于 UI 范围排除。SettingsCard/SettingsExpander 的实例化、主题、绑定显示、点击及真实 DispatcherQueue 调度也排除，只保留 C#/XAML 编译和资源/API 静态核对；不为验证这些内容初始化 UI。

本地 Core 356、Desktop NonUI 4 项通过，0 失败/跳过，报告 `/private/tmp/syncclipboard-stage9d-results/`；仓库格式检查退出 0，仅保留跨平台工作区加载警告，日志 `/tmp/syncclipboard-stage9d-format.log`。工作流 YAML 语法与 diff 检查通过。新增 Windows 转换器用例尚未在本机执行，必须由 PR 的 Windows runner 验证；WinUI NonUI 最低通过数由 4 提高到 6，五份最终 CI TRX 预计合计 374 项，不能将本地 360 项当作新增 Windows 测试已通过。

### 当前提交的测试与服务器证据

提交 `b00ddd0d95cc2e501225df5e7e5948a5d155846f` 的 [PR run 34772867993](https://github.com/Jeric-X/SyncClipboard/actions/runs/34772867993) 已生成五份 TRX，实际合计 374 项通过，0 失败/跳过。WinUI 的 6 项包括 `BoolNegation_ConvertsBothDirections (True,False)` 和 `(False,True)`，均实际执行通过；其余为 Core 356 与三个平台 Desktop 各 4 项。仓库 TRX 校验器按最低 374 项复核通过，报告目录 `/tmp/syncclipboard-pr419-b00d-artifacts/`。

Server 与 amd64/arm64 容器 CI 的实际日志分别包含四轮 Production 启动检查及两轮 API/认证/历史/传输冒烟，下载服务器在本机复验同样通过。两架构 macOS dmg 的签名、资源、依赖及每包 19 个 dylib 架构检查通过，挂载均已卸载。相关报告前缀 `/tmp/syncclipboard-pr419-b00d-`。这些证据不代替尚未完成的 Windows/Linux 完整产物矩阵与最终 PR 检查；步骤 9d 此时仍未通过。

### 最终 PR 与产物验证

上述提交的 PR run、[push run 34772866278](https://github.com/Jeric-X/SyncClipboard/actions/runs/34772866278) 和 [CodeQL 34772867838](https://github.com/Jeric-X/SyncClipboard/actions/runs/34772867838) 均成功完成。最终 103 SUCCESS、8 项预期发布 SKIPPED，无失败或待执行检查。当前提交 Codex 评审于 `2026-09-13T17:55:27.982282Z` 完成，五个历史线程均解决，行内及普通评论无新增可处理问题。

全部 47 个 artifact 已下载并与当前 PR run 清单逐项核对。完整 38 个 Windows/Linux 包组合通过静态审计，其中 24 个 Windows 组合逐包核对五项 Toolkit 依赖及 DLL 的 NuGet 哈希，并保留七项 NotifyIcon 配套 DLL、38 个托盘资源、WinUIEx/AppSDK 及其他前置依赖检查；六个 Linux 包元数据也通过。完整报告 `/tmp/syncclipboard-pr419-b00d-package-audit.json`，SHA256 `a6b9c8b6861877d8b04f31ff2627131b8c9604d6ee950997698a2bc6f4078b91`；本地详情 `docs/ai_design/.local/PR-419-Artifacts-b00ddd0d.md`。

结合前述 374 项测试、双架构 macOS 和 Server/容器证据，步骤 9d 非 UI 验证通过，允许进入步骤 10。没有执行 UI 验证，不表示后续步骤已完成。

## 步骤 10 前置：S3 HTTP 端点与真实协议基线（2026-09-14）

前置步骤通过提交：`b00ddd0d95cc2e501225df5e7e5948a5d155846f`。当前仍使用 AWSSDK.S3 3.7.414，尚未升级 v4；已从 NuGet 索引核定候选稳定版 4.0.103.2，包提供 net8.0 资产并要求 AWSSDK.Core `[4.0.102.4, 5.0.0)`。依据 [AWS V4 迁移说明](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/net-dg-v4.html)，后续需处理可空值、默认 null 集合及重试模式变化，不启用全局 InitializeCollections 绕过适配。

官方 MinIO 旧二进制下载地址返回 410，因此按[官方发布说明](https://github.com/minio/minio/releases/tag/RELEASE.2025-10-15T17-29-55Z)从固定源码构建：`go install github.com/minio/minio@RELEASE.2025-10-15T17-29-55Z`，实际 Go 模块 `v0.0.0-20251015172955-9e49d5e7a648`，构建信息 `/tmp/syncclipboard-stage10-minio-buildinfo.txt`。测试服务仅监听随机 loopback 端口，关闭浏览器控制台，使用随机凭据和临时数据目录；不连接现有桶或用户账号。

原始 v3 探针冻结在 `/tmp/syncclipboard-stage10-s3-v3-probe/`，对应二进制哈希 `/tmp/syncclipboard-stage10-v3-baseline-hashes.json`。真实 MinIO 测试的连接、空列表与初始化通过，但写入元数据时抛出 `When DisablePayloadSigning is true, the request must be sent over HTTPS`，日志 `/tmp/syncclipboard-stage10-v3-s3-smoke.log`。该基线失败不能计作通过。根因是所有自定义端点均设置 DisablePayloadSigning=true；[AWS 请求属性说明](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TPutObjectRequest.html)明确限制该选项只能用于 HTTPS。

最小修复仅在 HTTPS 自定义端点关闭 payload 签名，HTTP 保留签名；继续禁用自定义 PUT 的 chunk 编码，初始化目录标记也应用同一配置。这里修正了原 S3 协议说明中对“禁用 Payload Signing”的笼统描述：该行为只适用于 HTTPS。保留端点、区域、路径风格、凭据和清理配置语义，未改 SDK 包版本。

新增 `build/verification/S3Probe/` 调用实际 S3Adapter，配合 `s3_smoke.py` 启动真实 MinIO 和只转发到该实例的故障代理。涵盖空内容、Unicode 元数据与 key、条件 ETag 更新、32 MiB/多文件/传输哈希、取消、两次 500 后成功重试、错误凭据、配置重建、超过 1000 个对象分页和前缀隔离。每次退出均清理隔离桶及 MinIO 进程；测试不构造 UI。新增 CI `s3-test` 使用固定 Go/MinIO 版本并上传实际 JSON 结果，必须经当前 PR 执行通过。

初次修复后七组检查通过，报告 `/tmp/syncclipboard-stage10-v3-httpfix-s3-result2.json`：上传/下载各在注入两次 500 后第 3 次成功；持续 500 在第 4 次后由 20 秒显式取消终止，SDK 配置最多 5 次请求，不能称作已耗尽全部自动重试。上传签名前的流读取可能早于网络发送，因此上传取消只证明源流读取期间响应取消；下载取消发生在已读到响应数据后。后续探针另核对进度不超过文件长度和取消时限，最终证据尚待补齐。

Core 356、Desktop NonUI 4 项最终通过，0 失败/跳过，报告 `/private/tmp/syncclipboard-stage10-httpfix-results/`；首次并发构建 Desktop 遇到共用 Core obj 文件锁，待 Core 构建结束后串行重跑成功，没有改动产品或降低检查。仓库格式检查退出 0（仅跨平台工作区加载警告），YAML 语法和 diff 检查通过。此时前置修复仍未通过 PR，不能开始 v4 迁移或步骤 11。

补充的进度边界测试发现：SDK 为签名回退流并重新读取时，ProgressReadStream 累计同一批字节，导致上传进度超过文件长度。报告 `/tmp/syncclipboard-stage10-v3-httpfix-s3-smoke3.log`，该次失败不能计作通过。修复为可定位流按当前位置报告，不能定位的流仍累计字节数；探针同时检查普通上传和重试上传的进度上限，并检查取消覆盖上传后原有完整对象保持不变。此修复与 HTTP 签名兼容修复一起作为步骤 10 的前置提交，独立通过后才升级 SDK。

两项修复后的七组完整协议检查通过，报告 `/tmp/syncclipboard-stage10-v3-httpfix-s3-result4.json`。普通/重试上传进度没有超过文件长度，取消覆盖上传后原对象仍完整；注入错误后的上传/下载各 3 次请求后成功，持续错误在 4 次请求后响应显式取消且未超过 25 秒验证上限。Core 356、Desktop NonUI 4 项再次通过，0 失败/跳过，TRX `/private/tmp/syncclipboard-stage10-httpfix-final-results/`。最终探针增加异常捕获与错误退出码，非法参数验证退出 1；完整流程失败时仍不能生成通过报告。当前 SDK 的程序集版本是 3.3.0.0，但实际包为 3.7.414，报告同时保存 DLL 哈希，不能混淆程序集与 NuGet 版本。

新增 CI 任务会使 PR 另有一份 `s3-test-results` 产物；原五份 TRX 的最低总数仍为 374，S3 七组协议检查独立核验，不能混入 MSTest 计数。前置修复当前等待真实 PR 构建、MinIO 检查、产物与评审，不开始 v4 升级。

### 首次 PR 反馈与修复

前置提交 `05bb84cc4ee7627629c4a05768445a973f25db1c` 的 [PR run 34774908991](https://github.com/Jeric-X/SyncClipboard/actions/runs/34774908991) 在启动前失败，确切诊断是 core-test.yml 第 81 行的 `Unrecognized named-value: runner`。job 环境变量不能使用 runner 上下文；已将 GOBIN 移到安装步骤环境变量，并在运行步骤使用 runner.temp 路径。普通 YAML 语法检查无法发现这一错误，补用官方 actionlint v1.7.12 进行 GitHub 表达式语义检查，修复后退出 0。

CodeFactor 同时报告 C# 探针复杂度 42、Python 驱动复杂度 21。已将 C# 七组验证提取为具名类方法，并拆分 Python 代理处理、服务等待/清理与报告核验；C# Require 条件及 Python require AST 比对确认断言未减少。编译和格式检查通过，重构后的真实 MinIO 七组检查再次通过，报告 `/tmp/syncclipboard-stage10-refactor-s3-result.json`，与原通过报告的检查清单一致，上传/下载故障重试均第 3 次成功。当前提交 Codex 评审于 `2026-09-13T18:36:51.631944Z` 完成且未新增反馈，但工作流与 CodeFactor 仍须修复提交的真实 PR 重新验证，前置步骤尚未通过。

### 前置修复最终通过

修复提交 `93e68023caca43c44fe763486253af45d2afd165` 的 [PR run 34775465345](https://github.com/Jeric-X/SyncClipboard/actions/runs/34775465345)、[push run 34775464080](https://github.com/Jeric-X/SyncClipboard/actions/runs/34775464080) 和 [CodeQL 34775465033](https://github.com/Jeric-X/SyncClipboard/actions/runs/34775465033) 全部成功。最终 105 SUCCESS、8 项预期发布 SKIPPED，无失败或待执行检查，CodeFactor 已恢复成功。当前提交 Codex 评审于 `2026-09-13T18:46:08.335173Z` 完成，五个历史线程均解决，无新增评审或评论问题。

五份真实 TRX 共 374 项通过，0 失败/跳过；两条流水线的 MinIO 检查均实际成功，独立 S3 JSON 中的七组检查及 AWS DLL 哈希与本地一致。48 个 artifact 全部下载，完整 38 个 Windows/Linux 包组合、六 Linux 包元数据、双架构 macOS 签名/依赖/各 19 个 dylib 检查通过；Server 与双架构容器各四轮启动和两轮 API 冒烟成功，下载服务器本机复验同样通过。完整产物报告 `/tmp/syncclipboard-pr419-93e6-package-audit.json`，SHA256 `3fb0eeddef74ec1803e223df12f6ab489b37d750f7257190cbed8c9a3fd9d69b`，本地详情 `docs/ai_design/.local/PR-419-Artifacts-93e68023.md`。

步骤 10 前置修复非 UI 验证通过，允许迁移 SDK v4；并不表示步骤 10 主升级或 11–18 已完成。


## 步骤 10：AWS SDK S3 v4（2026-09-14）

前置修复通过提交 `93e68023caca43c44fe763486253af45d2afd165`。将 AWSSDK.S3 3.7.414 升级至稳定版 4.0.103.2，实际解析 AWSSDK.Core 3.7.401.10 → 4.0.102.4；[NuGet 版本与依赖](https://www.nuget.org/packages/AWSSDK.S3/4.0.103.2)。Core、WinUI3、macOS 依赖图差异均只有这两个包，选择 net8.0 资产，没有其他传递依赖变化。报告 `/tmp/syncclipboard-stage10-v4-resolved-dependency-diff.json`。

根据 [AWS v4 迁移指南](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/net-dg-v4.html)，将分页标记改为 `IsTruncated == true`，并在清理前检查 S3Objects 是否为 null/空列表；正常批量删除和兼容回退共用检查后的集合。未打开全局集合初始化开关。原有地址、Region、ForcePathStyle、代理、WHEN_REQUIRED 校验和设置、流生命周期与前置 HTTP 签名修复继续保留。

SDK 默认重试模式改为 Standard；实测上限为 3 次总请求，原版为 5 次。连续两次 HTTP 500 后，上传/下载均第 3 次成功且字节哈希正确；持续错误在第 3 次请求后自动耗尽，`persistentFailureCanceled=false`，没有无限重试。v4 不再允许 us-east-1 客户端跨区域访问 AWS 桶，迁移已有 AWS 账号时 Region 应填写桶实际区域；本项目保持显式 Region 配置与空值默认 us-east-1。这里没有实际访问 AWS 云账号，也没有将 MinIO 结果写成所有云厂商已验证。

同一套真实 MinIO 七组协议检查全部通过：空列表和目录标记、Unicode 元数据/ETag 冲突、空文件/32 MiB/多文件/哈希与完成进度、取消、故障代理重试、错误凭据与配置重建、1005+ 分页和重复清理/前缀隔离。报告 `/tmp/syncclipboard-stage10-v4-s3-result.json`，日志 `/tmp/syncclipboard-stage10-v4-s3-smoke.log`；对照修复后的 v3 报告，七组检查名称完全相同。本轮使用本机 HTTP MinIO；ForcePathStyle=false 搭配 IP 端点不等同于验证真实 DNS 虚拟主机寻址，HTTPS 云端和各云厂商差异不能由此推断。

Core 356、Desktop NonUI 4 项通过，0 失败/跳过，仓库 TRX 校验器按最低 360 项复核通过；报告 `/private/tmp/syncclipboard-stage10-v4-results/`。WinUI3、WinUI3 测试及 macOS 还原成功，仓库格式检查退出 0，仅保留跨平台工作区加载警告；日志 `/tmp/syncclipboard-stage10-v4-format.log`。Windows 和其他架构的实际编译、374 项 CI MSTest、七组 CI S3 协议检查与完整产物矩阵仍须在当前提交的真实 PR 验证，不开始步骤 11。

产物检查新增逐包核对 AWSSDK.S3/Core 的依赖版本和 net8.0 NuGet DLL 哈希：S3 为 `42776c7c8d2590279a5057894e56cf8d75475c29645017125f29eec88662a736`，Core 为 `222fced75324387f6b77acc00392a957aa916c3a3e87e6ac7c6b1814911c5232`。Windows/Linux 保留所有前置包检查，macOS 保留签名、资源、原生架构和前置依赖检查；不启动桌面应用。


### PR 评审修复：源流回退时保持进度单调

提交 `e7ed265d5d7f04332768e4939a7437e910613509` 的五份 TRX 共 374 项通过，七组 CI S3 检查与本地结果一致；双架构 macOS 和 Server/容器检查通过。但 [Codex 评审](https://github.com/Jeric-X/SyncClipboard/pull/419#discussion_r4000597648) 指出，直接使用流当前位置会在签名或重试回退后报告较小的进度，当前步骤因此未通过，不能开始步骤 11。

在已有真实 MinIO 探针中新增连续进度不得下降的断言，旧实现于普通 2 MiB 文件上传即失败：`Upload progress decreased during signing or retry`，还未进入故障代理重试组。日志 `/tmp/syncclipboard-stage10-v4-regression-smoke.log`。修复为报告已读取位置的历史最大值，并以文件总长封顶；非可定位流保持累计读取语义。没有修改 UI 或以 UI 运行检查替代数据断言。

修复后的七组完整 MinIO 检查通过，普通、空文件、大文件及重试传输同时满足进度单调、进度不超长、完成字节数和传输哈希要求；报告 `/tmp/syncclipboard-stage10-v4-monotonic-result.json`。Core 356、Desktop NonUI 4 项及格式检查再次通过，0 失败/跳过，报告 `/private/tmp/syncclipboard-stage10-v4-monotonic-results/`。本修复仍须新提交的 PR CI、完整产物与评审通过；先前 e7ed265d 的检查不能替代它。


### 步骤 10 最终 PR 与产物验证

验证通过提交 `570614d86594402310d80495edafe037b11edab4`：[PR run 34777597992](https://github.com/Jeric-X/SyncClipboard/actions/runs/34777597992)、[push run 34777596462](https://github.com/Jeric-X/SyncClipboard/actions/runs/34777596462)、[CodeQL 34777597644](https://github.com/Jeric-X/SyncClipboard/actions/runs/34777597644) 与 CodeFactor 均通过。最终 105 项成功、8 项预期发布跳过；当前提交 Codex 于 `2026-09-13T19:31:05.843724Z` 完成，无新增反馈。进度问题线程依据真实复现和修复后的 S3 CI 证据标记解决，全部六个线程均已解决；最终重新核对 head、评审、行内与普通评论无变化，没有发送 GitHub 评论。

48 个 artifact 全部下载并与当前 run 清单核对。五份 TRX 合计 374 项通过，0 失败/跳过；S3 七组独立 CI 检查通过，实际 DLL 哈希、重试结果与本地修复报告一致。完整 38 个 Windows/Linux 包组合通过（24 个 Windows、14 个 Linux），每个组合均核对 S3/Core 的 NuGet 版本及 DLL 字节哈希，并保留全部前置包检查。报告 `/tmp/syncclipboard-pr419-5706-package-audit.json`，SHA256 `816366e31b90be9c83ae370a8b85a38b7168b1a56463e82d83d14b520ea4d2a0`。

六个 Linux 包元数据和双架构 macOS 的签名、资源、依赖、各 19 个 dylib 架构与 AWS DLL 哈希均通过，挂载已卸载。Server 与 amd64/arm64 容器各四轮 Production 启动和两轮 API 冒烟成功；下载服务器产物本机复验同样通过。报告前缀 `/tmp/syncclipboard-pr419-5706-`；详细本地记录 `docs/ai_design/.local/PR-419-Artifacts-570614d8.md`。步骤 10 非 UI 验证通过，允许开始步骤 11；不代表后续升级已完成。监控 `pr419` 已按计划第 4 节在阶段通过后删除，下一阶段提交后再按需恢复。


## 步骤 11：图片处理依赖准备（2026-09-14）

前置步骤 10 已完整通过。尚未修改图片依赖版本，先冻结当前 Core、Test、WinUI3、macOS 依赖图到 `/private/tmp/syncclipboard-stage11-baseline-assets/`，准备旧版/新版一致的非 UI 图片数据回归。

NuGet 实时索引核定 Q16 各包候选 14.17.1、SystemDrawing 8.0.27；搜索缓存曾返回 8.0.25，未采用该过时结果。已下载并读取 8.0.27 nuspec：要求 Magick.NET.Core >= 14.17.1、System.Drawing.Common >= 8.0.29，源码提交 `298053457c16f413a9df6edaca668b12289bea2d`；[14.17.1 发布说明](https://github.com/dlemstra/Magick.NET/releases/tag/14.17.1)。实际还原后的依赖版本及漏洞审计仍待执行，不将候选核对视为升级通过。

本步需为 ClipboardImage 的保存/缓存、ImageHelper 的单帧/多帧兼容转换及图片尺寸、像素、透明通道、大图和无效输入建立非 UI 检查。原生库须覆盖 Windows x86/x64/arm64、Linux x64/arm64、macOS x64/arm64；x86 包继续保留时加入相应 Windows 构建及实际原生库执行，不能由既有 x64/arm64 包静态检查代替。官方 runner 清单已核对 macos-26-intel 和 windows-11-vs2026-arm 等原生架构标签；具体工作流必须由真实 PR 运行验证。没有启动图片预览或真实剪贴板。


### 步骤 11 实现与本地非 UI 验证

Q16 x64/x86/arm64/AnyCPU 统一由 14.9.1 升至 14.17.1，SystemDrawing 由 8.0.15 升至 8.0.27。实际 Magick.NET.Core 在 Core/Test/macOS 中由 14.9.1、在 WinUI3 中由 14.10.2 统一为 14.17.1；依赖图差异报告 `/tmp/syncclipboard-stage11-dependency-diff.json` 未发现其他产品传递依赖变化。Core 与 WinUI3 的显式 NuGet 漏洞查询均成功，所还原依赖图无当前已报告漏洞；这不是未知漏洞不存在的保证。

新增 ImageProbe 直接调用产品 ClipboardImage 与 ImageHelper，验证 PNG/TIFF 的 RGBA 像素及透明通道、BMP 解码、文件/字节缓存与八个并发实例隔离、WebP/AVIF/HEIC/HEIF 到 JPEG、动画 WebP 到 GIF 的帧/尺寸/时间/颜色、4096×2048 大图的全部 32 MiB RGBA 数据，以及无效输入和取消后恢复。HEIC 使用仓库自生成的固定蓝色样本，SHA256 `c5b79609d8db76828d07fb803a5af8948e73c5cacef3dc597489d6db4528ab95`；产品所需的是解码，不依赖 NuGet 包没有提供的 HEIC 编码器。Windows 另检查 SystemDrawing 的纯 Bitmap 往返转换，不创建窗口、控件或真实剪贴板。

Windows 探针直接引用 System.Drawing.Common 10.0.0，中央版本表相应补充该版本；这是对齐 WinUI3 已解析的 10.0.0，不是再次升级产品依赖。若只引用 Magick.NET.SystemDrawing，孤立探针会选择 8.0.29，从而无法验证实际产品组合。探针的 Directory.Packages.props 转发到 src 中的中央版本文件，不复制版本。

本机 macOS arm64 的旧版和新版六组图片检查均通过。新版报告 `/tmp/syncclipboard-stage11-new-image-result-v2.json` 记录实际 ImageMagick 7.1.2-31、Magick.NET 14.17.1 和托管/Core/原生库哈希；原生 SHA256 `9b2fe24ad0f589459e34d4f2f2813f68e84274caf94b5d0ceab372328d60f263`。Windows x86 探针交叉发布成功且无警告，但未在 macOS 上运行，不能作为 Windows 运行证据。

Core 356、Desktop NonUI 4 项通过，0 失败/跳过，TRX 位于 `/private/tmp/syncclipboard-stage11-results/`；仓库与探针格式检查均退出 0，仓库仅保留跨平台工作区加载警告。Windows/macOS 产品还原、actionlint 和 diff 检查通过。

CI 新增七个实际 RID 图片任务：Windows x86/x64/arm64、Linux x64/arm64、macOS x64/arm64。各任务检查进程架构、运行产品图片路径并上传含库哈希的 JSON；Windows x86 同时编译保留的 WinUI3 产品。Windows arm64 使用原生 windows-11-arm runner。既有构建、测试和打包矩阵保留。预计新增七份报告，完整 PR run 共 55 个 artifact；图片共 45 组检查，另有既有 374 项 MSTest 与七组 S3 检查。以上 CI、跨平台原生执行、最终产物及评审均待本次提交实际通过，不能据本地结果开始步骤 12。


### 步骤 11 首轮 PR 反馈修复

提交 `709d928774ff502962ee161a5a27c5cb93d7bb5f` 触发真实七架构 CI。CodeFactor 报告 ImageProbe 样本公式六处 SA1407：乘除和加法缺少显式优先级括号。仅补充括号，像素公式、检查内容与数量不变；修复后探针格式检查和 macOS arm64 六组实际图片回归通过。首次格式重跑因沙箱禁止 MSBuild 命名管道而未执行成功，获得沙箱外执行权限后通过，没有关闭诊断。报告 `/tmp/syncclipboard-stage11-codefactor-image-result.json`。新修复提交仍须重新通过 PR 检查，首轮结果不代替最终提交。


### 步骤 11 最终 PR 与产物验证

验证通过提交 `086599a976b86d7e914f77ce1a1606aec3aa039f`：[PR run 34781102768](https://github.com/Jeric-X/SyncClipboard/actions/runs/34781102768)、[push run 34781100888](https://github.com/Jeric-X/SyncClipboard/actions/runs/34781100888)、[CodeQL 34781102480](https://github.com/Jeric-X/SyncClipboard/actions/runs/34781102480) 与 CodeFactor 均成功，最终 119 项 SUCCESS、8 项预期发布 SKIPPED。Codex 于 `2026-09-13T20:37:35.655958Z` 完成本 head 评审，无新增可处理反馈；最终复核六个线程均已解决，评审、行内及普通评论无新增问题。没有发送 GitHub 评论。

五份 TRX 实际合计 374 项通过，0 失败/跳过。七个 RID 的图片任务全部成功，合计 45 组，执行架构、Magick/Core/原生库和 Windows SystemDrawing/Drawing.Common 哈希均与对应官方 NuGet 包一致；Windows x86 产品编译步骤确实运行成功。S3 七组协议检查、SDK 哈希和三次有界重试结果继续通过。

全部 55 个 artifact 已下载并与当前 PR run 清单逐项匹配。完整 38 个 Windows/Linux 包组合（24 Windows、14 Linux）通过，逐包核对 Q16/Core/SystemDrawing 版本及实际托管/原生文件字节，保留前置依赖、运行时、架构、资源及包完整性检查。报告 `/tmp/syncclipboard-pr419-0865-package-audit.json`，SHA256 `3024e9e2d016f8ecfe0a8834680831a7ac6488ed3aa0b57bcd34303ffa238f1c`。六个 Linux 包的元数据检查通过。

双架构 macOS 包均通过 strict codesign、各 19 个 dylib 架构与全部前置依赖核对。Magick 原生库的 LC_ID_DYLIB 在打包后变为 `@executable_path/../../Contents/MonoBundle/Magick.Native-Q16-<arch>.dll.dylib`；对官方 NuGet 临时副本应用该精确路径变换，再移除签名并归一化签名分配影响的 LINKEDIT vmsize 后，剩余完整字节逐一相同。损坏代码字节负例仍被拒绝，没有跳过原生检查或改写真实包。报告 `/tmp/syncclipboard-pr419-0865-macos-{arm64,x64}-audit2.json`，挂载均已卸载。

Server 与 amd64/arm64 容器的实际 CI 日志各有四轮 Production 启动与两轮 API 冒烟成功证据，下载 Server 产物在本机复验同样通过。图片汇总报告 `/tmp/syncclipboard-pr419-0865-image-report-audit.json`；本地完整记录 `docs/ai_design/.local/PR-419-Artifacts-086599a9.md`。步骤 11 非 UI 验证通过，监控 pr419 已删除，允许进入 12a；后续升级仍未完成。


## 步骤 12a：SharpHook 迁移准备（2026-09-14）

前置步骤 11 已完整通过。官方 NuGet 实时索引及下载包核定 SharpHook 最新稳定版 8.0.0，当前使用 5.2.3；8.0.0 的 net10.0 资产无额外包依赖，源码提交 `a1093d81eb5d96608a7885949d4a5e21b98efec5`。依据：[官方包](https://www.nuget.org/packages/SharpHook/8.0.0)、[迁移指南](https://sharphook.tolik.io/articles/migration.html)、[发布说明](https://github.com/TolikPylypchuk/SharpHook/releases/tag/v8.0.0)。已冻结当前依赖图到 `/private/tmp/syncclipboard-stage12a-baseline-assets/`，尚未修改依赖或产品代码。

需适配 SharpHook.Native → SharpHook.Data、事件模拟类型迁入 SharpHook.Simulation、EventSimulator.Create 初始化及 DI 管理的 IDisposable 生命周期、SimpleGlobalHook 构造和 Run 调用。项目当前使用 KeyPressed/KeyReleased，不依赖新版默认关闭的 KeyTyped。新版键码数值全部变化；项目配置使用自己的 Key 字符串枚举，应保留其格式。Vc102 改为 VcSection，VcKanji/VcHangul 合并至 VcHanja/VcKana，不能直接删除旧配置对应键或重复添加字典键；需覆盖输入映射、反向模拟映射以及注册/移除时的旧别名兼容。

Linux 默认后端变化涉及新增权限需求；本步骤按官方兼容路径保留既有 XRecord 后端行为，在首次 SharpHook 使用前配置，避免升级同时引入新的桌面权限与行为要求。仍须打包完整的 libuiohook.so、libuiohook-xrecord.so、libuiohook-x11.so、libuiohook-wayland.so，并核对 Windows/macOS 原生文件及版本。不得为测试注册真实 hook、创建设备、发送模拟输入、请求系统权限或启动 UI；初始化、执行和清理均经审查的 mock/纯键码逻辑可测试。

源代码检查和目标版本核定不代表本步骤通过。接下来先实现最小兼容改动和非 UI 回归，再进行本地检查、当前提交 PR CI/全部产物与评审；12a 未通过前不开始 12b。


### 步骤 12a 实现与本地非 UI 验证

SharpHook 5.2.3 → 8.0.0，采用 net10.0 资产。迁移 Data/Simulation 命名空间、SimpleGlobalHook 构造及 Run 调用。新增共享 SharpHookFactory，在真实模拟器或 hook 首次解析时设置 Linux XRecord 后端并检查返回值，避免在普通服务注册或非 UI DI 测试初始化时加载真实后端。EventSimulator 使用 Create，注册为 DI 工厂单例，随服务容器释放；hook 使用同一工厂确保后端配置顺序。

键码表改用 VcSection，保留项目 Key 枚举及其 JSON 名称。VcHanja/VcKana 作为新版输入映射，反向映射仍接受旧 Key.Kanji/Key.Hangul；SharpHook 注册及注销边界统一别名，使旧名称和新名称对应同一热键槽，不改变 Windows 原生热键注册器或配置存储。新增 25 项 Core 纯逻辑/mock 检查及 2 项 Desktop NonUI 注册表检查，验证左右修饰键、全部反向映射、固定旧配置 JSON、别名匹配/注销、模拟事件顺序、后端/初始化失败及 DI 单例释放。测试 provider 全为 mock，没有真实 hook、设备初始化、输入事件或 UI 调度。

本地 Core 381、Desktop NonUI 6 项通过，0 失败/跳过；报告 `/tmp/syncclipboard-stage12a-results/`。固定 JSON 加强后的 25 项回归再次通过，报告 `/tmp/syncclipboard-stage12a-keyboard-final/`。格式检查退出 0，仅跨平台工作区加载警告。Core 测试直接引用中央已有 Moq 4.20.72，其新增 Castle.Core/System.Diagnostics.EventLog 仅存在于测试图；Desktop/WinUI/macOS 产品依赖图仅 SharpHook 变更。Core 图另出现本地 arm64/AnyCPU 构建选择差异，Q16 版本仍为 14.17.1，没有再次升级图片依赖。依赖对照 `/tmp/syncclipboard-stage12a-dependency-diff.json`。

Windows/macOS 产品还原和 Linux x64 交叉发布成功，Linux SharpHook.dll 与四个原生后端文件逐字节匹配 NuGet；完整本地依赖审计 `/tmp/syncclipboard-stage12a-linux-local-audit.json` 通过，缺少任一后端文件的负例被拒绝。真实全平台包仍待 PR 构建与下载检查。

ELF 检查发现 8.0.0 的 Linux x64/arm64 XRecord 原生库均引用 GLIBC_2.38，并显式依赖 X11、Xtst、Xt、Xrandr、xkbcommon；证据 `/tmp/syncclipboard-stage12a-native-requirements.json`。因此 README 将桌面客户端 glibc 最低要求更新为 2.38，并明确这不适用于独立服务器；不能继续沿用仅 .NET 运行时的 2.27 下限。RPM/Debian 包声明补齐对应 XRecord 依赖。Ubuntu 24.04 的 Xt 包名为 [libxt6t64](https://packages.ubuntu.com/noble/libxt6t64)，xkbcommon 为 [libxkbcommon0](https://packages.ubuntu.com/noble/libxkbcommon0)。当前 PupNet 1.8 的包名配置不能编码版本约束，此限制在配置注释和 README 明示，后续步骤 17c/18 需继续核对；没有声称旧 glibc 系统可运行新版桌面客户端。

CI 最低计数调整为 Core 381、每平台 Desktop NonUI 6、WinUI NonUI 6，预计五份 TRX 合计 405。保留步骤 11 的七架构图片任务、Windows x86 产品编译、S3 及全部包矩阵；预计仍有 55 个 artifact。当前步骤仍须新提交 PR CI、405 项非 UI 测试、所有产物与评审实际通过，不能开始 12b。

### 步骤 12a 评审修复：英文 Linux 要求

提交 `2714a3608984f31a6cebb8fc0032db1c8bc3baf2` 的评审发现英文 README 仍保留 glibc 2.27，未同步中文说明中的 SharpHook 8 原生依赖要求。已将英文桌面要求改为 glibc 2.38，列出 X11/XTest/Xt/Xrandr/xkbcommon 及 Ubuntu 24.04 包名，并同步 XRecord/XWayland 和独立服务器范围说明。依据仍为本步骤已检查的 NuGet 原生 ELF 依赖；未执行真实桌面验证。

该提交下载的 Server 产物在本机通过四轮 Production 启动和两轮 API 冒烟；Core CI 报告 381 项通过。上述仅为阶段证据，不能代替文档修复后当前 head 的完整 CI、产物及评审验证，步骤 12a 尚未通过。

### 步骤 12a 最终 PR 与产物验证

验证通过提交 `1d523f2f701cdf697de71373519effa13d70e4db`：[PR run 34783783165](https://github.com/Jeric-X/SyncClipboard/actions/runs/34783783165)、[push run 34783781093](https://github.com/Jeric-X/SyncClipboard/actions/runs/34783781093)、[CodeQL 34783782802](https://github.com/Jeric-X/SyncClipboard/actions/runs/34783782802) 与 CodeFactor 均成功，共 119 项 SUCCESS、8 项预期发布 SKIPPED。五份 TRX 合计 405 项通过，0 失败/跳过；七 RID 图片报告共 45 组及 S3 七组检查通过，依赖哈希和重试次数均已复核。

全部 55 个 artifact 与当前 PR run 清单一致。完整 38 个 Windows/Linux 组合（24 个 Windows、14 个 Linux）通过，保留全部前置依赖检查，并逐包确认 SharpHook 8 net10.0 托管文件、Windows 原生 DLL、Linux 四后端库的官方 NuGet 字节和 CPU 架构。完整报告 `/tmp/syncclipboard-pr419-1d52-package-audit.json`，SHA256 `48a8087d0e8fd01e7bcf1e8cb7daf9c3daab020cf6ae6424e6ca84a630d6c9e5`。

六个 Linux deb/rpm 包元数据包含新增原生依赖；双架构 macOS 包严格签名、19 个 dylib 架构和 SharpHook 文件核对通过，仅允许已知 bundle install-name 与签名元数据变换，挂载已卸载。Server 与双架构容器 CI 各四轮 Production 启动及两轮 API 冒烟通过，下载 Server 产物本机相同检查通过。本地完整记录 `docs/ai_design/.local/PR-419-Artifacts-1d523f2f.md`，所有检查均未启动 UI。

当前 head 的 Codex 评审于 `2026-09-13T21:34:10.702715Z` 完成。英文 Linux 要求遗漏已修复；另有意见认为删除 `SimpleGlobalHook(true)` 改变异步事件分发，经 [5.2.3 精确源码](https://github.com/TolikPylypchuk/SharpHook/blob/eaadf9e7db2d0c3b83680e22009c9796d5e71dad/SharpHook/GlobalHookBase.cs#L135) 与 [8.0.0 精确源码](https://github.com/TolikPylypchuk/SharpHook/blob/a1093d81eb5d96608a7885949d4a5e21b98efec5/src/SharpHook/SimpleGlobalHook.cs) 核对不成立：旧参数只设置 RunAsync 所建线程的 IsBackground，项目升级前后均在线程池调用 Run；两版 SimpleGlobalHook 均直接同步 DispatchEvent。保留既有行为，没有为此切换事件分发模型或执行真实 hook/UI。

最终八个评审线程均已处理并解决，未新增可处理反馈，没有发送 GitHub 评论。步骤 12a 非 UI 验证通过，监控 pr419 已删除，允许进入 12b；后续升级仍未完成。

## 步骤 12b：NativeNotification 版本核定

2026-09-14 查询官方 NuGet 实时版本索引，NativeNotification 与 NativeNotification.Interface 最新稳定版均为现用的 1.0.5；保留配套版本，不虚构升级或切换其他通知方案。[NativeNotification 版本与依赖](https://www.nuget.org/packages/NativeNotification/1.0.5)。两个包均声明源码提交 `810605cedc431de35cba7f9ba333027c65be9031`，已下载精确源码审查接口与初始化行为。

NativeNotification 按平台提供 net8.0、net8.0-windows10.0.17763、net10.0-macos26.0 资产；Interface 为 net8.0。Linux 依赖 Tmds.DBus 0.92.0，Windows 依赖 Toolkit.Uwp.Notifications 7.1.3，本步骤不混入后续 12e 升级。既有依赖图冻结于 `/private/tmp/syncclipboard-stage12b-baseline-assets/`。接下来验证通知参数与回调的 mock 路径、三平台编译和实际包资产；不创建真实通知管理器、不显示通知或访问通知中心。本步骤尚未通过。

### 步骤 12b 实现与本地非 UI 验证

新增五项 NativeNotificationTests，调用实际产品 ShowText、SharedQuickMessage、ProfileActionBuilder.ToActionButtons 和 keyed DI 注册。公共 NotificationManagerBase 的初始化只创建托管会话字典；平台 Create/Show/Remove 均由 mock 接管。检查 Unicode/多行载荷、可选按钮、两秒时长、共享实例复用及旧按钮清除、移除后重新显示的顺序、按钮过滤/顺序/独立回调和无动作按钮，以及通知单例只创建一次。回调只修改测试计数，不复制、打开文件或访问桌面。

本地 Core 386、Desktop NonUI 6 项通过，0 失败/跳过，报告 `/tmp/syncclipboard-stage12b-results/`；格式诊断修正后五项通知回归再次通过，报告 `/tmp/syncclipboard-stage12b-notification-final/`。完整格式检查退出 0，仅跨平台工作区加载警告；Windows/macOS 还原、工作流语法与 diff 检查通过。首次测试因沙箱命名管道权限失败，结束该尝试后在允许 MSBuild 的环境重跑通过，没有修改产品或降低测试要求。

Core/Test/Desktop/WinUI/macOS 五份依赖图均未变化，报告 `/tmp/syncclipboard-stage12b-dependency-diff.json`。包审计增加各平台 NativeNotification 资产、Interface 及 Linux Tmds.DBus 的精确 NuGet 字节核对；前置阶段 Windows/Linux 产物已用于核定检查规则，缺失 Interface 的负例被拒绝，这些旧 head 结果不替代本步 PR 验证。CI 的 Core 最低数由 381 调整为 386，五份 TRX 预计合计 410；保留 45 组图片、七组 S3、55 个 artifact 和所有构建/打包矩阵。当前提交的完整 CI、产物和评审均待实际通过，尚不开始 12c。

### 步骤 12b 最终 PR 与产物验证

验证通过提交 `4574899507c94acdbe1e8372944c17d249ff8aa1`：[PR run 34785181519](https://github.com/Jeric-X/SyncClipboard/actions/runs/34785181519)、[push run 34785182015](https://github.com/Jeric-X/SyncClipboard/actions/runs/34785182015)、[CodeQL 34785181024](https://github.com/Jeric-X/SyncClipboard/actions/runs/34785181024) 与 CodeFactor 均成功，共 119 项 SUCCESS、8 项预期发布 SKIPPED。五份 TRX 共 410 项通过，0 失败/跳过；七 RID 共 45 组图片和七组 S3 检查通过。

全部 55 个 artifact 与当前 run 清单一致，完整 38 个 Windows/Linux 组合（Windows 24、Linux 14）及六个 Linux 包元数据通过。各包 NativeNotification/Interface 与 Linux Tmds.DBus 的官方平台程序集字节一致，保留所有前置依赖核对；完整报告 `/tmp/syncclipboard-pr419-4574-package-audit.json`，SHA256 `957bb81db7662e9c5180ce3c15fc624f50d0eddf724daacac8b61cd936192554`。

macOS NativeNotification.dll 经 SDK 的 managed-static registrar 重写，不能直接要求原始 NuGet 字节一致。已核对实际 26.5.10315 SDK 的 SelectRegistrar：Release macOS 默认使用此模式。只读 Cecil 元数据/IL 比较确认原有 16 类型、122 方法及字段、属性、事件和资源完全一致；差异仅为逐条审查过的七方法 ObjCRuntime.__Registrar__、注册该实例的模块初始化器，以及精确的 .NET 运行时/Microsoft.macOS 26.5 引用重定向。修改原有 IL、删方法、改注册器、增引用或资源的五种负例均被拒绝。Interface 原始字节一致，没有通过关闭注册器或改变发布行为来消除差异。

双架构 macOS 最终包的上述程序集检查、严格签名、19 个 dylib 架构及前置依赖通过，挂载已卸载；报告 `/tmp/syncclipboard-pr419-4574-macos-x64-audit2.json` 与 `/tmp/syncclipboard-pr419-4574-macos-arm64-audit.json`。Server 与双架构容器各四轮 Production 启动、两轮 API 冒烟通过，本机下载产物复验相同。完整本地记录 `docs/ai_design/.local/PR-419-Artifacts-45748995.md`。

Codex 于 `2026-09-13T22:00:02.885408Z` 完成本 head 评审，无新增意见；最终八个线程均已解决，行内反馈无变化，没有发送 GitHub 评论。步骤 12b 非 UI 验证通过，监控 pr419 已删除，允许开始 12c；未执行 UI 验证，后续升级仍未完成。

## 步骤 12c：Vanara 版本与迁移范围

2026-09-14 官方实时索引确认 ComCtl32、DbgHelp、Kernel32、User32 最新稳定版均为 5.0.7，四包由 4.0.1 配套升级；[NuGet 版本](https://www.nuget.org/packages/Vanara.PInvoke.User32/5.0.7)。升级前六份依赖图冻结于 `/private/tmp/syncclipboard-stage12c-baseline-assets/`。本项目实际引用仅在 WinUI3，涉及 TaskDialog 参数/内存、键码与热键及低层键盘 hook 签名、崩溃转储结构和调用。下一步核对 5.x 的 Shared/Core 合并、生成式 API 和结构布局，补齐可隔离的非 UI 验证，再提交当前步骤 PR；不显示对话框、不注册真实热键或 hook，不操作真实窗口/剪贴板。


### 步骤 12c 实现与本地非 UI 验证

已核对四包声明的精确源码提交 `f8473235041cce8e9c894dfb387e98dd9df789e9`。5.0.7 的 RegisterHotKey 使用 EnumRebase<VK, uint>，保留 uint 隐式转换；MiniDumpWriteDump 的 in 结构与可选参数仍接受现有调用；TaskDialogIndirect 和 hook 调用也通过隔离编译。无需为签名引入额外行为修改。WinUI3 及其测试依赖图从八个 4.0.1 包改为七个 5.0.7 包，Shared 合并入 Core；四份 Core/Test/Desktop/macOS 图不变，报告 `/tmp/syncclipboard-stage12c-dependency-diff.json`。

WinUIGlobalDialog 增加内部可注入的原生调用委托，公开默认构造和实际 TaskDialogIndirect 调用保持原行为。新增 20 项 NonUI 用例：确认/取消返回值、Unicode 和多按钮原生内存载荷、单关闭按钮、HRESULT 失败与调用异常传播及配置字符串释放；左右修饰键/东亚别名、字母键和未知键转换；TaskDialog、minidump 结构布局以及合成键盘事件缓冲区解码。测试仅访问自有内存，原生对话框调用由委托替代，不构造控件、显示对话框、调用真实 hook 或查询桌面。Windows 最低通过数由 6 调整为 26，当前 CI 五份 TRX 预期共 430 项。

本地 Core 386、Desktop NonUI 6 项通过，0 失败/跳过，报告 `/tmp/syncclipboard-stage12c-results/`；Windows/macOS 还原、工作流语法及完整格式检查通过，后者仅有跨平台工作区加载警告。隔离项目链接实际 WinUIGlobalDialog、KeyboardMap 及新增测试源码，并编译现有 minidump/hook/hotkey 调用，0 警告/错误；该证据不代表在 macOS 完成 WinUI3 全量构建或运行 Windows 测试。

产物审计新增七个 Vanara 5.0.7 net10.0-windows7.0 程序集的精确官方字节及依赖图检查，拒绝残留 Shared 和非 Windows 包引入 Vanara；已用官方资产核定规则，缺文件、旧 Shared、错误字节和 Linux 混入四种负例均拒绝。当前 head 的 Windows 测试、全平台 CI、55 个产物与评审仍待通过，步骤 12c 尚未通过，不进入 12d。


### 步骤 12c 首轮 PR 反馈

提交 `c30f4a54e5e321233baaad1ece26b11164b03e76` 的 CodeFactor 检出两处 SA1407：测试中原生按钮地址及结构偏移量计算混用加法和乘法，需显式标注优先级。已仅为乘法添加括号，不改变断言或产品行为；隔离编译 0 警告/错误，完整格式检查退出 0。两个问题分别追加本地修复日志；没有关闭检查或发送 GitHub 评论。修复后提交仍需完整 CI、产物与评审验证，当前步骤未通过。


### 步骤 12c 测试程序集引用修复

`32d09403` 的 Windows 26 项非 UI 测试全部通过，0 失败/跳过。三个已下载的 Windows 原始产物通过全部前置依赖与七个 Vanara 5.0.7 官方资产核对，macOS arm64 包签名、19 个 dylib、前置依赖与无 Vanara 检查通过并已卸载。服务器/双架构容器及本机下载服务器均通过四轮启动和两轮 API，S3 七组通过；这些是该提交的阶段证据，不替代后续修复提交的结果。

完整日志另发现两个本次新增的 CS0436，涉及 SDK 生成的 AutoInitialize 与 NativeMethods：新增 InternalsVisibleTo 使产品程序集中的内部 SDK 类型对测试可见，与测试自身生成类型重名。上一通过提交的同一 Windows job 没有这两个警告。测试到 WinUI 产品的引用改为显式 WinUIProduct 别名，并同步三处产品命名空间导入；不抑制警告，不关闭或改变 SDK 初始化。

隔离编译使用当前 CI 的真实产品程序集、原始 SDK 初始化源码和实际新增测试：引用同时进入全局命名空间时复现两个 CS0436，仅保留 WinUIProduct 别名后 0 警告/错误。证据 `/tmp/syncclipboard-stage12c-alias-{baseline,fixed}.log`。Windows 测试还原与仓库格式检查退出 0；当前修复仍需 PR 验证全部 26 项用例并确认两警告消失，步骤 12c 尚未通过。


### 步骤 12c 最终 PR 与产物验证

验证通过提交 `81bdec694690812e01fb7fcc545c7f1f2f7e3679`：[PR run 34788050747](https://github.com/Jeric-X/SyncClipboard/actions/runs/34788050747)、[push run 34788048310](https://github.com/Jeric-X/SyncClipboard/actions/runs/34788048310)、[CodeQL 34788050577](https://github.com/Jeric-X/SyncClipboard/actions/runs/34788050577) 与 CodeFactor 均成功，119 项 SUCCESS、8 项预期发布 SKIPPED。合并测试提交 `ad8f3133bf195f0c32e90a203b74b9ad267b652a` 的父提交包含当前 head 和 master 基线。五份 TRX 共 430 项通过（Core 386、三平台 Desktop 各 6、WinUI3 26），0 失败/跳过；Windows 日志已确认 AutoInitialize 与 NativeMethods 两条 CS0436 消失，未抑制诊断或改变 SDK 初始化。

全部 55 个 artifact 与当前 run 清单及 head 一致。完整 38 个 Windows/Linux 组合通过：24 个 Windows 组合逐包核对七个 Vanara 5.0.7 net10.0-windows7.0 程序集的官方字节与依赖图，没有残留 Shared；14 个 Linux 组合未混入 Vanara。前置 .NET/Microsoft/EF/SQLite、Avalonia/WinUI、AWS、图像、SharpHook、通知和资源检查全部保留。报告 `/tmp/syncclipboard-pr419-81bd-package-audit.json`，SHA256 `0a01144fc7216f920a4d3300258df63ef7a52df1737674fd803b442a9f06ae58`。

六个 Linux deb/rpm 包元数据、macOS 双架构严格签名及每包 19 个 dylib、通知 SDK 注册器改写和全部前置程序集检查通过；macOS 无 Vanara，挂载均已卸载。七 RID 共 45 组图片及七组 S3 检查通过。服务器与双架构容器各四轮 Production 启动、两轮 API 冒烟通过，本机下载服务器复验相同。完整记录 `docs/ai_design/.local/PR-419-Artifacts-81bdec69.md`，没有执行任何 UI 验证。

Codex 于 `2026-09-13T22:57:16.574124Z` 完成本 head 评审，阶段收尾复查无新增意见，八个线程均已解决；两项 CodeFactor SA1407 和两项新 CS0436 均已修复并验证。监控 pr419 已删除，步骤 12c 非 UI 验证通过，允许开始 12d；12d–18 仍须依次执行，整体升级尚未完成。


## 步骤 12d：Interop.UIAutomationClient 版本核定与兼容验证

2026-09-14 官方 NuGet 实时索引的最新稳定版为现用 `10.19041.0`，保留版本，不引入替代 UI 自动化框架。[官方包与版本](https://www.nuget.org/packages/Interop.UIAutomationClient/10.19041.0)。项目仅在 WinUI3 引用；各 Windows RID 选择 netcoreapp3.0 资产，包自身不依赖其他包，build target 保持 EmbedInteropTypes=false。六份升级前依赖图冻结于 `/private/tmp/syncclipboard-stage12d-baseline-assets/`，本步骤还原后图均未改变，报告 `/tmp/syncclipboard-stage12d-dependency-diff.json`。

CurrentSelectedContentProvider 的单元素文本提取方法与 CaretPositionProvider 的单元素光标提取方法改为 internal，供现有友元测试访问；算法、COM 创建、原生入口和回退顺序不变。新增 21 项 NonUI 契约用例，使用严格托管接口 mock 检查不支持/失败模式、空选区、多段 Unicode/换行/空白保留、首个矩形和带符号坐标转换、TextPattern2 缺失/失败时回退、无选区、退化选区克隆后扩展，以及扩展失败/不完整矩形返回 null。原始范围不被修改，mock 对象不是 COM 对象。

本地隔离宿主链接实际两个 provider 与原生声明源文件，只调用上述两个单元素入口，21 项通过、0 失败/跳过，报告 `/tmp/syncclipboard-stage12d-provider-results/`。不加载 WinUI SDK/界面，不创建 CUIAutomation8，不调用 GetCurrentSelectedContent/GetCaretPosition 的真实桌面入口、焦点/祖先元素查询、资源管理器 COM、Win32/MSAA 或剪贴板。临时 net10 宿主编译未执行的 Windows 专用方法时有 CA1416 提示，未屏蔽诊断；它不代替真实 Windows TFM 的编译/测试。首轮临时宿主因 /tmp 符号链接导致项目依赖未纳入还原图，改用规范化 /private/tmp 路径重新还原后通过，没有修改产品依赖来绕过问题。

Core 386、Desktop NonUI 6 项通过，0 失败/跳过；新增测试格式检查、平台还原、仓库格式和工作流语法通过。Windows CI 最低通过数由 26 增至 47，五份真实 CI TRX 预期共 451 项。UI Automation 包审计新增版本、唯一文件、netcoreapp3.0 官方字节和非 Windows 无该依赖的检查，官方 DLL SHA256 `dcea43a1f5a2114b7bcc9e41cc1377064307611d0caa50f1ec203b887f4c20bb`；缺文件、错误字节、错误版本和 Linux 混入四种负例均拒绝。前一步 Windows 产物仅用于核定审计规则，不能作为本步骤通过证据。

当前步骤仍待 Windows 完整测试、全平台 CI、全部 55 个产物和评审通过，不进入 12e。所有需要真实 UI/COM 客户端或桌面会话的验证继续范围排除，不安排人工或解锁后补测。

### 步骤 12d CI 与非 UI 验证证据

提交 `794d7b623358b477fbe1a2d05fca71fed2c4baef` 的 [PR run 34789650486](https://github.com/Jeric-X/SyncClipboard/actions/runs/34789650486)、[push run 34789648924](https://github.com/Jeric-X/SyncClipboard/actions/runs/34789648924)、[CodeQL 34789650219](https://github.com/Jeric-X/SyncClipboard/actions/runs/34789650219) 和 CodeFactor 已成功，119 项 SUCCESS、8 项预期发布 SKIPPED。合并测试提交 `088007f2fb3770948f7408ca1d64501cd6f8751a` 包含本提交及 master 基线。五份下载的 TRX 确认 451 项通过（Core 386、三平台 Desktop 各 6、WinUI3 47），0 失败/跳过；Windows TRX 包含全部新增 21 项 mock 契约用例，实际日志未出现 CS0436。

七 RID 的 45 组图片检查及官方程序集哈希、七组 S3 检查与重试次数均已核对。服务器及双架构容器 CI 各四轮 Production 启动、两轮 API 检查通过，下载服务器在本机复验相同。六个 Linux 包元数据与 macOS 双架构包检查通过；每个 macOS 包的 19 个 dylib、严格签名、前置依赖及无 Windows UIAutomation/Vanara 载荷均已核对，挂载已卸载。没有执行 UI 验证。

远程清单已核对全部 55 个 artifact 的名称、所属 run/head 和未过期状态。Codex 于 `2026-09-13T23:29:06.914326Z` 完成本提交评审，八个线程均已解决、行内反馈无变化。完整包审计与最终复查结果见下文。


### 步骤 12d 最终通过记录

上述提交的全部 55 个 artifact 已下载并与当前 run/head 清单逐项匹配。完整 38 个 Windows/Linux 组合审计通过：24 个 Windows 组合逐包核对 Interop.UIAutomationClient 10.19041.0 的唯一 netcoreapp3.0 官方程序集及依赖声明；14 个 Linux 组合没有混入该 Windows 依赖。Vanara 七包和所有前置 .NET、Microsoft、EF/SQLite、Avalonia/WinUI、AWS、图像、SharpHook、通知及资源检查全部保留并通过。报告 `/tmp/syncclipboard-pr419-794d-package-audit.json`，SHA256 `8921b5f89f1ec3e6a2c81c6cb774f3c3cb859161a86d7651a9169370a0294946`。

最终远程复查仍为 119 项 SUCCESS、8 项预期发布 SKIPPED，八个评审线程均已解决，行内及普通评论无变化。451 项非 UI 测试、七 RID 45 组图片、七组 S3、六个 Linux 包元数据、macOS 双架构及服务器/容器检查均通过。完整本地记录 `docs/ai_design/.local/PR-419-Artifacts-794d7b62.md`。监控 pr419 已删除，步骤 12d 非 UI 验证通过，允许开始 12e；整体升级尚未完成。


## 步骤 12e：Microsoft.Toolkit.Uwp.Notifications 版本核定与兼容验证

2026-09-14 官方实时 NuGet 索引确认现用 `7.1.3` 已是最新稳定版，保留版本。[官方包](https://www.nuget.org/packages/Microsoft.Toolkit.Uwp.Notifications/7.1.3)。它由 NativeNotification 1.0.5 的 Windows 资产间接引入，当前 WinUI3 及其测试选择 `lib/net5.0-windows10.0.17763/Microsoft.Toolkit.Uwp.Notifications.dll`；System.Drawing.Common 实际仍为前置步骤核定的 10.0.0。六份基线图冻结于 `/private/tmp/syncclipboard-stage12e-baseline-assets/`，还原后均不变，报告 `/tmp/syncclipboard-stage12e-dependency-diff.json`。

新增 11 项 NonUI 载荷契约用例：实际 ToastSession/ProgressSession 的受保护 GetBuilder 经测试子类暴露，仅构造托管数据；验证三个文本绑定、可选图片 URI、按钮顺序、Unicode/XML 特殊字符与原样激活参数、进度绑定和不确定状态、静音 XML，以及重建载荷时清理旧图片/按钮且不修改旧载荷。构造函数仅保存未使用的空 manager；不创建 WindowsNotificationManager，不调用 Show/Update/Remove、GetToast/GetXml、WinRT 通知服务、真实激活或界面操作。按钮回调不执行，通知 IsAlive 始终为 false。

官方包重新下载后与缓存 Windows DLL 字节一致，SHA256 `e6676557727bc03cf7bceb1cb7b46ec4623ed7eb57813e8f04785bcd9d868b05`。包声明的源码提交 `72205c9add7c3fc1ed63bb77e6fc101e39f1ac33` 中 AddAudio 对空 src 的处理与实际发行 DLL 不同；只读 IL 检查及隔离探针确认真实 DLL 接受 NativeNotification 使用的 AddAudio(null, null, true)，并生成 silent=true、无 src 的 XML。以官方发行资产和真实执行为准，没有据源码差异臆测故障或修改产品行为。证据 `/tmp/syncclipboard-stage12e-source-audit.json`。

本地隔离测试直接引用官方 Windows 依赖 DLL，11 项通过、0 失败/跳过，TRX 位于 `/tmp/syncclipboard-stage12e-toast-results/`；临时 net10.0 宿主对 Windows 标注 API 的 CA1416 提示保留，不把此探针当作真实 Windows 平台验证。Core 386、Desktop NonUI 6 项通过，0 失败/跳过；Windows/macOS 还原、仓库格式检查、actionlint 和 diff 检查通过，仓库格式检查只有跨平台工作区加载警告。

Windows CI 最低通过数从 47 调整为 58，五份 TRX 预期共 462 项；既有七 RID 45 组图片、七组 S3、完整平台矩阵和 55 个 artifact 要求保留。产物审计新增该 Toolkit 的精确版本、唯一文件及官方 Windows 资产字节核对，非 Windows 不得混入；缺文件、错误字节、错误版本、Linux 混入和重复 DLL 五种负例均拒绝。上一步产物仅用于核定审计规则，不能代替本步证据。当前提交仍待完整 Windows CI、全部产物及评审通过，不进入 13a。


### 步骤 12e 最终通过记录

验证通过提交 `527efe6bed5a4132c966499e1f968a3a4dd7393d`：[PR run 34791441402](https://github.com/Jeric-X/SyncClipboard/actions/runs/34791441402)、[push run 34791438928](https://github.com/Jeric-X/SyncClipboard/actions/runs/34791438928)、[CodeQL 34791440969](https://github.com/Jeric-X/SyncClipboard/actions/runs/34791440969) 与 CodeFactor 均成功，119 项 SUCCESS、8 项预期发布 SKIPPED。合并测试提交 `7e2b1b5dc2dc997b8f6f1faa31009ce573b0af58` 包含 master 基线及当前 head。五份 TRX 共 462 项通过（Core 386、三平台 Desktop 各 6、WinUI3 58），0 失败/跳过；实际 Windows 报告包含全部新增 11 项载荷用例，日志未出现新增警告。

55 个 artifact 全部下载并与当前 run/head 清单匹配。完整 38 个 Windows/Linux 组合通过：24 个 Windows 组合逐包核对 Microsoft.Toolkit.Uwp.Notifications 7.1.3 的唯一 net5.0-windows10.0.17763 官方资产与依赖版本；14 个 Linux 组合未混入此 Windows 依赖。所有前置 .NET/Microsoft、EF/SQLite、Avalonia/WinUI、AWS、图片、SharpHook、通知、Vanara、UIAutomation 及资源检查全部保留。报告 `/tmp/syncclipboard-pr419-527e-package-audit.json`，SHA256 `81847f441326938e1419947c9bb2869c0ceab7ae70c8e0f45aac2b1d32795c62`。

六个 Linux 包元数据、macOS 双架构严格签名及各 19 个 dylib、通知注册器改写和全部前置依赖审计通过，macOS 不含 Windows Toolkit/UIAutomation/Vanara，挂载均已卸载。七 RID 45 组图片、七组 S3 与服务器/双架构容器 CI 检查通过；下载服务器本机复验同样完成四轮启动及两轮 API。

Codex 于 `2026-09-14T00:07:48.655185Z` 完成本 head 评审；最终八个线程均已解决，行内和普通评论无新增问题，没有发送 GitHub 评论。完整本地记录 `docs/ai_design/.local/PR-419-Artifacts-527efe6b.md`。监控 pr419 已删除，步骤 12e 非 UI 验证通过，允许进入 13a；整体升级尚未完成，UI 验证继续范围排除。


## 步骤 13a：CommunityToolkit.Mvvm 升级

2026-09-14 官方实时 NuGet 索引核定最新稳定版为 8.4.2，中央版本从 8.4.0 改为 8.4.2。[8.4.1 发布说明](https://github.com/CommunityToolkit/dotnet/releases/tag/v8.4.1) 包含 Roslyn 5.0 / C# 14 支持、CanExecute override 与语言版本检查修复；[8.4.2](https://github.com/CommunityToolkit/dotnet/releases/tag/v8.4.2) 修复重复分析器筛选的版本判断顺序。包声明精确源码提交 `35dfe7b12da0e64384bfaec7ebc0883196a89f9f`。未同时升级 ObservableCollections；后续本地格式检查要求迁移 ObservableProperty 字段，处理见下文。

六份升级前依赖图冻结于 `/private/tmp/syncclipboard-stage13a-baseline-assets/`。旧版 Core 的 90 份 MVVM 源生成文件保存于 `/private/tmp/syncclipboard-stage13a-generated-before/`；仅升级包版本、尚未迁移字段时重新编译，90 份生成文件逐字节相同，报告 `/tmp/syncclipboard-stage13a-generated-diff.json`。实际 Csc 命令确认只使用 8.4.2 的 roslyn5.0 CodeFixers 和 SourceGenerators，未复用旧生成器；证据 `/tmp/syncclipboard-stage13a-selected-generators.json` 及编译诊断日志。Core 构建 0 警告/错误，现有 386 项 Core 测试通过、无失败/跳过，报告 `/tmp/syncclipboard-stage13a-initial-results/`。

### 属性迁移与初始化兼容性

8.4.2 在当前 C# 14 工具链启用 MVVMTK0042 信息诊断；仓库 `dotnet format --severity info` 因此未通过。采用官方修复将 ObservableProperty 字段转换为 partial 属性，保留属性名、类型、通知/命令特性及初始化值，没有屏蔽诊断。随后按生成器签名调整 HistoryRecordVM 的旧值可空标注，并处理两个纯数据编辑模型的主构造函数诊断。先前 90 份生成文件的逐字节比较仅证明“单独换包”阶段，不能作为迁移后代码相同的证据。

官方字段修复会将构造函数中的直接字段赋值改为属性 setter；About、HistorySetting、ServerConfig、CliboardAssistant、SystemSetting、SyncSetting 六个模型因此增加只读初始化标记，在原字段加载期间禁止配置回写和平台回调，加载结束后恢复原属性行为。否则可能保存不完整历史配置、规范化未知主题/语言、舍入文件字节限制，或调用字体/启动项服务。SystemSetting 的内部构造入口接受已有启动项状态，StaticConfig 的内部入口接受临时路径，供测试隔离真实任务计划程序和用户配置；公开入口保持原服务来源。

新增 25 项非 UI 回归：13 项覆盖实际属性通知顺序/相等值去重、依赖属性、整数校验边界、生成式校验、命令实例与 CanExecute、异步取消/重入及失败恢复；12 项以六个实际设置模型的默认/已保存配置分别验证无构造期写入、加载值一致及后续修改生效。使用临时目录和严格 mock，不启动桌面框架、不操作真实窗口/通知/剪贴板/输入设备。初始化测试曾发现未知主题仍被改写，补齐保护后 25 项全部通过、0 失败/跳过，报告 `/tmp/syncclipboard-stage13a-initialization-results2/`。

当前仍须完成全量本地验证与依赖图核对，然后提交当前 head 验证完整 CI、全部产物及评审。步骤 13a 尚未通过，不进入 13b。


### 步骤 13a 本地验证结果

最终 Core 411、Desktop NonUI 6 项通过，0 失败/跳过，TRX `/tmp/syncclipboard-stage13a-final-results/`；编译无新增警告。WinUI3、macOS 和 WinUI3 测试项目还原成功。六份依赖图只有 CommunityToolkit.Mvvm 8.4.0 → 8.4.2，无其余包增删或元数据变化，报告 `/tmp/syncclipboard-stage13a-dependency-diff.json`。仓库格式检查退出 0（仅跨平台工作区加载警告），actionlint 与 diff 检查通过。

CI Core 最低通过数从 386 提升为 411，五份 TRX 预期共 487 项（411 + 3 × 6 + 58）；保留全部构建/打包矩阵、七 RID 45 组图片、七组 S3 和 55 个 artifact。产物审计增加 Mvvm 8.4.2 的平台资产、唯一程序集和官方包字节核对，错误版本/平台、缺失/重复或损坏文件的负例均被拒绝。当前步骤仍待本次提交的完整 PR CI、产物及评审验证，不开始 13b。


步骤 13a 已提交至 `6c92d16692c24554cc15d4642347bdd1090f59c1`，当前 [PR run 34794355248](https://github.com/Jeric-X/SyncClipboard/actions/runs/34794355248) 仍在验证中。迁移后的 Core 再次生成源码并成功构建，0 警告/错误；30 份 ObservableProperty 生成文件共 169 个属性的名称、类型、相等判断、通知/命令调用及 setter 语句逐项静态核对。去除 backing-field 名称与 C# partial 声明差异后，仅 HistoryRecordVM.FilePath 的旧值局部变量可空标注变化，已核对对应回调签名；报告 `/tmp/syncclipboard-stage13a-generated-contract.json`。该检查不执行 ViewModel 或 UI。


### 步骤 13a 最终通过记录

验证通过提交 `6c92d16692c24554cc15d4642347bdd1090f59c1`：[PR run 34794355248](https://github.com/Jeric-X/SyncClipboard/actions/runs/34794355248)、[push run 34794352515](https://github.com/Jeric-X/SyncClipboard/actions/runs/34794352515)、[CodeQL 34794354872](https://github.com/Jeric-X/SyncClipboard/actions/runs/34794354872) 与 CodeFactor 全部成功，共 119 项 SUCCESS、8 项预期发布 SKIPPED。合并测试提交 `032292d1edb9bb712bf220f16fa04baf85122e0f` 的父提交为 master 基线和当前 head。五份 TRX 共 487 项通过（Core 411、三平台 Desktop 各 6、WinUI3 58），0 失败/跳过；实际 CI 包含全部 25 项新增 MVVM 用例，Windows 测试日志无新增编译警告。

55 个 artifact 全部下载且精确匹配当前 run/head 及既有清单。38 个 Windows/Linux 组合（24 Windows、14 Linux）逐包通过唯一 MVVM 8.4.2 程序集及正确平台官方资产检查，保留全部前置依赖、原生库、架构、运行时及资源检查。报告 `/tmp/syncclipboard-pr419-6c92-package-audit.json`，SHA256 `06069da7176f123c1b65023b7c0aba82fd3b5437dec7f368c0b23f4b6da16671`。

六份 Linux deb/rpm 元数据与 macOS 双架构静态审计通过，每个 DMG 检查严格签名、19 个 dylib、通知注册器改写及全部前置依赖；MVVM 使用官方 net8.0 资产，挂载均已卸载。七 RID 45 组图片及七组 S3 检查通过。服务器/双架构容器 CI 各四轮 Production 启动和两轮 API 冒烟通过，下载 Server 产物本机复验相同。

Codex 于 `2026-09-14T01:04:27.489345Z` 完成本 head 评审，无新增意见；八个线程全部解决，行内反馈无变化，普通评论仅更新评审状态。完整本地记录 `docs/ai_design/.local/PR-419-Artifacts-6c92d166.md`。步骤 13a 非 UI 验证通过，允许进入 13b；整体升级尚未完成，不执行 UI 验证。


## 步骤 13b：ObservableCollections 兼容性验证

2026-09-14 官方实时 NuGet 索引和[官方发布](https://github.com/Cysharp/ObservableCollections/releases/tag/v3.3.4)确认 3.3.4 仍是最新稳定版，保留当前中央版本，不虚构升级。包源码提交 `094f7df69f4b2f805788f14f9d389694f687b1a1`，官方 nupkg SHA256 `ff848f9936b3ad0705e485bf913d196320f03825803c6985515cb00abbc3831b`；提供 net6.0/net8.0/netstandard2.0/netstandard2.1 资产，当前使用的 net8.0 无传递依赖。六份前置依赖图冻结于 `/private/tmp/syncclipboard-stage13b-baseline-assets/`。

实际使用位于 HistoryViewModel 的 ObservableList → CreateView → ToNotifyCollectionChanged 路径，以及过滤、插入/删除和历史选择逻辑。下一步审查通知、筛选和释放语义并补齐不初始化实际 HistoryViewModel/UI 的集合与可隔离逻辑回归，再完成独立 PR CI/产物/评审门槛。本步源码核对同时清理此前属性迁移时误复制到上一行末尾的四处重复注释；原位置注释保留，不改变代码行为。步骤 13b 尚未通过，不进入 14。


### 步骤 13b 本地实现与测试范围

新增 16 项集合回归，使用项目的纯 HistoryRecord 和 HistoryRecordKey，通过 ObservableList → CreateView → ToNotifyCollectionChanged 路径验证过滤后的增删替换索引、身份及选择键保持、两页数据添加的单项通知/Count、范围删除、两种历史时间排序与反转、实际静态删除选择目标、清空重载及泛型/非泛型 IList 访问。同步上下文替身会拒绝任何 Post/Send；另有队列式假调度器检查延迟通知载荷，两个 Dispose 用例确认退订边界。没有构造实际 HistoryViewModel 或 UI，选择交互仍范围排除。

首次复合验证命令被自动审批拒绝，理由是未筛选的 Core 测试可能超出非 UI 范围。随后核对全部 38 个 Core 测试类的入口、初始化、清理和替身，对 33 个尚未标注的类补充 NonUI 分类；本地与 CI 均改为正向筛选，并同步 AGENTS.md/CLAUDE.md。[范围清单](Core-NonUI-Test-Scope.md)列出每类依据。筛选后的 Core 427 项全部通过，包含前一阶段全部 411 项和新增 16 项，无缺失、失败或跳过；不是通过减少覆盖绕过审批。原先安全的依赖还原与格式检查单独完成。


### 步骤 13b 本地验证结果

最终 Core NonUI 427、Desktop NonUI 6 项通过，0 失败/跳过；仓库 TRX 校验器分别按最低 427/6 项核定。六份依赖图的 targets/libraries 与步骤开始时完全一致，报告 `/tmp/syncclipboard-stage13b-dependency-diff.json`。33 个既有测试文件逐一比较，改动仅为添加 NonUI 类级分类，没有修改断言或删除用例。

WinUI3、macOS 和 WinUI3 测试项目还原完成；仓库格式检查退出 0（`/tmp/syncclipboard-stage13b-final-format3.log`，仅跨平台工作区加载警告），actionlint 和 diff 检查通过。CI Core 最低数提升至 427，五份 TRX 要求合计 503 项（427 + 3 × 6 + 58）。保留全部 55 个 artifact 和既有构建、打包、图片、S3、服务器与容器检查；产物审计新增 ObservableCollections 3.3.4 唯一程序集和官方 net8.0 资产核对，缺失、重复、损坏、旧版本及错误框架五种负例均被拒绝。当前仍须完成本次提交的完整 PR CI、产物及评审验证，步骤 13b 未通过，不进入 14。


### 步骤 13b 当前提交验证进展

当前提交 `5f40344fb08b946859830fd29ef7ca706e2af6b3`，对应 [PR run 34796991077](https://github.com/Jeric-X/SyncClipboard/actions/runs/34796991077)。五份 CI TRX 共 503 项通过（Core 427、三平台 Desktop 各 6、WinUI3 58），0 失败/跳过；Core 用例与本地 427 项逐项一致，新增 16 项集合回归全部实际执行。Windows NonUI 编译无新增警告，报告 `/tmp/syncclipboard-pr419-5f40-test-audit.json`、`/tmp/syncclipboard-pr419-5f40-inventory-audit.json`。

七 RID 45 组图片和七组 S3 检查通过。服务器与双架构容器的当前 CI 日志均包含四轮 Production 启动及两轮 API 冒烟，下载服务器产物的本机复验也通过。macOS x64 DMG 已只读静态检查并卸载：19 个 dylib、签名、框架及全部既有依赖正确，ObservableCollections 使用官方 3.3.4 net8.0 资产。

Codex 于 `2026-09-14T01:51:27.348465Z` 完成本提交评审；随后读取行内评论无新增或改动，八个线程全部解决。完整 CI、其余产物与最终状态尚待核对，不能据此标记 13b 通过，也不进入 14。监控任务 `pr419` 每 10 分钟检查当前阶段。


### 步骤 13b 最终通过记录

验证通过提交 `5f40344fb08b946859830fd29ef7ca706e2af6b3`：[PR run 34796991077](https://github.com/Jeric-X/SyncClipboard/actions/runs/34796991077)、[push run 34796989467](https://github.com/Jeric-X/SyncClipboard/actions/runs/34796989467)、[CodeQL 34796990798](https://github.com/Jeric-X/SyncClipboard/actions/runs/34796990798) 均完成成功，CodeFactor 成功；最终 119 SUCCESS、8 项预期发布 SKIPPED。合并测试提交 `237fe7a65215efb634588c28ac57dd4d49d093f8` 的父节点为 master 基线和当前 head。

55 个 artifact 全部精确匹配当前 run/head 和既有名称清单。38 个 Windows/Linux 组合（24 Windows、14 Linux）通过全部累计检查，ObservableCollections 唯一程序集为官方 3.3.4 net8.0 资产，既有依赖报告与上一阶段逐项一致。报告 `/tmp/syncclipboard-pr419-5f40-package-audit.json`，SHA256 `c0bb5acd0dbc655bb1bf6b406d5f78d0d145bdef8dddbfa4b264ed2515b398e0`。两个 macOS DMG 只读检查并卸载，每包 19 个 dylib、严格签名与全部依赖正确；六份 Linux deb/rpm 元数据通过。

五份 TRX 共 503 项通过，0 失败/跳过；Core 427 项与本地逐项一致，新增 16 项集合用例全部实际执行。七 RID 45 组图片、七组 S3，以及服务器/双架构容器各四轮启动和两轮 API 检查通过，下载服务器的本机复验同样通过。

Codex 于 `2026-09-14T01:51:27.348465Z` 完成本 head 评审，最终再次核对无新增行内反馈，八个线程全部解决。完整本地记录 `docs/ai_design/.local/PR-419-Artifacts-5f40344f.md`。本阶段监控任务已删除；步骤 13b 非 UI 验证通过，允许进入 14，整体升级尚未完成。


## 步骤 14：Quartz 4.1.0 迁移

2026-09-14 读取官方 NuGet 实时索引及 nupkg，Quartz 与原 DI 包最高稳定版本均为 4.1.0；搜索缓存仍显示较早版本，以实际包为准。Quartz nupkg SHA256 `f31c8502bd15ca9a887e92014fcd3fc93c3c494bdb20da5016856a623a89a00e`，源码提交 `c4184eb558b6ec1856b66947fcc1e3fa4a40c2fc`，仅提供 net10.0 资产。DI 包元数据明确功能已并入 Quartz，本身为空迁移包，因此移除独立 PackageReference 和中央版本，主包升至 4.1.0。依据：[NuGet 实时索引](https://api.nuget.org/v3-flatcontainer/quartz/index.json)、[4.x 迁移指南](https://www.quartz-scheduler.net/documentation/quartz-4.x/migration-guide.html)。

六份升级前依赖图保存在 `/private/tmp/syncclipboard-stage14/baseline-assets/`。实际注册六个任务：AppdataFileDelete/Update 每 24 小时，HistoryCleanup 每分钟，DeletedHistoryDataCleanup 每 5 分钟，OrphanedHistoryCleanup/LocalFileCacheCleanup 每 6 小时，均从启动时首次触发。需适配 ValueTask 任务接口、DI 与生命周期，并以隔离宿主和替身验证。UpdateJob 的真实 UI 调度和更新流程仍范围排除；测试不得操作真实应用数据目录。当前步骤未通过，不进入 15。


### 步骤 14 首轮接口适配

升级后的首次 Core 编译发现六个 IJob 的签名不兼容。已按精确包源码改为 `ValueTask Execute(IJobExecutionContext, CancellationToken)`，取消令牌传入原有历史清理调用及 Task.Run；更新和缓存任务在开始前检查取消。Quartz 4 的 AddQuartz 已注册默认 IScheduler，移除 AppCore 中旧的手工工厂注册，避免覆盖新版容器注册。原有六个任务的触发间隔尚未改变。

适配后 `dotnet build src/SyncClipboard.Core -c Release --no-restore` 退出 0，0 警告、0 错误，日志 `/tmp/syncclipboard-stage14-adapted-build.log`。这仅证明编译；调度注册中未等待的异步调用、生命周期与取消行为仍需专项核对，尚未执行本阶段完整非 UI 测试、格式检查、其他平台还原、产物及 PR 验证，步骤 14 未通过。


### 步骤 14 注册与生命周期回归

现有调度注册丢弃异步返回值，可能在注册未完成时启动；改为逐项等待六个注册和 Start，失败传回调用者。为每个任务和触发器设置稳定标识，并以调度器实例级锁保护初始化；已存在任务不重新排期，部分注册失败后可继续，重复或并发初始化不产生重复任务。六个间隔和 StartNow 行为保留。AppCore 退出改为等待容器异步释放，兼容 Quartz 4 的 IAsyncDisposable；保留只实现 IDisposable 的容器后备路径。

新增 11 项 NonUI 测试：生产注册元数据/时机、并发注册等待、失败恢复、初始化取消、等待锁期间取消、启动失败传播；真实隔离调度器验证 Core DI 默认代理、工厂实例、重复启动、作用域释放、失败后再次触发、Interrupt 令牌和优雅关闭等待。真实调度器只执行内存探针；六个生产任务不会在这组测试中实际执行。

首轮 8 项中三项断言误把新版延迟代理当作工厂实例，或在代理关闭后读取状态导致其尝试重新解析。核对精确源码后，断言分别检查代理稳定性、实际调度器身份和实际关闭状态，没有删除失败用例或降低生命周期要求。最终专项 11 项全部通过，完整 Core 438、Desktop NonUI 6 项通过，0 失败/跳过，报告 `/tmp/syncclipboard-stage14-current-results/`。实际清理任务的临时目录验证、依赖图、格式与 PR 仍待完成，不进入 15。


### 步骤 14 当前静态检查与覆盖证据

Windows、macOS 和 WinUI3 测试项目还原通过。六份依赖图均移除 Quartz 3.14.0 / 独立 DI 3.14.0，新增 Quartz 4.1.0 及其五个 10.0.0 传递依赖：Diagnostics.Abstractions、Diagnostics.HealthChecks.Abstractions、Diagnostics.HealthChecks、FileProviders.Abstractions、Hosting.Abstractions；其余共有包的版本及完整元数据保持一致。报告 `/tmp/syncclipboard-stage14-current-dependency-diff.json`。

格式检查首次要求简化集合初始化和显式传入测试取消令牌，已修正且未屏蔽诊断；清理阶段保留独立的 10 秒有界等待。复核格式退出 0（`/tmp/syncclipboard-stage14-format2.log`，仅跨平台工作区加载警告），actionlint 与 diff 检查通过。修正后的 11 项 Quartz 专项再次通过（`/tmp/syncclipboard-stage14-quartz-results4/`）。当前 Core 438 与上一阶段 427 项逐项对比，无遗漏且新增 11 项全部为 Quartz 回归；CI 最低数已调整为 438，五份 TRX 预期共 514 项。

已准备 Quartz 4.1.0 net10.0 官方资产及旧 DI 程序集/依赖缺席的产物规则。官方 DLL SHA256 `c733959ad54e48c4d9e28e8ea2b4f3eeea53c94ff1fd858b89941a0e75ff1343` 与本地测试宿主实际文件一致；缺失、重复、大小写别名、损坏、旧版本、旧 DI 文件、旧 DI 依赖七种负例均被拒绝，报告 `/tmp/syncclipboard-stage14-auditor-check.json`。这些规则尚未替代本阶段真实 PR 产物检查。仍须补齐实际清理任务的独立临时目录验证后再提交当前步骤，不进入 15。

### 步骤 14 实际任务隔离验证与取消修复

新增 `build/verification/QuartzProbe` 及 `quartz_jobs_smoke.py`。包装脚本先将探针复制到新建的临时目录，写入 PortableAppDataFolder/PortableUserConfig，再启动独立进程；探针核对可执行目录和便携数据根后才创建服务。结束后删除临时目录，不运行桌面入口。两项负例分别关闭一个便携配置，均在初始化数据前拒绝执行，报告 `/tmp/syncclipboard-stage14-probe-isolation-negative.json`。

10 组检查覆盖实际六个任务：旧临时文件/日志/dump/更新包清理，过期历史与关联目录清理及收藏/置顶/同步/近期记录保留，软删除记录与传输文件清理，过期孤立目录和缓存孤立记录清理，更新开关、正确回调交接、替身调度失败传播及恢复，以及重复清理的幂等性。更新回调不执行，通知、窗口和 HTTP 均为严格替身。六个任务的预取消均验证数据保留。目录年龄由临时目录的时间元数据设置并断言，不跳过年龄检查。

首轮发现 HistoryCleanupJob 调用的 CleanupExpiredHistory 吞掉 OperationCanceledException。修复为入口检查取消，并将请求取消产生的异常重新抛给调度器；其他异常仍按原逻辑记录。增加确定性的等待中取消验证：探针仅通过反射取得实际托管数据库信号量，持锁后启动清理，确认任务等待，再取消并在 10 秒内观察异常，最后释放锁；历史数据保持不变，后续正常清理成功。报告 `/tmp/syncclipboard-stage14-production-jobs-final.json` 的 10 组检查、六项预取消和等待中取消均通过，Quartz 程序集版本 4.1.0.0，SHA256 与官方资产相同。这里未声称所有同步文件清理都能在执行中即时中断，也未执行真实 UI 更新流程。

最终 Core NonUI 438、Desktop NonUI 6 项全部通过，0 失败/跳过；TRX 位于 `/tmp/syncclipboard-stage14-final-results/`，仓库报告校验器按 438/6 项验证通过。最终探针构建 0 警告/错误，探针格式检查退出 0，仓库格式检查退出 0（仅跨平台工作区加载警告），actionlint 与 diff 检查通过。

三平台 Desktop CI 串行增加探针构建和相同包装命令，报告写入各自 `TestResults/desktop/quartz-job-result.json` 并随原 artifact 上传，仍保留 55 个 artifact 和五份合计 514 项的 TRX 门槛。当前仍待本阶段提交的完整 PR CI、三平台探针、所有产物及评审通过，步骤 14 未通过，不进入 15。

### 步骤 14 首次 PR 与探针结构修复

提交 `cf3728d2f4f3fba0345faacc5b80e9a6f76b212f` 已推送至 PR 419，[PR run 34801582104](https://github.com/Jeric-X/SyncClipboard/actions/runs/34801582104) 已确认三平台 Desktop 非 UI 测试与实际任务探针成功，其余检查仍在进行。八个既有评审线程均已解决，初次读取无新增行内反馈；当前提交的新评审尚未完成。监控 `pr419` 每 10 分钟检查当前阶段。

CodeFactor 报告新增探针顶层长方法复杂度 46。已按任务拆为命名验证方法，单独封装任务执行/取消助手和严格更新调度替身；不关闭规则、不删除用例。源码比较确认全部字符串常量与 Check 表达式保留，报告 `/tmp/syncclipboard-stage14-codefactor-contract.json`。重构后构建 0 警告/错误，探针格式检查通过；实际 10 组检查、六项预取消、等待中取消及 Quartz 版本/哈希报告与重构前逐项完全相同，报告 `/tmp/syncclipboard-stage14-codefactor-jobs.json`。两项便携隔离入口负例再次通过。后续须由修复提交重新完成全部 PR 门槛，不能沿用旧 head 结果宣告当前阶段通过。

### 步骤 14 中断测试清理竞态修复

结构修复提交 `0f8a6af9f61d9105f3212c240c0d9bdadf80ed50` 的 CodeFactor 已成功。随后 [Core CI 103847953719](https://github.com/Jeric-X/SyncClipboard/actions/runs/34802546303/job/103847953719) 出现 437 项通过、1 项失败：中断测试已完成首次 Interrupt 和令牌取消断言，但 finally 再次中断正在结束的任务，Quartz 对已释放取消源调用 Cancel，抛出 ObjectDisposedException。

删除清理阶段多余的第二次 Interrupt；首次中断返回值、实际任务令牌取消、作用域释放和有界关闭等待的断言全部保留。独立清理取消源仍链接到任务，确保断言失败时也能退出。没有吞掉异常或重试掩盖问题。修复后 Core 438 项通过，Quartz 11 项专项连续 10 轮共 110 项通过，均无失败/跳过；TRX 校验器按 438/110 项核定，证据 `/tmp/syncclipboard-stage14-interrupt-fix-results/core/` 和 `/tmp/syncclipboard-stage14-interrupt-repeat/`。仓库格式检查退出 0（仅跨平台工作区加载警告），diff 检查通过。

本次同步用户最新要求：取消远程编译产物下载及二进制审计，信任当前 head 的 CI 构建/打包结果；不再保留 55 份 artifact 下载、38 个组合和 macOS DMG 审计门槛。必要的额外二进制检查只针对本地编译的 macOS 版本。非 UI 功能测试、CI 结果和评审仍须独立通过，步骤 14 尚未完成，不进入 15。

修复提交 `356cf8e7f951cef3a1286158d275892740bbfcc9` 的 [Windows Core CI 103849784068](https://github.com/Jeric-X/SyncClipboard/actions/runs/34803186971/job/103849784068) 已成功；实际日志确认 438 项通过、0 失败/跳过，包含先前失败的中断回归，证据 `/tmp/syncclipboard-pr419-356c-core-success.log`。其他 CI 及新评审仍在进行，当前阶段尚未通过。没有下载编译产物或执行 UI 验证。

### 步骤 14 PR 检查与评审证据

同一提交的 [PR run 34803186971](https://github.com/Jeric-X/SyncClipboard/actions/runs/34803186971) 已完成，58 个任务全部成功；[CodeQL 34803186474](https://github.com/Jeric-X/SyncClipboard/actions/runs/34803186474) 成功。测试日志确认 Core 438、三平台 Desktop 各 6、WinUI3 58，合计 514 项通过，均无失败/跳过。三平台日志另确认各自 Quartz 10 组隔离任务检查、六项取消入口且无 UI 执行，汇总 `/tmp/syncclipboard-pr419-356c-desktop-evidence.json`。CI 构建和打包产物按用户要求直接信任，没有下载编译产物进行二进制验证。

Codex 于 `2026-09-14T03:43:24.082263Z` 完成本提交评审，八个线程全部解决，行内反馈与本阶段开始时一致，无新增问题。推送工作流 [34803183949](https://github.com/Jeric-X/SyncClipboard/actions/runs/34803183949) 仍在运行，当前阶段尚待它完成，不能提前进入步骤 15。

### 步骤 14 最终通过记录

验证通过提交 `356cf8e7f951cef3a1286158d275892740bbfcc9`：[PR run 34803186971](https://github.com/Jeric-X/SyncClipboard/actions/runs/34803186971)、[push run 34803183949](https://github.com/Jeric-X/SyncClipboard/actions/runs/34803183949)、[CodeQL 34803186474](https://github.com/Jeric-X/SyncClipboard/actions/runs/34803186474) 均已完成成功，CodeFactor 成功。最终 119 项 SUCCESS、8 项预期发布 SKIPPED；跳过的是 GitHub 草稿/正式发布、Server/macOS/Linux/Windows 发布及 Homebrew/winget 提交，不属于本次构建测试门槛。PR mergeStateStatus 为 CLEAN，八个线程全部解决，行内反馈无新增，当前 head 的 Codex 评审已完成。

514 项非 UI 测试、三平台各 10 组 Quartz 临时数据检查及全部必要构建/打包 CI 已通过。按最新范围直接信任 CI 产物，没有下载编译产物进行二进制审计，也未执行 UI 验证。最终记录 `/tmp/syncclipboard-pr419-356c-complete.json`、`/tmp/syncclipboard-pr419-356c-push-complete.json` 及各测试日志。监控 `pr419` 已删除，步骤 14 通过，允许进入 15；整体升级尚未完成。

## 步骤 15：Swagger / OpenAPI（进行中）

Swashbuckle.AspNetCore 8.1.1 → 10.2.3，Microsoft.OpenApi 显式固定 2.12.2，版本集中写入 `src/Directory.Packages.props`。上游 Swagger 10.2.3 依赖 OpenAPI >= 2.7.5；使用 v10 迁移说明对应的 2.x API，未混入 Microsoft.OpenApi 3.10.2 主版本迁移。ApiDescription.Server 随 Swashbuckle 从 6.0.5 升为 10.0.0。元数据和升级前 11 份依赖图保存在 `/private/tmp/syncclipboard-stage15/`，不下载远程编译产物审计。

适配 OpenAPI 新命名空间、只读模型接口、JsonSchemaType 和引用类型。两个过滤器使用公开的复制 API 保留原有文档信息；查询参数的 properties、required、encoding 同步采用不受当前文化影响的小驼峰名称。补全已有上传接口的 hasData、可选传输数据哈希头与 HTTP Basic 认证声明，不改变实际认证方式及接口行为。文档格式保持 OpenAPI 3.0。

新增 Swagger JSON 契约检查接入现有服务器冒烟：核对 12 条路径及方法、上传字段、查询字段、枚举、引用及认证。另以仅引用 Server.Core 的内置服务器探针检查独立服务 Development/Production 与内置服务 Production 诊断开关；只访问 HTTP API 和 Swagger JSON，使用临时配置/数据库、回环端口及随机账号密码，不启动 UI，也不访问 Swagger UI。通过实际表单请求核对必填参数、查询筛选与传输数据回读。

本地验证：Core NonUI 438、Desktop NonUI 6 项全部通过，0 失败/跳过，TRX 位于 `/private/tmp/syncclipboard-stage15-results/`；四种 Swagger HTTP 模式通过，记录 `/tmp/syncclipboard-stage15-diagnostic-smoke3.log`。现有同步/文件/重启冒烟两轮与 Production 首次启动配置四轮通过。服务器与探针构建均无警告/错误，平台依赖还原、仓库及探针格式检查退出 0（仓库仅有跨平台工作区加载警告），actionlint 和 Python 语法检查通过。

11 个项目的依赖图比较确认：Shared 无变更，其余仅改变四个 Swashbuckle 包、Microsoft.OpenApi 及 ApiDescription.Server，报告 `/tmp/syncclipboard-stage15-dependency-diff.json`。检查探针初次运行暴露了测试配置遗漏和文本 size 使用字节数的问题，已改为预写临时配置及文本字符数，并完成上述四模式实测；没有为适应测试修改产品协议或放宽断言。

当前步骤本地检查已通过，尚待提交后的 CI 与评审验证；不进入 16a。

### 步骤 15 PR 验证与 ZIP 恢复测试修复

提交 `5564ef7601fb8e17aafb2e1219f108cb4ecd3816` 已推送。服务器 CI `103857250501` 实际执行四种 Swagger 模式及既有同步回归并成功，双架构容器也成功。但 [Windows Core CI](https://github.com/Jeric-X/SyncClipboard/actions/runs/34805772715/job/103857250458) 有 437 通过、1 失败：原有压缩包恢复用例错误地要求重新生成 ZIP 与原包字节哈希相同。ZIP 条目默认携带生成时间，因此相同资料内容并不保证压缩包字节相同。

先将原包条目固定为 2000 年时间戳，保留原断言，在本地确定性复现相同失败，报告 `/private/tmp/syncclipboard-stage15-zip-repro/`。随后保留新包实际 SHA256、返回值和数据库记录一致的检查，并改为验证文件名/内容与资料标识保持不变、新包字节哈希正确更新；缺失、损坏、不可读三个场景均保留。不修改产品协议，不下载远程编译产物，后续仍须由修复提交完成 CI 和评审。

当前提交的 [P1 评审](https://github.com/Jeric-X/SyncClipboard/pull/419#discussion_r4002220775) 指出正常退出同步等待 Quartz，而 UpdateJob 可能等待主线程继续更新，形成死锁。将 AppCore 退出改为缓存的一次性 StopAsync，两个桌面前端与实例关闭命令异步等待；调度器先完成关闭，再释放其余应用服务。桌面退出入口防止重复执行。事件循环已经异常结束的两个 Avalonia 程序入口仅做最多五秒的尽力清理，不保留无限同步等待。

既有优雅关闭测试改为直接调用生产关闭/释放方法，使用纯内存任务的开始与完成信号验证等待不会阻塞调用者以及最终释放。真实桌面退出、更新 UI 回调仍按用户要求排除，不执行，也不宣称已验收。

修复后的 Core 438 与 Desktop NonUI 6 项通过，0 失败/跳过，报告 `/private/tmp/syncclipboard-stage15-shutdown-final/`。macOS arm64 编译成功，仅有命令行 RID 覆盖项目 RID 的 SDK 提示；仓库格式检查通过，仅有跨平台工作区加载警告。源码核对全部正常退出调用均使用异步等待。

Default 编译还暴露了升级前已存在的 NU1904：其 Windows TFM 经 Toolkit 通知包解析 System.Drawing.Common 4.7.0，冻结的步骤 15 基线已含同一警告。仅在该 Windows TFM 添加显式引用，沿用中央表已有的 10.0.0，与 WinUI3 和 Windows 图片探针对齐；Linux 引用不变，不屏蔽警告。[对应公告](https://github.com/advisories/GHSA-rxg9-xrhp-64gj) 的运行时影响限 macOS/Linux，此处处理的是遗留依赖及还原警告，不据此声称 Windows 运行时存在同一漏洞。

修复后实际依赖图中 Windows 使用 System.Drawing.Common / Microsoft.Win32.SystemEvents 10.0.0，net10.0 图没有 System.Drawing.Common；NuGet 日志为空，Default 编译 0 警告/错误、格式检查通过，记录 `/tmp/syncclipboard-stage15-default-drawing-build.log`。所有修复尚待新提交的完整 CI 与评审验证，步骤 15 未通过。

修复提交 `55ea1e44e2e5f111cdaa7d0ac331f938ab71229b` 已推送。[Windows Core CI 103861322854](https://github.com/Jeric-X/SyncClipboard/actions/runs/34807213205/job/103861322854) 成功，实际日志确认 438 项通过、0 失败/跳过，覆盖原失败的 ZIP 恢复用例与生产关闭方法的内存任务验证，证据 `/tmp/syncclipboard-pr419-55ea-core-success.log`。Codex 于 `2026-09-14T04:51:57.711042Z` 完成本 head 评审，无新增行内反馈；原 P1 线程待其余必要平台 CI 确认后解决。其他 CI 仍在运行，步骤 15 未通过，不进入 16a。

三平台 Desktop、WinUI NonUI 任务随后全部成功；Windows Desktop 任务 `103861322877` 中替代 Windows 入口编译成功，WinUI 任务 `103861322865` 中产品/测试编译及非 UI 覆盖检查成功。基于这些结果及新评审无新增问题，P1 线程 `PRRT_kwDOBXBc9s6h_X-t` 已标记解决，没有发送评论。Windows ARM64 构建及部分 Linux 打包仍在运行，完整阶段门槛尚未通过。

### 步骤 15 最终通过记录

验证通过提交 `55ea1e44e2e5f111cdaa7d0ac331f938ab71229b`：[PR run 34807213205](https://github.com/Jeric-X/SyncClipboard/actions/runs/34807213205) 的 58 个任务全部成功，[push run 34807210688](https://github.com/Jeric-X/SyncClipboard/actions/runs/34807210688) 的 58 个任务成功、8 个发布任务预期跳过；[CodeQL 34807212934](https://github.com/Jeric-X/SyncClipboard/actions/runs/34807212934) 及 CodeFactor 成功。最终 119 SUCCESS、8 SKIPPED，PR 为 CLEAN；新评审完成且无新增问题，9 个线程均解决。跳过项仅为草稿/正式发布、平台发布及 Homebrew/winget 提交。

日志确认 Core 438、三平台 Desktop 各 6、WinUI 58，共 514 项非 UI 测试通过，0 失败/跳过，三平台 Quartz 探针各 10 组检查通过。服务器与容器的 Swagger 契约、四模式 HTTP 验证及必要平台构建/打包均已通过。信任 CI 编译产物，没有下载远程二进制，也未执行 UI 验证。最终证据 `/tmp/syncclipboard-pr419-55ea-complete.json`、`/tmp/syncclipboard-pr419-55ea-pr-complete.json`、`/tmp/syncclipboard-pr419-55ea-push-complete.json` 及测试日志。监控 `pr419` 已删除，允许进入步骤 16a；整体升级尚未完成。

## 步骤 16a：Test SDK 与 MSTest（进行中）

官方 NuGet 索引核定 Microsoft.NET.Test.Sdk 18.10.0、MSTest.TestAdapter/TestFramework 4.4.0 为最新稳定版，分别从 17.14.1 / 3.10.4 升级。元数据、升级前 11 份依赖图及本地测试报告保存在 `/private/tmp/syncclipboard-stage16a/`。精确源码提交为 VSTest `5b0c2fc69de12eb4383a7b6b38acab164a2f68fb`、MSTest `a81dff85c81f88b1cd0421619225b1037341d201`。coverlet 6.0.4 与 Moq 4.20.72 未改动。

首次编译无警告/错误，原 Core 438 与 Desktop 6 项直接通过，用例名称逐项无增减。但仓库格式检查发现新版分析器要求迁移 138 处 TestContext 取消令牌访问、集合断言及一处 catch 内断言。仅使用相应官方修复器并人工核对语义，不关闭规则、不删除用例。集合比较保持数量、顺序和成员要求；异常用例改为显式捕获并检查异常，仍拒绝 LocalProfileDataUnavailableException，只接受 IO/权限错误。四处字节数组比较因新增 Span 重载不能跨 await 保存参数，改为先读取文件再比较；没有改变文件内容或预期结果。

三套测试的实际 MSBuild 属性均为 EnableMSTestRunner=false、IsTestingPlatformApplication=false，UseWinUI/UseUwpTools 未启用，adapter/framework 均选择普通 net9.0 平台服务目录。继续使用 VSTest；MSTest 依赖携带的 MTP 包不代表启用了 MTP。自定义 DI DataSource 仍仅返回 Type 元数据，混合 UI DI 执行继续排除。产品依赖图未改变，仅三套测试图更新测试框架、TestHost/ObjectModel/CodeCoverage 18.10.0 与 Testing.Platform 2.4.0 等依赖，移除旧 AdapterUtilities/VSTestBridge，报告 `/tmp/syncclipboard-stage16a-dependency-diff.json`。

适配后 Core 438、Desktop 6 项全部通过，0 失败/跳过；最终 TRX 位于 `/private/tmp/syncclipboard-stage16a-ready/`，仓库报告校验器通过，用例名称多重集与升级前逐项一致，未增删测试。报告 `/tmp/syncclipboard-stage16a-ready-inventory.json`。WinUI 的两处通知断言仍只比较 XML 数据；Windows 编译和 58 项非 UI 测试交由当前提交 CI 验证。

格式工具的新 CS0103 输出源于 MSTest 4 的 [ClassCleanupBehavior 迁移修复器](https://github.com/microsoft/testfx/blob/a81dff85c81f88b1cd0421619225b1037341d201/src/Analyzers/MSTest.Analyzers.CodeFixes/RemoveClassCleanupBehaviorArgumentFixer.cs)：它注册此编译诊断，导致格式工具收集 macOS 设计时工作区缺少 WinUI/资源生成代码的错误。本仓库没有待迁移的 ClassCleanupBehavior；格式命令仅增加 `--exclude-diagnostics CS0103`，实际平台构建仍强制检查 CS0103，没有修改 NoWarn、编译器或 MSTest 规则。CI 与 AGENTS/CLAUDE 同步该命令，完整格式检查退出 0，仅有跨平台工作区加载警告；actionlint 与 diff 检查通过。这项 CI 调整也必须由本阶段 PR 验证。

当前阶段本地检查已完成，提交后的全部 CI 与评审尚待通过，不进入 16b。


### 步骤 16a 补修：S3 上传完成标记

首轮提交 `5a61a2e8ff0d25120f3b09abbf30ffc79bd3e340` 已推送，监控 `pr419` 每 10 分钟检查。复查完整评审正文发现，步骤 15 的 `2026-09-14T04:31:10Z` 评审除退出 P1 线程外，正文还包含 S3 中间 EOF 提前报告完成的 P2；此前只核对行内线程而漏处理这项正文反馈。因此步骤 15 的收尾记录不能代表这项既有反馈已解决，本阶段先补修，整体升级仍未完成。后续必须同时核对 reviews 的正文、行内线程和普通评论。

原 ProgressReadStream 在 SDK 签名或重试的源流 EOF 设置 End=true，上传方法在 PutObjectAsync 成功后又报告一次完成。现有 S3 探针增加成功完成只报告一次、失败/取消不报告完成的检查；原实现已在空文件上传中复现，日志 `/tmp/syncclipboard-stage16a-s3-repro.log`。修复移除源流 EOF 完成报告，保留成功请求后的最终报告、单调有界进度和现有取消行为。验证仅使用本地编译的协议探针、源码构建的 MinIO、临时文件与回环 HTTP；不运行通知 UI，不下载或审计远程编译产物。修复后的本地结果及新提交 CI 尚待完成。

修复后七组真实 S3 协议检查通过，包括普通/空文件/32 MiB/并行上传、两次 500 后重试成功、持续失败与取消；新增完成标记断言全部通过。证据 `/tmp/syncclipboard-stage16a-s3-fixed.json`。Core 438 项再次通过，0 失败/跳过，仓库格式检查退出 0。额外单独检查 S3Probe 格式时发现其原有 token 命名、CreateAdapter 可静态化和 JsonSerializerOptions 缓存三项提示，均不在本次改动中；该探针不属于仓库格式 CI 的解决方案范围，不据此声称此额外检查通过，也不作无关格式改动。实际探针编译无警告/错误。修复提交的 CI 与完整评审仍待通过，不进入 16b。

修复提交 `cd132345e47e48f2f5b76984e6b7eb43067aa632` 已推送；当前 [PR run 34810664267](https://github.com/Jeric-X/SyncClipboard/actions/runs/34810664267)、[push run 34810660034](https://github.com/Jeric-X/SyncClipboard/actions/runs/34810660034) 和 CodeQL 34810663794 正在验证。已取消被替代的 5a61a2e8 两个旧构建运行以释放资源，旧取消结果不作为当前提交通过证据。当前格式任务 103871188881 成功，Windows Core 103871189031 的日志确认 438 通过、0 失败/跳过，S3 103871189039 日志确认七组通过，包括新增完成标记断言。证据 `/tmp/syncclipboard-pr419-cd13-style.log`、`/tmp/syncclipboard-pr419-cd13-core.log`、`/tmp/syncclipboard-pr419-cd13-s3.log`。其余必要 CI 与当前提交评审尚未完成，步骤 16a 未通过，不进入 16b。监控 `pr419` 保持每 10 分钟检查。


### 步骤 16a 评审核对：TRX completed 字段

[评审 P1](https://github.com/Jeric-X/SyncClipboard/pull/419#discussion_r4002527532) 认为成功的 VSTest 报告应含 completed=total，因此要求放宽校验。当前实际 Core438、三平台 Desktop6 的 CI 均通过原校验器，本地 TRX 的 completed 均为0。进一步核对所用 VSTest 精确源码：[TestRunSummary 构造函数](https://github.com/microsoft/vstest/blob/5b0c2fc69de12eb4383a7b6b38acab164a2f68fb/src/Microsoft.TestPlatform.Extensions.TrxLogger/ObjectModel/TestRunSummary.cs#L104) 显式将 completed 写为0；[TestOutcome 定义](https://github.com/microsoft/vstest/blob/5b0c2fc69de12eb4383a7b6b38acab164a2f68fb/src/Microsoft.TestPlatform.Extensions.TrxLogger/ObjectModel/TestOutcome.cs#L84) 将 Completed 与 Passed 分开，前者不代表已判断成功。ResultSummary 的 outcome=Completed 与 Counters.completed 含义不同，现有校验器已接受前者。该反馈不适用于本项目实际 VSTest 报告，不修改测试门槛。当前 WinUI CI 与完整阶段仍待完成；线程 PRRT_kwDOBXBc9s6iAJJ4 待核对后解决，不发送评论。

当前 WinUI 58 项与其 TRX 校验均已通过，日志 `/tmp/syncclipboard-pr419-cd13-winui.log`；至此 Core438、三平台 Desktop 各6、WinUI58 共514项非UI测试全部成功、0失败/跳过。三平台 Quartz 探针各10组及六项取消入口也通过。TRX评审线程 PRRT_kwDOBXBc9s6iAJJ4 已依据精确官方源码和三套测试实际CI证据标记解决，GraphQL确认 isResolved=true；未发送评论，未修改通过条件。其余构建/打包和完整阶段CI仍待完成。


### 步骤 16a 最终通过记录

验证通过提交 `cd132345e47e48f2f5b76984e6b7eb43067aa632`：[PR run 34810664267](https://github.com/Jeric-X/SyncClipboard/actions/runs/34810664267) 的58项任务全部成功，[push run 34810660034](https://github.com/Jeric-X/SyncClipboard/actions/runs/34810660034) 的58项成功、8项发布任务预期跳过；CodeQL 34810663794和CodeFactor成功。最终119 SUCCESS、8 SKIPPED，PR状态CLEAN。当前评审于2026-09-14T05:53:48.290271Z完成，TRX误报已按官方源码及实际测试证据处理；10个线程全部解决，没有未处理的评审正文/行内/普通评论事项。

514项非UI测试、三平台Quartz探针、S3七组协议及其新增完成标记断言通过；各平台构建和打包成功，未下载或复用远程产物做二进制审计，UI验证实际执行0。证据 `/tmp/syncclipboard-pr419-cd13-complete.json`、`-pr-complete.json`、`-push-complete.json`、`-final-runs.json`、`-final-threads.json` 及各测试日志。监控pr419已删除，允许进入16b；整体升级仍未完成。


## 步骤 16b：coverlet 覆盖率采集（进行中）

官方NuGet索引于2026-09-14核定coverlet.collector最新稳定版10.0.1，从6.0.4升级；精确源码提交d21b5b6a08d48f51405ba2c5c5660f91a565776d，元数据与升级前11份依赖图保存于 `/private/tmp/syncclipboard-stage16b/`。官方10系列新增.NET10支持；现有SDK10.0.302和TestSDK18.10.0满足其VSTest要求。继续使用VSTest collector，没有加入coverlet.MTP/msbuild或改变测试宿主。三份测试依赖图仅coverlet.collector版本变化，八份产品依赖图不变；Moq留待16c。

旧6.0.4和新10.0.1均使用同一runsettings采集Core438、Desktop6，全部通过且测试名称逐项无增减、0失败/跳过。新旧Core报告均命中4165/16500行，Desktop报告均命中205/21211行；这些是各测试项目报告内的产品代码行数，不跨报告相加，也不代表UI覆盖。最终TRX/Cobertura位于 `/private/tmp/syncclipboard-stage16b-final/`，对比报告 `/tmp/syncclipboard-stage16b-comparison.json`。

新增runsettings仅纳入SyncClipboard产品程序集、排除测试程序集；覆盖率采集不改变NonUI测试筛选及初始化/清理范围。CI在现有Core、三平台Desktop、WinUI测试命令上启用采集，保留原438/6/58计数校验；另检查报告存在、有效非零覆盖，以及Core/Shared/Server.Core、Desktop或WinUI产品程序集有实际命中。WinUI产品程序集名为SyncClipboard，明确用此名称检查。VSTest可能保存原始附件和部署副本，逐份校验但不累加重复副本。

报告校验器通过真实基线/新报告正例与六种负例：零命中汇总、缺少目标程序集、目标程序集无命中、非法汇总、损坏XML、缺失报告。使用既有defusedxml读取，不引入依赖。WinUI依赖还原、仓库格式、actionlint和diff检查通过；实际Windows/三平台采集必须由本提交PR验证。本地没有启动UI，也没有下载编译产物。本阶段尚未提交并完成CI/评审，不进入16c。

步骤16b提交 `022165a9ada2bf9435f357315a7ed51a1f96cfe5` 已推送至PR419。[PR run 34812426943](https://github.com/Jeric-X/SyncClipboard/actions/runs/34812426943)、[push run 34812423050](https://github.com/Jeric-X/SyncClipboard/actions/runs/34812423050) 和CodeQL34812426503已启动；首次检查未发现新增反馈，10个旧线程均已处理，本head评审正在运行。监控pr419已按每10分钟创建。必须收齐当前head三套测试的实际采集/报告校验及全部CI、评审结果，尚不进入16c。

当前提交的五个测试任务已全部成功，实际日志确认Core438、三平台Desktop各6、WinUI58，共514项通过、0失败/跳过。新增报告校验均实际运行：Windows Core报告4174/16500行且Core/Shared/Server.Core均有命中；Linux/Windows Desktop报告215/21211行，macOS Desktop205/21211行，均确认Desktop程序集命中；WinUI报告541/36594行，确认产品程序集SyncClipboard命中。报告副本未累加。日志 `/tmp/syncclipboard-pr419-0221-{core,desktop-linux,desktop-macos,desktop-windows,winui}.log`。三平台Quartz10组与六项取消入口同时通过。自动评审于2026-09-14T06:19:08.136562Z完成，无新增正文/行内问题，10个线程均已处理。剩余构建/打包仍在运行，步骤16b尚未最终通过。


### 步骤 16b 最终通过记录

验证通过提交 `022165a9ada2bf9435f357315a7ed51a1f96cfe5`：[PR run 34812426943](https://github.com/Jeric-X/SyncClipboard/actions/runs/34812426943) 的58项任务全部成功，[push run 34812423050](https://github.com/Jeric-X/SyncClipboard/actions/runs/34812423050) 的58项成功、8项发布任务预期跳过。CodeQL34812426503及CodeFactor成功；总计119 SUCCESS、8 SKIPPED，PR状态CLEAN。当前提交评审完成，无新增问题，10个线程均已处理。

514项非UI测试及全部五个测试任务的覆盖率采集/报告校验通过，目标产品程序集都有实际命中；三平台Quartz与其余必要协议/平台构建、打包成功。不下载远程编译产物，UI验证执行0。证据 `/tmp/syncclipboard-pr419-0221-complete.json`、`-pr-complete.json`、`-push-complete.json`、`-final-runs.json`、`-final-threads.json` 和五份测试日志。监控pr419已删除，允许进入16c，整体升级未完成。


## 步骤 16c：Moq 版本与 mock 兼容性（进行中）

2026-09-14 官方NuGet索引和发布页确认Moq4.20.72已是最新稳定版，保留现有版本，不虚构升级。包对应源码959fc5128f868fdcbdbb70dbfca86330980431ba，元数据及11份基线依赖图位于 `/private/tmp/syncclipboard-stage16c/`。三个测试项目均解析lib/net6.0/Moq.dll与Castle.Core5.1.1，由.NET10测试宿主执行；这不是.NET6运行时要求。11份依赖图完全不变。

核对现有mock覆盖：Core的严格IServiceProvider、MVVM初始化、通知键控DI/属性/回调、Quartz调度器及SharpHook提供者；Desktop仅mock已运行hook，验证注册状态及从未启动hook，不抛出事件或执行UI调度；WinUI的UIAutomation契约使用严格接口替身验证属性、返回值与异常，不创建真实COM/UIAutomation实例。通知CallBase仅运行管理会话字典的公共基类，平台Create被mock替换。没有改变MockBehavior、默认返回、调用次数/参数验证或测试分类。

本阶段独立Core438、Desktop6全部通过，0失败/跳过，用例名称与16b逐项一致；覆盖率采集及目标程序集命中检查通过。报告 `/private/tmp/syncclipboard-stage16c-results/` 与 `/tmp/syncclipboard-stage16c-comparison.json`。源代码、测试和CI配置没有变化，本提交只记录版本核定和阶段证据；Windows58项及三平台既有验证仍必须在本阶段提交上通过PR CI。当前尚待提交和全部CI/评审，不进入17a。

步骤16c提交 `a88cb22e9d2cf568db1f2e0052a59f965bde6d68` 已推送。[PR run 34814177471](https://github.com/Jeric-X/SyncClipboard/actions/runs/34814177471)、[push run 34814174076](https://github.com/Jeric-X/SyncClipboard/actions/runs/34814174076) 和CodeQL34814177052已启动；当前head评审正在运行，首次完整反馈检查无新增问题，旧线程均已处理。监控pr419已创建，每10分钟检查。必须当前提交全部CI/评审通过后才进入17a，整体升级仍未完成。

本阶段当前提交的Windows Core438、三平台Desktop各6、WinUI58，共514项NonUI测试全部通过，0失败/跳过；五个测试任务的覆盖率报告校验及三平台Quartz10组/六项取消入口均通过。日志 `/tmp/syncclipboard-pr419-a88c-core-complete.log`、`-desktop-macos.log`、`-desktop-linux.log`、`-desktop-windows.log`、`-winui.log`。首次读取Core日志遇网络EOF，仅重取同一成功任务日志后确认完整，不将网络读取失败视作CI失败。当前提交自动评审于2026-09-14T06:43:14.213652Z完成，无新增问题，10个线程均已处理；其余构建/打包仍待完成，尚不进入17a。


### 步骤 16c 最终通过记录

验证通过提交 `a88cb22e9d2cf568db1f2e0052a59f965bde6d68`：[PR run 34814177471](https://github.com/Jeric-X/SyncClipboard/actions/runs/34814177471) 的58项任务全部成功，[push run 34814174076](https://github.com/Jeric-X/SyncClipboard/actions/runs/34814174076) 的58项成功、8项发布任务预期跳过；CodeQL34814177052与CodeFactor成功。总计119 SUCCESS、8 SKIPPED，PR状态CLEAN，当前评审完成无新增问题，10个线程均已处理。

514项NonUI测试、五个任务的产品代码覆盖率校验、三平台Quartz及其他必要协议/平台构建/打包全部通过。不下载或复用远程编译产物做二进制审计，UI验证执行0。最终证据 `/tmp/syncclipboard-pr419-a88c-complete.json`、`-pr-complete.json`、`-push-complete.json`、`-final-runs.json`、`-final-threads.json` 及测试日志。监控pr419已删除，允许进入17a；整体升级未完成。

## 步骤 17a：CodeCracker 维护状态与编译器兼容性（进行中）

2026-09-14 官方 NuGet 索引确认 CodeCracker.CSharp 1.1.0 仍为最新稳定版；仅 WinUI3 产品项目引用，保留版本及原 PrivateAssets/IncludeAssets 配置。官方 v1.1.0 发布于 2018-05-20，仓库未归档，最新提交 9c69a36b7fda87d3eaa087c9e015ae4c5e898865 为 2024-02-13 的 Dependabot 构建依赖更新。包元数据未提供精确源码提交，不把仓库最新提交当作包源码。元数据记录 `/private/tmp/syncclipboard-stage17a/`、`/tmp/syncclipboard-stage17a-{repo,release,last-commit}.json`。

临时 AnalyzerProbe 导入仓库中央包版本和相同 .editorconfig，使用 SDK 10.0.302、net10.0/C# 14，只编译不运行；主构造函数和集合表达式编译成功，故意空 catch 产生预期 CC0004。修复探针后再编译为 0 警告/错误，两个 SARIF 分别只有 CC0004 和无诊断，没有分析器加载/执行异常。首次临时项目把中央包属性导入得过晚，触发 NU1015；改为标准 Directory.Packages.props 导入后还原成功，产品配置未受影响。日志 `/tmp/syncclipboard-stage17a-analyzer-probe2.log`、`-analyzer-fixed.log`。此探针证明当前编译器下可加载并产生诊断，不声称旧分析器覆盖所有 C# 14 语义。

保留原七项 CC0105/CC0001/CC0034/CC0057/CC0061/CC0074/CC0072 排除，没有扩大禁用范围。11 份项目依赖图 targets/libraries 完全不变。仓库格式检查退出 0，仅跨平台工作区加载警告，日志 `/tmp/syncclipboard-stage17a-format.log`；沿用已核实的格式专用 CS0103 排除，实际构建不排除此错误。本阶段只改计划和记录，仍必须通过当前提交 Windows 实际编译、全部 PR CI 和完整评审后才进入 17b。未下载编译产物，UI 验证执行 0。

步骤17a提交 `a80450ccefb522e32e5d24d15814dad76e626e30` 已推送。[PR run 34816306776](https://github.com/Jeric-X/SyncClipboard/actions/runs/34816306776)、[push run 34816303089](https://github.com/Jeric-X/SyncClipboard/actions/runs/34816303089) 和CodeQL34816306347已启动。初次核对完整评审正文、行内/普通评论无新增可处理问题，10个线程均已解决；本head评审仍在运行。监控pr419已创建，每10分钟检查。当前阶段尚未通过，不进入17b。

当前提交已完成的Core438、Linux/macOS Desktop各6以及push工作流WinUI58项NonUI测试均通过，0失败/跳过；相关覆盖率校验通过，Linux/macOS Quartz探针10组及六项取消入口通过。Windows实际编译日志无AD0001/CS8032/CS8784/CS8785分析器加载或执行异常，证据 `/tmp/syncclipboard-pr419-a804-winui-push.log`；其余测试日志为同前缀core、desktop-linux、desktop-macos。当前PR工作流剩余Windows任务、打包和本head评审尚未结束，不能据此通过17a。

PR工作流的五份实际测试日志现已收齐：Core438、三平台Desktop各6、WinUI58，合计514项通过，0失败/跳过；五个任务均通过产品覆盖率校验，三平台Quartz各10组及六项取消入口通过，未出现分析器异常。汇总 `/tmp/syncclipboard-stage17a-ci-tests.json`。状态观察进程曾因GitHub API网络EOF终止；直接API确认同一run仍在运行后恢复只读观察，没有重跑CI。完整构建/打包和评审仍待结束。

步骤17a的PR run34816306776已完成，58项任务全部成功，CodeQL34816306347成功；push run34816303089仍有构建任务运行，本head自动评审仍在运行。再次核对无新增正文/行内反馈、10个线程均已处理，仍不进入17b。

当前head所有CI已终止成功：PR58项成功，push58项成功及8项发布任务预期跳过，CodeQL/CodeFactor成功，汇总119 SUCCESS、8 SKIPPED，PR CLEAN。证据 `/tmp/syncclipboard-pr419-a804-pr-complete.json`、`-push-complete.json`、`-final-runs.json`。自动评审摘要仍显示本提交Running，因此尚不标记阶段最终通过，监控继续。

### 步骤 17a 评审补修：重启前取消运行中的更新任务

自动评审于2026-09-14T07:35:03.929566Z完成，新增[P2线程](https://github.com/Jeric-X/SyncClipboard/pull/419#discussion_r4003049029)：旧实例优雅关闭只等待Quartz任务，更新流程丢失取消令牌，长时间更新会超过替换实例的10秒互斥锁等待，导致重启后没有实例存活。该反馈可从源码和隔离调度器验证，虽然涉及重启入口，仍不执行实际UI或应用重启。步骤17a保持未通过，不进入17b。

新增纯内存任务回归在旧实现复现等待取消超时，`/tmp/syncclipboard-stage17a-restart-repro.log`。核对Quartz4.1.0精确源码c4184eb558b6ec1856b66947fcc1e3fa4a40c2fc的[关闭实现](https://github.com/quartznet/quartznet/blob/c4184eb558b6ec1856b66947fcc1e3fa4a40c2fc/src/Quartz/Core/QuartzScheduler.cs)，使用其ShutdownJobInterruption.WhenWaitingForJobs配置：停止触发后取消运行任务，仍等待任务和作用域安全结束。保留异步关闭，不用10秒超时强行释放仍在运行的服务。

UpdateJob通过IThreadDispatcher新增的取消重载传递Quartz令牌，UpdateChecker.RunAutoUpdateFlow接受该令牌并传给现有SingletonTask/HTTP检查和下载链。取消重载在提交前及回调执行前检查令牌，避免排队后取消的回调开始实际更新。真实UI回调仍范围排除。非UI测试仅主动执行记录令牌的纯委托；生产UpdateJob探针以严格替身核对回调目标、方法和令牌，绝不执行该回调。

修复后15项Quartz专项通过，完整Core442和Desktop6通过，0失败/跳过；原Core438项逐项保留，仅新增关闭取消/释放次序1项及令牌传递/提前取消3项，Desktop用例完全不变。TRX/Cobertura位于 `/private/tmp/syncclipboard-stage17a-fixed-results/`，计数及实际产品程序集命中校验通过；11份依赖图不变，对比 `/tmp/syncclipboard-stage17a-fixed-test-comparison.json`。实际Quartz探针10组、六项取消入口、更新令牌交接通过，`/tmp/syncclipboard-stage17a-fixed-quartz.json`，UI回调执行0。Windows测试还原、仓库与探针格式、actionlint及diff检查通过。

CI Core最低数提升至442，五份测试合计预期518项。修复后的新提交仍须完整CI和评审通过；不能沿用a80450cc的成功结果。该问题已追加本地日志，线程PRRT_kwDOBXBc9s6iBelw待新提交验证后解决，不发送评论。完全信任CI产物，不下载远程编译产物或做二进制审计。

重启取消修复提交 `c8ea1102005bf22ceff965872c5cf7852674a982` 已推送。[PR run34819793133](https://github.com/Jeric-X/SyncClipboard/actions/runs/34819793133)、[push run34819789352](https://github.com/Jeric-X/SyncClipboard/actions/runs/34819789352)、CodeQL34819792612正在运行，本head评审已启动；首次检查无新增问题。11个线程中重启P2仍待新提交验证后解决，其余10个已处理。监控pr419已更新为当前提交和442/6/58门槛，每10分钟；步骤17a未通过，不进入17b。

修复提交的五个CI测试任务现已完成：Windows Core442、三平台Desktop各6、WinUI58，共518项NonUI测试通过，0失败/跳过；覆盖率校验和三平台Quartz10组/六取消入口/令牌交接均通过，汇总 `/tmp/syncclipboard-stage17a-fixed-ci-tests.json`。本head评审于2026-09-14T07:57:29.903077Z完成，无新增正文或行内问题；重启P2线程PRRT_kwDOBXBc9s6iBelw经实际Windows回归及三平台探针证据标记解决，11个线程均已处理，未发送评论。其余构建/打包仍待结束，阶段尚未最终通过。

### 步骤 17a 最终通过记录

验证通过提交 `c8ea1102005bf22ceff965872c5cf7852674a982`：[PR run34819793133](https://github.com/Jeric-X/SyncClipboard/actions/runs/34819793133) 58项全部成功，[push run34819789352](https://github.com/Jeric-X/SyncClipboard/actions/runs/34819789352) 58项成功、8项发布任务预期跳过，CodeQL34819792612与CodeFactor成功。汇总119 SUCCESS、8 SKIPPED，PR CLEAN；本head自动评审于2026-09-14T07:57:29.903077Z完成，无新增问题，11个线程均已处理。

518项NonUI测试、五份覆盖率校验、三平台Quartz及其余必要协议/构建/打包全部通过。完全信任CI产物，未下载或审计远程编译产物，UI验证执行0。最终证据 `/tmp/syncclipboard-pr419-c8ea-complete.json`、`-pr-complete.json`、`-push-complete.json`、`-latest-runs.json`、`-final-threads.json` 与 `/tmp/syncclipboard-stage17a-fixed-ci-tests.json`。监控pr419已删除，允许进入17b；整体升级仍未完成。

## 步骤 17b：容器工具与配置兼容性（进行中）

2026-09-14 官方NuGet索引确认 Microsoft.VisualStudio.Azure.Containers.Tools.Targets 最新稳定版1.23.0，从1.22.1升级；官方变更为增加Podman支持。本项目继续使用Docker。版本/包元数据和11份基线依赖图位于 `/private/tmp/syncclipboard-stage17b/`；nuspec提供提交9b098373e85a4daf3f0cf716ab3a27b02470f7dc但没有仓库URL，不据此构造未经核实的源码链接。

仅Server引用此构建包。使用1.23.0支持的新属性ContainerBuildContext替代DockerfileContext，保留`..`，相对Server Dockerfile仍指向src；求值前后Linux、net10.0和Release的Regular模式一致，记录 `/tmp/syncclipboard-stage17b-baseline-properties.json`、`-properties.json`。包的props/targets文本确认新属性传给容器构建任务。Dockerfile仍使用.NET10 Ubuntu24.04基础镜像、SDK10.0.302、原多阶段构建、5033端口和/app/data内容目录；Compose仍是现有远程发布镜像的使用示例，不拉取或运行该镜像。未新增IDE启动/调试或UI验证。

11份项目依赖图比较仅Server的容器工具1.22.1→1.23.0变化；其余包和10份图不变。Server各目标下该包仅提供build资产，没有compile/runtime资产，不把包的.NETFramework4.7.2元数据解释为产品需安装该运行时。报告 `/tmp/syncclipboard-stage17b-dependency-diff.json`。

本地dotnet restore与Release发布通过；发布目录 `/private/tmp/syncclipboard-stage17b-publish/`。server_smoke两轮API/认证/历史/传输数据通过；server_startup_smoke的命令行/环境配置各首次和重启四轮通过，均临时目录与回环HTTP、无UI。日志 `/tmp/syncclipboard-stage17b-{restore,publish,server-smoke,server-startup,format}.log`，仓库格式检查退出0，仅跨平台工作区加载警告。

本机无Docker CLI，不安装/启动图形容器工具。当前阶段提交必须由既有PR的Ubuntu amd64/arm64原生runner实际执行Docker构建、平台/系统确认、API与首次启动容器冒烟，以及独立Server发布/Swagger JSON检查，并完成全套CI/评审。无需下载CI编译产物；步骤17b尚未通过，不进入17c。

步骤17b提交 `cc2022c0aaa67b4e507d5467850995b67a97eaf9` 已推送。[PR run34822154967](https://github.com/Jeric-X/SyncClipboard/actions/runs/34822154967)、[push run34822150921](https://github.com/Jeric-X/SyncClipboard/actions/runs/34822150921)、CodeQL34822154689已启动，本head评审正在运行，首次完整反馈核对无新增问题，11个线程均已处理。监控pr419已创建，每10分钟检查。仍须当前提交的双架构容器/Server以及全部CI和评审通过，不进入17c。

本提交Server发布与双架构容器任务均成功。实际日志确认Ubuntu24.04原生amd64/arm64分别执行Docker构建并运行各自架构的.NET10；两个容器各两轮API/认证/历史/传输数据及四轮命令行/环境配置首次启动与重启检查通过。独立Server也完成同样六轮检查及四种Swagger JSON诊断/环境组合，未访问Swagger UI。证据 `/tmp/syncclipboard-pr419-cc20-server.log`、`-container-amd64.log`、`-container-arm64.log`。其余完整CI与本head评审仍待结束，17b尚未最终通过。

当前提交的Core442、三平台Desktop各6、WinUI58，共518项NonUI测试均通过、0失败/跳过；五份覆盖率校验和三平台Quartz探针通过，汇总 `/tmp/syncclipboard-stage17b-ci-tests.json`。本head自动评审于2026-09-14T08:25:10.270285Z完成，无新增正文或行内问题，11个线程均已处理。完整构建/打包仍待结束，17b尚未最终通过。

### 步骤 17b 最终通过记录

验证通过提交 `cc2022c0aaa67b4e507d5467850995b67a97eaf9`：[PR run34822154967](https://github.com/Jeric-X/SyncClipboard/actions/runs/34822154967) 58项全部成功，[push run34822150921](https://github.com/Jeric-X/SyncClipboard/actions/runs/34822150921) 58项成功、8项发布任务预期跳过，CodeQL34822154689和CodeFactor成功。汇总119 SUCCESS、8 SKIPPED，PR CLEAN；本head评审于2026-09-14T08:25:10.270285Z完成，无新增问题，11个线程均已处理。

518项NonUI测试、覆盖率及三平台Quartz检查通过；Server发布、双架构原生Docker构建/平台检查与各六轮HTTP/首次启动验证、四种Swagger JSON组合均实际成功。完全信任CI产物，未下载或审计远程编译产物，UI验证执行0。证据 `/tmp/syncclipboard-pr419-cc20-complete.json`、`-pr-complete.json`、`-push-complete.json`、`-latest-runs.json`、`-final-threads.json`，三份Server/容器日志和 `/tmp/syncclipboard-stage17b-ci-tests.json`。监控pr419已删除，允许进入17c；整体升级未完成。


## 步骤 17c：PupNet 与 Linux 打包工具

2026-09-14 官方 NuGet 索引与 GitHub 发布确认 KuiperZone.PupNet 1.10.0 为最新稳定版，v1.10.0 对应源码提交 `90793a199180da1ff2437bcbda83dedde2420d5f`。工具项目自身为 net10.0/框架依赖工具，未引用额外 NuGet 包。将 Linux 打包 CI 从 1.8.0 升至 1.10.0，移除该任务的 SDK 8.0.x，继续固定 SDK 10.0.302；AGENTS/CLAUDE 同步工具要求。

PupNet 1.9 起不再附带 appimagetool。AppImage 任务安装官方稳定版 appimagetool 1.9.1 和 libfuse2，将其原始文件名加入 PATH；按 runner 主机 x64 架构选择构建工具，目标仍由既有 linux-x64/linux-arm64 参数决定。RPM 任务显式安装 rpm。保留原 10 个 Linux 安装包组合、arm64 RPM 排除、显式 --kind、版本与发行号、输出重命名、自包含与非自包含配置；不新增包类型或发布行为。

只获取官方工具源码并在本地编译，0 警告/错误；本地 --version/--help 正常。仅对临时配置副本执行 --upgrade-conf -y，新旧配置规范化后完全相同，AppImageVersionOutput=true 原本已启用，保持既有版本文件名。结合上游源码确认 deb/rpm 输出命名匹配现有 CI 重命名规则。配置检查未执行 BuildHost、PostPublish 或应用运行，仓库配置只更新工具版本注释；证据 `/private/tmp/syncclipboard-stage17c/config-check/` 与 `/tmp/syncclipboard-stage17c-local-tool-build.log`。

11 份项目依赖图 targets/libraries 不变；package.sh/PostPublish.sh 的 bash 语法、actionlint、diff 检查通过，仓库格式检查退出 0，仅既有跨平台工作区加载警告，日志 `/tmp/syncclipboard-stage17c-format.log`。实际 Linux 包生成与重命名需本阶段当前提交的 PR CI 验证，并等待完整 CI/评审后才能进入 18。完全信任 CI 编译产物，不下载或复用远程产物审计，UI 验证执行 0。


步骤17c提交 `19900d25e7f056dc4d389df1b0e05cf15f98976d` 已推送。[PR run34825478991](https://github.com/Jeric-X/SyncClipboard/actions/runs/34825478991)、[push run34825474514](https://github.com/Jeric-X/SyncClipboard/actions/runs/34825474514)、CodeQL34825478612已启动，本head评审正在运行。首次完整反馈核对未发现新增问题，11个线程均已处理；监控pr419已创建，每10分钟。当前步骤仍未通过，不进入18，不下载远程编译产物。


当前提交五份CI测试日志已确认Core442、三平台Desktop各6、WinUI58，共518项NonUI测试全部通过，0失败/跳过；产品覆盖率校验及三平台Quartz各10组/六项取消入口通过。汇总 `/tmp/syncclipboard-stage17c-ci-tests.json`，日志 `/tmp/syncclipboard-pr419-1990-{core,desktop-linux,desktop-macos,desktop-windows,winui}.log`。Linux实际打包、其余构建和本head评审仍在运行，17c尚未通过。


本提交PR和push工作流的10个Linux安装包组合均实际成功：x64/arm64的AppImage、deb及x64的rpm，均包含自包含/非自包含。日志确认PupNet Deploy 1.10.0运行，实际打包、既有模式重命名和上传步骤完成，证据 `/tmp/syncclipboard-pr419-1990-{appimage-arm64,rpm-x64,deb-arm64}.log` 与 `-progress4.json`、`-push-progress4.json`。appimagetool从固定1.9.1发布URL安装，其自报横幅为continuous build/8c8c91f，不将URL标签解释为额外二进制版本审计。只读取CI日志，未下载生成的安装包；完整Windows/macOS构建及本head评审仍须收齐后才通过17c。


### 步骤 17c 评审修复：运行中的清理任务取消

提交19900d25的CI最终汇总119 SUCCESS、8 SKIPPED，PR CLEAN，但本head评审于2026-09-14T09:09:56.191239Z完成时新增[P2反馈](https://github.com/Jeric-X/SyncClipboard/pull/419#discussion_r4003702561)：清理任务仅把令牌交给Task.Run，委托开始后的扫描和递归删除不观察取消，仍可能延迟Quartz关闭并超过替换实例的10秒等待。因此17c未通过，不进入18。

修复将令牌传入孤儿历史目录、应用数据及本地缓存清理实际路径。历史/缓存查询改为可取消异步枚举，信号量等待、逐行/目录处理与数据库保存接收令牌；取消异常不再被普通错误处理吞掉。新增共享文件清理方法在每个文件/目录边界检查取消，递归删除时不遍历目录链接的目标。历史已删除数据清理也使用此方法和可取消查询，并在目录删除完成后才清除记录的文件信息，避免取消时先丢掉重试所需路径。现有清理保留规则、Quartz注册/间隔和等待安全释放次序保持不变。操作系统单次文件操作本身不能被托管令牌抢占，本次保证各操作之间可以停止；没有声称实际桌面重启已验证。

新增FileSysCancellationTests两项确定性回归：执行前取消保持全部数据；首个目录及其子文件清理后取消保留下一文件，并可继续清理。Core原442项逐项保留，现444全部通过；Desktop原6项不变且通过，0失败/跳过。覆盖率与实际产品程序集命中校验通过，报告 `/private/tmp/syncclipboard-stage17c-fixed-results/`；首次完整Core命令因runsettings路径笔误未执行测试，改用仓库nonui-coverage.runsettings后通过，日志 `/tmp/syncclipboard-stage17c-fixed-core2.log`、`-fixed-desktop.log`。

Quartz实际生产任务探针仍10组、六项提前取消，加上孤儿历史/缓存两项查询开始后的取消：订阅EF数据库命令完成诊断，仅在真实SELECT执行后取消，确认异常传出且目录/数据库记录保留，之后正常清理和重复清理成功。报告 `/tmp/syncclipboard-stage17c-cancellation-quartz.json`，日志同前缀-cancellation-quartz.log，新增字段runningCleanupCancellationJobs=2由CI校验器强制核对；不依赖计时、UI或实际桌面。

11份依赖图不变，仓库及探针格式、actionlint、diff通过；CI Core最低数提高至444，五份测试合计预期520项。问题已追加本地修复日志，线程PRRT_kwDOBXBc9s6iDINR待修复提交的全套CI/评审通过后解决，不发送评论。仍完全信任CI产物，不下载远程编译产物或做二进制审计。
