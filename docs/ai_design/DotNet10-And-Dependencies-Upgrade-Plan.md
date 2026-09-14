# .NET 10 与主要依赖分步升级计划（仅非 UI 验证）

编写日期：2026-09-13。代码基线：`master`，`cc7289d1`（传输数据哈希校验与持久化，PR #413）。

本文是待执行的升级计划，未表示任何升级、测试或 PR 验证已经完成。目标是先统一项目的 .NET 版本，再依次升级主要依赖；每一步独立提交、独立验证，通过后才能开始下一步。

**验证范围：所有需要 UI 的内容均不验证。** 保留构建、静态检查、不依赖 UI 的自动化测试、协议/数据检查和打包产物检查。UI 项目仍需升级适配并通过编译，但不启动桌面应用，不执行点击、输入、截图、视觉比较、窗口/焦点/热键/通知实测或依赖交互桌面会话的测试。需要 UI 的检查统一标记为“范围排除：按用户要求不验证”，不作为阶段通过条件，也不因锁屏或缺少可操作桌面而阻塞升级。阶段通过仅代表本文约定的非 UI 验证通过。

此范围适用于全部升级步骤、本地验证、PR CI 和后续评审修复，无论 macOS 是否锁屏都不执行 UI 验证。需要 UI 才能完成的检查直接记录为“范围排除”，不安排解锁后的补测，也不以安装虚拟显示器、启动无头 UI 框架或远程桌面代替执行。锁屏时以实际取得的非 UI 命令退出码和 CI 结果为准；若系统休眠导致任务暂停，恢复后继续收集结果，不能提前标记通过。

## 1. 执行规则

1. **严格串行。** 每一步按“确认前置条件 → 锁定版本 → 最小改动 → 本地非 UI 验证 → 提交并推送 PR → 监控并修复 → 记录通过证据”执行。范围内检查失败、缺少必要非 UI 平台验证或等待用户判断时，停止后续升级。需要 UI 的检查按第 3.2 节排除，不等待 UI 验收后再进入下一步。
2. **默认复用一个升级 PR。** 从已同步的 `master` 创建 `codex/upgrade-dotnet10-dependencies`，每一步形成独立提交或明确的提交组。在同一 PR 中逐步追加，避免混入尚未验证的下一步。每次记录当前 head SHA；上一版 CI 通过不能证明下一版通过。
3. **CI 必须通过真实 PR 验证。** 修改工作流、SDK、TFM、发布参数、Docker、安装包或影响其依赖解析的包版本，都必须推送至 PR，并执行第 4 节的监控流程。本计划所有实际升级步骤均要求 PR 验证。
4. **不自动合并或发布。** PR 干净只表示该步验证通过，不表示已合并。未获明确授权，不修改 `master`、强推、合并、关闭、修改 PR 目标分支或创建发布标签。若改用每步一个 PR，先由用户合并前一步，再从更新后的 `master` 开始下一步。
5. **版本固定。** NuGet 版本只写入 `src/Directory.Packages.props`。执行每一步前重新核对官方发布说明、NuGet 目标框架与传递依赖，记录精确版本；不把 `latest`、`10.*` 等浮动版本写入包引用。本文的候选版本不是已经验证的兼容组合。
6. **保持范围可归因。** .NET 升级阶段保留主要 NuGet 版本。依赖升级阶段一次只处理一个依赖或必须共同迁移的一组；不顺带启用 AOT、裁剪、单文件、原生 Wayland 或更换测试运行器。
7. **保持范围内覆盖。** 不通过删除失败测试、缩减构建平台矩阵、关闭范围内检查、吞掉异常或屏蔽新警告来取得通过。依赖 UI 的用例按用户要求列入明确的范围排除清单，不计作通过；不要删除测试源码，也不要把整个 Desktop/WinUI3 测试项目直接排除。必要的兼容修复留在当前步骤，记录原因和验证。
8. **遵守仓库规范。** 不跨平台构建整个 solution；读取当时有效的 `AGENTS.md`。说明目标框架、命令或架构的内容发生变化时，同步修改 `AGENTS.md` 与 `CLAUDE.md`。

## 2. 基线与目标

### 2.1 目标框架

| 项目 | 当前目标 | 最终目标 | 步骤 |
| --- | --- | --- | --- |
| SyncClipboard.Desktop.MacOS | net10.0-macos | 保持 | 3 回归 |
| SyncClipboard.WinUI3、SyncClipboard.Test.WinUI3 | net9.0-windows10.0.19041.0 | net10.0-windows10.0.19041.0 | 2 |
| SyncClipboard.Desktop.Default | net8.0-windows10.0.17763.0;net8.0 | net10.0-windows10.0.17763.0;net10.0 | 3 |
| SyncClipboard.Desktop、SyncClipboard.Test.Desktop | net8.0 | net10.0 | 3 |
| SyncClipboard.Server | net8.0 | net10.0 | 4 |
| SyncClipboard.Shared、SyncClipboard.Server.Core、SyncClipboard.Core、SyncClipboard.Test | net8.0 | net10.0 | 5 |

按入口项目到共享库的顺序升级：.NET 10 入口可以暂时引用 net8.0 库；先把共享库改成仅支持 net10.0，会使尚未升级的入口无法引用。步骤 2、3 中测试项目对 `SyncClipboard.Test` 的引用暂时仍是 net8.0，到步骤 5 再统一。

### 2.2 主要依赖

| 依赖组 | 当前版本 | 候选目标或选择规则 | 步骤 |
| --- | --- | --- | --- |
| Microsoft.Extensions.DependencyInjection、Logging.Console、SignalR.Client、System.Net.Http.Json | 9.0.9 / 9.0.8 | 核对后固定兼容的 10.0 补丁版本 | 6 |
| EF Core、Sqlite、Design | 9.0.8 | 相同的稳定 10.0 补丁版本，核对 Microsoft.Data.Sqlite 与原生依赖 | 7 |
| Avalonia、Desktop、Themes.Fluent、Fonts.Inter | 11.3.18 | 12.1.2，执行时复核后续稳定补丁 | 8 |
| FluentAvaloniaUI | 2.3.0 | 3.1.0，要求 .NET 10、Avalonia >= 12.1.0 | 8 |
| AsyncImageLoader.Avalonia | 3.3.0 | 3.8.0，已声明支持 Avalonia 12 | 8 |
| FluentAvalonia.BreadcrumbBar | 2.0.2 | 兼容版本、受维护替代或最小移植，不能假设旧包可用 | 8 |
| Avalonia.Diagnostics | 11.3.18 | 移除；需要工具时核定 AvaloniaUI.DiagnosticsSupport 版本 | 8 |
| WindowsAppSDK、SDK.BuildTools | 1.8.260529003 / 10.0.26100.4948 | 2.4.0 / 10.0.28000.2705，执行时核对配套组件及部署要求 | 9a |
| WinUIEx | 2.3.4 | 2.9.3，要求 net8.0-windows10.0.19041 与 WinUI >= 1.8.250906003 | 9b |
| H.NotifyIcon.WinUI | 2.3.0 | 2.4.1，要求 .NET 10，配套 Core/GeneratedIcons 2.4.1 | 9c |
| WinUI CommunityToolkit SettingsControls/Converters | 8.2.250402 | 8.2.251219，配套 Extensions/Helpers/Triggers 同版 | 9d |
| AWSSDK.S3 | 3.7.414 | 4.0.103.2，配套 AWSSDK.Core 4.0.102.4 | 10 |
| Magick.NET Q16 各架构包、SystemDrawing | 14.9.1 / 8.0.15 | Q16 14.17.1；SystemDrawing 8.0.27，配套 Core 14.17.1 | 11 |
| SharpHook | 5.2.3 | 8.0.0，配套 libuiohook 2.0.0；迁移 API 并保留现有 XRecord 行为 | 12a |
| NativeNotification、NativeNotification.Interface | 1.0.5 | 2026-09-14 核定均已是最新稳定版，保留配套 1.0.5 并独立验证兼容性 | 12b |
| Vanara.PInvoke ComCtl32/DbgHelp/Kernel32/User32 | 4.0.1 | 配套 5.0.7，核对 Shared/Core 合并及生成式 API | 12c |
| Interop.UIAutomationClient | 10.19041.0 | 2026-09-14 核定已是最新稳定版，保留并独立验证兼容性 | 12d |
| Microsoft.Toolkit.Uwp.Notifications | 7.1.3 | 2026-09-14 核定已是最新稳定版，保留并独立验证通知载荷兼容性 | 12e |
| CommunityToolkit.Mvvm | 8.4.0 | 8.4.2；核对 Roslyn 5.0 / C# 14 源生成器选择与兼容性 | 13a |
| ObservableCollections | 3.3.4 | 核定稳定版本 | 13b |
| Quartz、Quartz.Extensions.DependencyInjection | 3.14.0 | 相同的稳定兼容版本 | 14 |
| Swashbuckle.AspNetCore | 8.1.1 | 核定 ASP.NET Core 10 兼容版本 | 15 |
| Test SDK、MSTest、coverlet、Moq | 17.14.1 / 3.10.4 / 6.0.4 / 4.20.72 | 保持 VSTest，逐组核定稳定兼容版本 | 16a–16c |
| CodeCracker、Containers.Tools.Targets、PupNet | 1.1.0 / 1.22.1 / 1.8.0 | 逐项检查维护状态与工具运行时要求 | 17a–17c |

FluentAvalonia 与图片加载器的依据分别为 [NuGet 依赖声明](https://www.nuget.org/packages/FluentAvaloniaUI/3.1.0) 和 [图片加载器发布说明](https://www.nuget.org/packages/AsyncImageLoader.Avalonia/3.8.0)。其余未列精确目标的项目，必须在执行对应步骤时填入版本与官方来源后再修改；若没有更新，记录“当前已是可用稳定版”，不能虚构升级。

## 3. 通用验证与记录

### 3.1 编译和测试

以下命令从仓库根目录运行，除格式检查外。框架参数使用**当前步骤实际目标框架**；不要提前给尚未升级的项目指定 net10.0。

运行测试前逐项确认是否依赖 UI。下列 `dotnet test` 命令只在整个测试项目均不依赖 UI 时直接执行；否则使用明确的测试分类或 `--filter` 仅运行非 UI 用例，并记录原总数、纳入数、UI 排除数及用例名称/原因。DI 测试若构造窗口或初始化交互桌面，同样属于排除项；能够通过既有 mock 在无 UI 环境运行的 DI、ViewModel、转换器和集合测试继续执行。不得将测试发现成功或 UI 用例被排除写成整套测试通过。

Desktop/WinUI3 的混合测试项目采用明确的非 UI 白名单：审查测试初始化、数据源、服务构造及清理过程后，将安全用例标为 `TestCategory("NonUI")`，使用下面的正向筛选命令。分类尚未完成时先分类，再运行；不直接运行整个混合测试项目，也不只用 `TestCategory!=RequiresUI` 排除已知用例，以免新加入或未分类的 UI 用例被执行。筛选结果为零项不算验证通过。

```bash
dotnet build src/SyncClipboard.Shared -c Release
dotnet build src/SyncClipboard.Server.Core -c Release
dotnet build src/SyncClipboard.Core -c Release
dotnet build src/SyncClipboard.Desktop -c Release
dotnet test src/SyncClipboard.Test -c Release
dotnet test src/SyncClipboard.Test.Desktop -c Release --filter "TestCategory=NonUI"
```

Windows 原生终端另外运行：

```powershell
msbuild src\SyncClipboard.WinUI3\SyncClipboard.WinUI3.csproj /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:Configuration=Release /p:WindowsAppSDKSelfContained=true /p:SelfContained=true /v:m -restore
dotnet test src/SyncClipboard.Test.WinUI3 -c Release --filter "TestCategory=NonUI"
```

Windows 构建同时覆盖 arm64，以及现有自包含/非自包含和 Windows App SDK 携带/不携带矩阵。命令按对应架构替换 `Platform` 和 `RuntimeIdentifier`。测试运行器与原生库使用一致架构；跨编译 arm64 不等于在 arm64 执行了测试。

Linux 发布示例（步骤 3 起目标为 net10.0；验证更早的基线时使用 net8.0）：

```bash
dotnet publish src/SyncClipboard.Desktop.Default/SyncClipboard.Desktop.Default.csproj -f net10.0 -r linux-x64 -c Release --self-contained true
```

同时验证 linux-arm64 和非自包含产物；Windows 上另外验证 Desktop.Default 的 Windows TFM，因为 WinUI3 构建不能覆盖这个入口。

macOS 发布示例：

```bash
dotnet publish src/SyncClipboard.Desktop.MacOS/SyncClipboard.Desktop.MacOS.csproj -r osx-arm64 -c Release
dotnet publish src/SyncClipboard.Desktop.MacOS/SyncClipboard.Desktop.MacOS.csproj -r osx-x64 -c Release
```

服务器与 Docker：

```bash
dotnet publish src/SyncClipboard.Server/SyncClipboard.Server.csproj -c Release
docker build -f src/SyncClipboard.Server/Dockerfile -t syncclipboard-upgrade-check src
```

在隔离配置和测试数据库下启动服务器/容器，验证认证、文件上传下载、SignalR 和历史记录。使用临时端口及测试账号，不使用个人配置、生产桶或真实用户数据库。Docker 构建上下文为 `src`。

代码格式检查从 `src/` 执行仓库规定命令：

```bash
dotnet format --verify-no-changes --severity info --no-restore
```

先完成对应平台依赖还原。若本机受到平台项目限制，记录未覆盖部分并交由已配置工作负载的 CI 验证，不将局部检查写成全量通过。执行命令后记录退出码、测试数量和失败/跳过情况；仅“命令启动成功”不能作为通过证据。

### 3.2 非 UI 回归集合

执行时按下表判定范围；测试名称含“单元测试”“DI”或“无头”本身不能证明它不依赖 UI。

| 检查类型 | 本计划处理方式 |
| --- | --- |
| 桌面项目还原、C#/XAML 编译、静态分析、安装包解包检查 | 保留，按当前步骤独立验证 |
| 使用 mock 的业务逻辑、ViewModel、数据转换及服务端 API 测试 | 确认初始化、执行和清理均不依赖 UI 后运行 |
| 图片编解码、像素/透明通道比较、图片格式转换及纯 Bitmap 数据转换 | 保留；直接检查数据，不打开图片预览、不创建控件、不访问真实剪贴板 |
| 创建窗口/控件、加载运行时界面、启动 UI 框架或依赖 UI 调度线程的测试 | 排除；隐藏窗口、无头模式和虚拟显示器也不执行 |
| 桌面程序启动、图形安装向导、托盘、热键、通知、真实剪贴板及跨应用操作 | 排除，不安排人工补测或解锁后补测 |
| 注册真实全局 hook、初始化输入设备、发送模拟按键或触发系统权限提示 | 排除；仅保留使用 mock provider 的键码、配置、注册表及生命周期测试，构造和释放也不得调用真实桌面后端 |
| UI 截图、视觉比较、交互验收 | 排除，不作为本步骤或最终交付的通过条件 |

若原定验证在执行前发现必须初始化 UI，应记录具体检查及排除原因；能够拆出的非 UI 部分单独验证，不能为了保留原检查而启动 UI。只有这些范围排除项尚未验证时，允许在其余必要检查及 PR 门槛全部通过后进入下一步。

以下排除清单直接适用于相关步骤及最终验收，不再为这些内容建立验证任务：

| 涉及步骤 | 不验证的 UI 内容 | 保留的非 UI 检查 |
| --- | --- | --- |
| 2、3、8、9、18 | 窗口、页面、主题、字体、面包屑、弹窗、托盘的显示与交互；运行时 XAML 加载 | C#/XAML 编译、API 与资源引用静态核对、依赖解析、包结构，以及隔离的导航逻辑测试 |
| 8、11、12、18 | 真实剪贴板、拖拽、图片预览、热键、通知和桌面元素访问 | 数据转换、图像数据、参数解析、mock 测试及不初始化 UI 的原生库检查 |
| 13、16、18 | 真实 UI 绑定更新、UI 线程调度、控件选择与滚动 | 源生成代码、属性通知、集合和命令逻辑；仅运行经审查的非 UI 用例 |
| 15、17、18 | Swagger UI、IDE 图形操作、安装向导、安装后桌面启动 | HTTP/JSON 检查、命令行构建、服务器冒烟和安装包静态检查 |

所有步骤中的“绑定验证”仅指编译绑定和可隔离的数据通知逻辑；“原生库加载”也须先确认不会初始化 UI 或访问真实桌面。无法满足时按 UI 范围排除处理，不以测试名称或命令行入口判断其是否属于非 UI。

每一步（包括带字母的子步骤）均按以下顺序判定是否通过：

1. 开始验证前列出检查项，将需要 UI 的内容标为“范围排除：按用户要求不验证”，不执行、不记作通过，也不留下人工补测任务。
2. 独立执行其余构建、非 UI 测试、协议/数据及产物检查；任一必要检查失败或缺少证据时，当前步骤保持未通过。
3. 对当前提交完成 PR CI 与问题监控；涉及 UI 验证的评审建议沿用本范围约定，但指出的代码兼容性问题仍须检查和适配。
4. 记录“非 UI 验证通过”及对应提交和证据后再进入下一步。UI 排除项不阻塞推进，最终交付也不要求补做 UI 验证。

| 编号 | 必须验证的行为 |
| --- | --- |
| R1 剪贴板数据逻辑 | 通过测试数据和 mock 验证文本、HTML、图片、单文件、多文件的 Profile 转换、序列化、哈希、历史恢复逻辑和同步去重；不访问真实系统剪贴板，不做跨应用复制/粘贴/拖拽 |
| R2 桌面代码兼容性 | C#/XAML 编译、依赖解析、生成代码、资源引用，以及不初始化交互桌面的 DI/ViewModel/转换器测试；不验证界面显示和实际交互行为 |
| R3 数据 | 使用旧版生成的数据库副本；历史条数、内容、时间、排序、收藏、清理、迁移及传输哈希正确；保留升级前副本 |
| R4 同步 | 通过 API、测试客户端或独立测试宿主验证 Official/WebDAV/S3 与内置服务器代码；认证、代理、HTTPS、取消/重试、文件完整性及旧新版协议互通，不启动桌面客户端 |
| R5 发行产物 | 构建安装包与便携包，检查包结构、资源和原生库、runtimeconfig/deps、版本、名称及 CPU 架构；服务器/容器可做无 UI 启动检查，桌面程序和图形安装向导不启动 |

命令日志、测试报告和产物检查记录应标注 OS、架构、配置及产物所属提交。不采集 UI 截图。范围内测试服务或原生运行环境不可用时记录阻塞，可由对应 CI 承担；仅缺少 UI 会话、设备上的界面操作或 UI 行为证据不构成阻塞。不能把编译、mock 测试或包结构检查解释为 UI 运行验证。

升级包含原生库的依赖时，使用全新发布输出，或先清理目标项目的生成缓存；核对最终包内的库版本，不能只检查输出目录旁边的文件。macOS 本地复验先运行 `dotnet clean src/SyncClipboard.Desktop.MacOS -r osx-arm64 -c Release`，再执行发布和 `build/macos/BundleTool.sh --app-bundle <实际应用包路径> --architecture arm64`，最后检查包内容与严格签名；x64 对应替换架构。只完成 `dotnet publish` 还未覆盖 CI 后续的资源处理和重新签名。

## 4. PR 提交与持续监控门槛

实际执行提交/监控时使用 `$pr-watch` 技能，并重新读取其最新内容。当前默认分支是 `master`。运行任何 `gh` 命令前，按全局规则请求沙箱外执行；不要先在沙箱内试跑，也不要把代理不可达当作认证失败。

1. 本地验证完成后检查完整 diff，只暂存当前步骤文件，以中文 Conventional Commit 提交并正常推送开发分支。
2. 首步创建 PR，后续步骤更新同一 PR 的描述和进度记录。描述包含问题、最终改动、准确版本、验证结果、平台限制和回退方式。
3. 推送后立即读取 aggregate/individual checks、reviews、review decision、未解决 review threads、行内评论和普通讨论评论。确认检查属于当前 head，PR 合并测试提交包含该 head；不能复用历史绿色结果。
4. 持续监控期间使用当前任务的 heartbeat，默认每 10 分钟一次，名称 `监控 PR<编号>`；先检查已有自动化，避免重复。在监控提示中保存本计划的非 UI 验证范围及 UI 排除清单，不因后续评审建议自行恢复 UI 验证门槛。状态无变化且无可处理问题时保持安静，仅在有意义变化、完成、失败或需要用户决定时通知。
5. 每个可处理问题均定位根因、作当前步骤内最小修复、重跑受影响验证、提交并推送，再检查新的提交。获得明确授权后才通过 GitHub 评论回复他人；不能因为自动化模板建议回复就自行发送消息。
6. 逐项维护本地修复日志 `docs/ai_design/.local/PR-<编号>-Issue-Fixes.md`，包含发现来源、原问题、根因、修复、验证、提交和线程状态。加入本地 `.git/info/exclude`，不改共享 `.gitignore`；绝不暂存、提交、推送、覆盖或删除日志。heartbeat 保存绝对路径和禁止提交约束。
7. 检查尚未完成、有失败或仍有可处理反馈时，不开始下一步。需要用户判断的问题列明并等待；若只剩这种问题，按技能删除 heartbeat，但本步骤仍保持阻塞状态。
8. 检查全部结束且无未解决的可处理反馈后，记录最终 head 和检查链接，删除 heartbeat。下一步推送后重新立即检查并在需要时恢复监控。

**本计划比普通 PR 的“干净”条件多一个覆盖要求：** 与本步骤相关的构建、范围内非 UI 测试、打包必须实际运行成功；`skipped`、`neutral`、没有触发或矩阵被删除不能代替必要检查通过。UI 测试按第 3.1 节记录为范围排除，不纳入通过数量；若既有 CI 包含 UI 测试，应显式拆分/筛选并在 PR 中说明，不通过伪造成功结果处理。无关且预期跳过的任务需说明原因。范围内既有失败也必须查明，不能直接忽略后继续。

现有 `.github/workflows/build-entry-pr.yml` 已调用格式检查、Core 测试、Windows 打包、Server 构建、Linux 打包、macOS 打包。另有 CodeQL。当前仅运行 Core 测试，需要核定并补齐 Desktop/WinUI3 中不依赖 UI 的测试覆盖；现有 Server 发布也不等同于 Docker 构建和启动验证。

不要为了验证而创建发布标签或调用发布工作流。缺少的必要验证应接入 PR 检查，例如 Docker 只构建与冒烟、不推送镜像。

## 5. 顺序执行步骤

### 步骤 0：建立可比较基线

**改动：** 暂不升级。确认工作区、分支和远程；记录最新基线 SHA、SDK/MSBuild/Xcode/workload 版本、全部直接依赖及关键传递依赖。保存现有工作流矩阵和可运行的旧版产物，准备脱敏配置和数据库副本。

**验证：** 在当前目标框架运行第 3 节可用检查及 R1–R5 基线；检查现有 PR/CI 结果，明确既有失败、缺失测试及未验证的平台。核对最低系统支持目标，不把可编译平台版本等同于官方支持范围。

**通过条件：** 非 UI 基线可复现；范围内所需构建/测试运行环境、服务和测试数据就绪，UI 排除清单已记录。若有范围内既有失败，先单独修复并验证，不混入下一步升级。

### 步骤 1：先统一构建工具链，保持 TFM 和依赖不变

**改动：**

- 为仓库确定可复现的 .NET 10 SDK 版本/feature band 和 `global.json` 策略，并核对 VS 2026/MSBuild、Xcode 与 macOS workload 的配套关系。
- 更新 `code-style.yml`、`codeql.yml`、`win-build.yml`、`linux-compile.yml`、`linux-package.yml`、`server-build.yml`、`core-test.yml` 和 `mac-package.yml` 中相关工具链配置。**仍保留未迁移项目及 PupNet 等工具需要的 .NET 8/9 运行时**，不要依赖主版本 roll-forward 或 runner 预装环境。
- 补齐 PR 中 Desktop/WinUI3 的非 UI 测试、Desktop.Default Windows TFM 编译、Docker 构建和服务器冒烟覆盖；依赖 UI 的用例按第 3.1 节列明并排除，不要求配置交互桌面环境。
- 清点 `build/appveyor/appveyor.yml` 的 VS 2022 和旧项目路径，确认是否仍使用；使用则迁移，不使用则记录为遗留，不能作为当前验证依据。

**验证：** 第 3 节原目标框架构建及全部范围内测试；检查实际选中的 SDK，而不只看 setup 安装日志。提交 PR，监控现有及新增范围内检查全部通过，下载各平台产物执行 R5；仅服务器/容器执行无 UI 启动检查。

**通过条件：** 新工具链可构建旧目标；构建矩阵和非 UI 测试覆盖没有减少；旧版命令行工具和非 UI 测试宿主仍可运行。只安装 SDK 10 不表示 net8/net9 应用能够执行。

### 步骤 2：升级 WinUI3 入口及其测试到 .NET 10

**改动：** 两个 WinUI3 项目改为 `net10.0-windows10.0.19041.0`。保留 Windows SDK 目标版本、WindowsAppSDK 与其他依赖版本。同步 `win-build.yml` 产物路径、`build/windows/CompileAndCreateInstaller.ps1`、根目录及 `src/.vscode/` 调试配置、有关说明文件。

**验证：** Windows Release 构建和 Test.WinUI3 中的非 UI 用例；现有 x64/arm64、携带/不携带 .NET 和 App SDK 的矩阵；R1、R2、R5。解包检查实际文件、原生库和运行时声明，不启动图形安装程序或桌面产物。

**通过条件：** 当前步骤 PR 检查与 Windows 回归通过；Core/Shared/Test 暂时保持 net8.0 仍能被引用。

### 步骤 3：升级 Avalonia 共享桌面层、Default 入口及测试

**改动：** Desktop、Test.Desktop 改为 net10.0，Desktop.Default 的两个 TFM 按第 2 节修改。macOS 保持 net10.0-macos；**所有 Avalonia 相关包保持原版本**。修改 Linux 显式 TFM 发布参数、调试路径、运行时说明和必要的安装包依赖声明。

**验证：** Desktop 非 UI 测试、Default 的 Linux/Windows 两个入口编译、macOS 两架构发布；PR 中 Windows、Linux AppImage/deb/rpm（保留现有 arm64 rpm 排除）、macOS dmg；R1、R2、R4、R5。保留原有 Linux 后端配置，不启动 X11/Wayland 桌面会话验证。

**通过条件：** 现有 Avalonia 11 在 .NET 10 下通过构建和回归；不能靠提前升级 Avalonia 12 绕过当前阶段失败。

### 步骤 4：升级独立 Server 入口和容器

**改动：** Server 改为 net10.0，Server.Core/Shared 暂时保留 net8.0。更新 `server-build.yml` 产物路径、Docker 构建/运行镜像以及中英文 README 的 ASP.NET Core 运行时说明。核对 `server-release.yml` 消费的产物路径，但不执行发布。

**验证：** Server 发布、Docker 构建/启动、R3/R4；PR 中 Server 和容器检查必须实际运行。验证数据目录挂载、认证、端口和退出行为；通过无 UI 测试客户端验证旧协议连接新服务器、新协议连接旧服务器。

**通过条件：** 二进制与镜像均通过验证。默认 .NET 10 镜像基础系统发生变化，需要显式记录选用的镜像标签及基础发行版。[官方容器变更](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/10.0/default-images-use-ubuntu)

### 步骤 5：统一共享库与 Core 测试到 .NET 10

**改动：** Shared、Server.Core、Core、Test 一起改为 net10.0，此时所有入口和上层测试均已兼容。保留原 NuGet 版本。同步框架说明；搜索遗漏的 net8/net9 路径和发布参数，区分历史文档与活动配置。保留工具确实需要的旧运行时。

**验证：** 第 3 节所有项目构建、所有范围内测试和 PR 平台矩阵；R1–R5。特别检查各 RID 下 Core 的 `WINDOWS`、`MACOS`、`LINUX` 条件代码。核对 `.runtimeconfig.json` 中声明的运行框架及非自包含部署需求。

**通过条件：** 所有产品项目 TFM 已统一到 .NET 10 对应目标，主要 NuGet 版本尚未升级；形成后续依赖升级的独立回退点。

### 步骤 6：升级微软基础库

**改动：** 升级 DI、Logging.Console、SignalR.Client、System.Net.Http.Json 到核定的 10.0 补丁版本。检查共享框架和传递引用，按实际需要处理重复包引用/裁剪提示；不机械删除仍被项目使用的包，不升级 EF 或 Swagger。

**验证：** 全部范围内测试，重点非 UI 的 DI 解析、配置序列化、日志、SignalR 重连及取消；R4，所有 PR 构建。通过独立测试宿主启动内置 ASP.NET Core 服务代码，不启动桌面应用。

**通过条件：** 无包降级或程序集加载冲突，配置和传输协议仍与旧版兼容。

### 步骤 7：升级 EF Core / SQLite

**改动：** EF Core、Sqlite、Design 固定到同一 10.0 补丁，记录解析出的 Microsoft.Data.Sqlite 和 SQLite 原生版本；需要 `dotnet-ef` 时使用配套版本。不要仅为依赖版本变化生成空数据库迁移或重建数据库。

**验证：** 全部范围内 Core 测试和 R3/R4；在无 UI 测试宿主中用客户端、服务端旧库副本分别测试迁移、查询、分页、收藏、删除、清理、传输哈希与文件关联，比较升级前后的时间值和条数。检查 UTC 时间处理；若有 schema diff，逐项解释。所有范围内 PR 检查通过。

**通过条件：** 无历史记录丢失、时间偏移或哈希关联变化。回退必须使用升级前数据库副本，不能假定旧程序可打开升级后数据库。[EF/SQLite 迁移说明](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes)

### 步骤 8：升级 Avalonia 12 与必须配套的 UI 依赖

**改动：** 先核定第 2 节的 Avalonia/FluentAvalonia/AsyncImageLoader 组合，再一次性迁移这组存在主版本耦合的依赖。核对 BreadcrumbBar 的 API、XAML 与依赖兼容性，否则在本步提供替代或最小移植，按原导航逻辑适配；实际界面行为不在验证范围内。

- 删除 `App.axaml.cs` 中的 `BindingPlugins.DataValidators.RemoveAt(0)` 及失效导入。
- 将 `HistoryWindow.axaml.cs` 的 `ExtendClientAreaChromeHints` 改为新窗口装饰 API，按官方文档映射原无标题栏配置，不做窗口实测。
- 将 `TrayIconContextMenu.cs` 的 `NativeMenuItemToggleType` 改为 `MenuItemToggleType`。
- 移除旧 Diagnostics 引用；按需求接入新工具。检查 XAML 的 `Watermark`、自定义模板选择器、主题资源与 FluentAvalonia 导航/弹窗 API。
- 现有剪贴板与拖拽已经使用 DataTransfer 系列 API，重点检查平台格式和数据生命周期，不再做一次无必要的架构改写。
- 不启用新的原生 Wayland 后端，不把字体/主题视觉重设计混入迁移。

**验证：** 全平台构建与 Desktop 非 UI 测试、R1/R2/R5；检查 XAML 编译、类型与资源引用、编译绑定、依赖图及打包资源。导航和选择逻辑只通过不初始化 UI 的 ViewModel/集合测试验证，不逐页操作，不运行桌面产物。

**通过条件：** 依赖解析、C#/XAML 编译及范围内测试通过，打包资源完整；BreadcrumbBar 至少具备源码/API 核对及编译证据，不能只依据 NuGet 还原成功。XAML 运行时加载、视觉效果和交互行为标为“本计划不验证”，不宣称不存在运行时 UI 问题。[Avalonia 12 迁移说明](https://v11.docs.avaloniaui.net/docs/avalonia12-breaking-changes/)

### 步骤 9a–9d：依次升级 Windows UI 依赖

每一行是独立子步骤，完成当前行的本地验证和 PR 监控后才能开始下一行。

| 子步骤 | 改动 | 独立验证及通过条件 |
| --- | --- | --- |
| 9a | WindowsAppSDK 与必要配套的 Windows.SDK.BuildTools | WinUI3 编译、XAML、x64/arm64 打包及 App SDK 携带/不携带矩阵，R1/R2/R5 |
| 9b | WinUIEx | 窗口 API 调用编译兼容、依赖解析及可隔离的逻辑测试；Windows PR 构建，不创建真实窗口 |
| 9c | H.NotifyIcon.WinUI | 托盘 API/事件签名编译兼容、资源和包内容；Windows PR 构建及非 UI 测试，不创建真实托盘图标 |
| 9d | CommunityToolkit.WinUI Controls/Converters | 页面 XAML/绑定编译及纯转换器测试；Windows PR 构建及非 UI 测试 |

若某组必须与另一组同升，先记录官方依赖证据，将最小不可拆分组合视为一个验证单元；不能只因为方便而合并步骤。

### 步骤 10：升级 AWS SDK S3

**前置基线修复：** 真实 MinIO 测试已发现旧版 SDK 对 HTTP 自定义端点的上传失败：产品无条件关闭 payload 签名，而 SDK 只允许在 HTTPS 上使用 `UNSIGNED-PAYLOAD`。先保持 AWSSDK.S3 3.7.414，单独修复并通过本地协议检查和 PR CI；该前置提交通过后再升级 v4，不将旧版失败当作通过基线。具体证据见执行记录。

**改动：** 按官方 V4 指南迁移 AWSSDK.S3；检查模型可空值/集合默认值、请求构造、校验和、流和客户端生命周期。保留现有 S3 兼容存储的地址、路径风格和认证配置语义。

**验证：** 使用隔离测试桶完成上传、下载、列表、删除、空内容、多文件、大文件、取消/重试、传输哈希；覆盖实际支持的 S3 兼容服务而不只 AWS。运行 R4、相关 Core 测试及 PR 检查，不在日志记录凭据。

**通过条件：** 与旧版行为一致，失败重试不产生无限循环或损坏对象。没有可用测试服务时保持阻塞。[AWS V4 迁移指南](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/net-dg-v4.html)

### 步骤 11：升级图片处理依赖

**改动：** Magick.NET Q16 的 x64/x86/arm64/AnyCPU 包统一版本，SystemDrawing 按依赖要求配套；保留图像质量、格式处理与平台选择策略。

**验证：** 图片读取/转换、透明通道、大图、无效图片和缓存；R1/R5；各架构原生库加载、文件大小和打包内容，PR 全平台构建。

**通过条件：** 不依赖 UI 的图像测试和原生库加载检查通过，生成图片的尺寸、像素/透明通道等数据符合预期；不检查预览界面。项目保留 x86 包时也必须安排 Windows x86 构建和非 UI 原生库验证，不能用现有 x64/arm64 矩阵代表它。

### 步骤 12a–12e：依次升级原生集成依赖

| 子步骤 | 改动 | 独立验证及通过条件 |
| --- | --- | --- |
| 12a | SharpHook | 三平台编译、原生库包内容、键盘映射/修饰键纯逻辑和 mock 测试；R1/R2、全平台 PR，不注册真实热键或模拟输入 |
| 12b | NativeNotification 与 Interface 配套 | 三平台编译、接口兼容及通知参数/回调逻辑的 mock 测试；R2、全平台 PR，不发送真实通知 |
| 12c | Vanara.PInvoke 各模块配套 | 原生 API 签名、结构布局、资源打包及使用 mock 的错误处理测试；WinUI3/Core 非 UI 测试、Windows PR；不调用真实对话框、窗口、剪贴板、热键注册或键盘 hook |
| 12d | Interop.UIAutomationClient | COM 引用和调用签名编译兼容、窗口信息转换和降级分支的 mock 测试；Windows PR；不创建真实 UI Automation 客户端、不查询桌面元素 |
| 12e | Microsoft.Toolkit.Uwp.Notifications | 通知载荷、激活参数的纯解析测试及包内容检查；Windows PR；不初始化真实通知服务、不发送通知或执行界面激活 |

每一行都必须单独通过第 4 节。废弃包没有直接升级路线时，先记录维护现状和迁移范围；需要替换技术方案的，等待明确决定，不借此引入未经评估的大重构。

### 步骤 13a–13b：依次升级 MVVM 与集合库

**13a：** 升级 CommunityToolkit.Mvvm。通过无 UI 测试验证源生成器、命令、属性通知、校验、异步命令和取消，运行 Core/Desktop/WinUI3 范围内测试及 PR；通过后再开始 13b。

**13b：** 升级 ObservableCollections。通过集合/ViewModel 测试验证历史列表新增/删除/排序、多选状态、过滤、分页和同步通知；可隔离的调度逻辑使用 mock，运行范围内相关测试和 PR，不测试真实 UI 线程交互或长列表滚动。

**通过条件：** 当前子步骤的生成代码、编译绑定、属性通知和集合事件在非 UI 检查范围内通过；不验证运行时 UI 绑定效果，不依靠后一个子步骤修复前一个子步骤的问题。

### 步骤 14：升级 Quartz

**改动：** Quartz 与其 DI 扩展配套升级；核对注册方式、任务生命周期与取消行为，不改变产品调度策略。

**验证：** 非 UI DI 测试；查明实际注册的全部定时任务，通过隔离宿主和 mock 验证启动、触发、关闭、取消、异常后的恢复以及重复注册。任务中的界面、真实剪贴板、通知或输入操作不得实际执行；无法隔离的用例列为 UI 范围排除。相关 PR 检查通过。

**通过条件：** 任务在原定时机执行，无重复执行、无法退出或后台异常。

### 步骤 15：升级 Swagger / OpenAPI

**改动：** 升级 Swashbuckle，按解析出的 OpenAPI.NET 主版本调整 `MultipartFormDataOperationFilter`、`QueryHistoryOperationFilter` 和 `Web.cs`。TFM 改到 .NET 10 本身不强制这一迁移，因此放在独立步骤。

**验证：** 在无 UI 宿主中验证 Server 与内置服务器的诊断模式，通过 HTTP 获取并解析 Swagger JSON，检查 multipart 上传、历史查询参数、枚举和认证文档；真实 API 调用与 R4、Server PR 检查，不打开浏览器验证 Swagger UI。

**通过条件：** 文档可生成且与实际端点一致，正常同步行为不改变。

### 步骤 16a–16c：依次升级测试依赖

| 子步骤 | 改动 | 独立验证及通过条件 |
| --- | --- | --- |
| 16a | Microsoft.NET.Test.Sdk、MSTest.TestAdapter、MSTest.TestFramework | 保持 VSTest 模式，三套测试中的非 UI 用例可发现并执行；范围内 DI DataSource 数据仍被覆盖；UI 排除清单及 PR 测试数量变化必须说明 |
| 16b | coverlet.collector | 执行覆盖率采集并读取报告，测试数不减少；PR 中实际验证采集任务 |
| 16c | Moq | 三套项目中的范围内测试重新通过，特别是非 UI 的 DI/平台服务 mock；无因默认行为变化而漏测 |

不把升级测试包和测试运行器迁移合在一起，不用大面积修改断言掩盖行为变化。每个子步骤都须完成 PR 监控。

### 步骤 17a–17c：依次处理分析器与构建工具

| 子步骤 | 改动 | 独立验证及通过条件 |
| --- | --- | --- |
| 17a | 核对 CodeCracker.CSharp 的维护状态及新版编译器兼容性 | 若升级/替代，保留有效规则覆盖，解释诊断差异；格式、分析和 Windows PR 通过，不直接关闭分析器 |
| 17b | Containers.Tools.Targets | 静态核对 IDE 容器配置，执行 CLI Docker 构建/冒烟；Server PR 检查通过，不操作 IDE 界面 |
| 17c | PupNet 与它需要的工具运行时 | 保留现有包类型和架构，Linux AppImage/deb/rpm 构建与 R5 包内容检查通过；若无升级，记录现有工具兼容性证据 |

如果工具仍依赖旧运行时，CI 继续安装对应运行时；产品统一 net10.0 不要求删除所有旧工具运行时。

### 步骤 18：最终非 UI 集成验收与交接

**改动：** 汇总实际版本、支持系统、运行时安装说明和所有活动脚本；同步 AGENTS/CLAUDE，清理本次升级留下的失效配置。任何新修复都要重新验证受影响部分。

**验证：** 最终 head 上运行第 3 节全部非 UI 验证和 R1–R5；确认范围内 PR 检查、评审、行内线程与普通评论无未解决的可处理问题。下载最终产物，检查实际版本和所有约定架构。缺少必要非 UI 构建/测试覆盖时由对应 CI 补齐；不要求 UI 会话或界面实测，不缩减构建架构矩阵。

**通过条件：** 记录最终 head、PR、CI runs、测试计数、产物和非 UI 验证证据；所有前置步骤已通过，没有“待确认”的目标版本或范围内必要验证。实际 UI 行为统一标注“本计划不验证”，不作为阻塞，不声称 UI 已验收。删除监控 heartbeat，交由用户决定合并及发布。

最终交付中的“无未解决问题”限定为约定的非 UI 检查与已处理的代码评审反馈，不表示已经验证界面行为。交付记录明确列出 UI 范围排除项即可，不附加人工 UI 验收或锁屏解除后的补测要求。

## 6. 每步证据模板与回退

执行时在本文件末尾追加阶段记录或链接到独立执行记录；范围内未执行项保持“待执行”，需要 UI 的内容统一记为“范围排除”，不列入待办或补测。历史记录中的“已通过”仅指当时约定的非 UI 验证。本地 PR 问题修复日志仍按第 4 节保留，不提交其中内容。

```text
步骤编号与名称：
状态（仅非 UI 范围）：待执行 / 进行中 / 阻塞 / 非 UI 验证通过
前置步骤通过记录：
开始提交 / 验证通过提交：
依赖原版本 → 精确目标版本；官方来源与核对日期：
改动文件与必须共同升级的原因：
验证命令、OS、架构、退出码、测试总数/通过/失败/跳过数：
非 UI 回归编号、结果、产物和证据：
UI 排除用例/检查清单、数量与原因（本计划不验证，不计通过，不阻塞）：
需要 UI 的检查实际执行数量：0；本地与 CI 均适用
混合测试项目的非 UI 筛选条件及实际执行数（不得为零）：
排除项处理：不执行 UI 验证；仅记录相关兼容性分析与已完成的编译/静态检查，不以它们代替 UI 验收
是否存在需要 UI 的待办：否；发现此类检查时移入上述排除清单
非 UI 测试边界：初始化、执行、清理均不启动 UI、不访问真实桌面、不触发权限提示；否则范围排除
PR URL、当前 head SHA、CI run/check 链接及覆盖矩阵：
评审 / 未解决线程 / 行内评论 / 普通评论处理结论：
本地问题修复日志绝对路径：
范围内未覆盖项及阻塞原因：
回退提交与测试配置/数据库备份位置：
监控 automation ID、间隔与删除结果：
是否允许进入下一步：本步必要非 UI 检查及当前 head 的 PR CI/问题监控全部通过时填“是”；不等待 UI 验证
```

发生问题时停止推进，优先在当前步骤作最小修复。需要回退已经推送的步骤时，用新的 revert 提交保留历史，不强推或重置共享分支；回退后重新验证。数据库、配置与外部测试存储的恢复单独处理，不能假定撤销代码就能撤销数据变化。

## 7. 官方参考

- [.NET 10 兼容性变更](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10)；从 .NET 8 迁移还应检查 [.NET 9 变更](https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0)。
- [SDK、MSBuild 与 Visual Studio 版本关系](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs)。
- [.NET 10 支持系统与 libc 要求](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)、[OpenSSL 最低要求](https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/openssl-version-requirement)。
- [ASP.NET Core 10 迁移](https://learn.microsoft.com/en-us/aspnet/core/migration/90-to-100?view=aspnetcore-10.0)、[EF Core 10 目标框架](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew)。
- [Avalonia 12.1.2 发布](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.2)、[12.1 Wayland 状态](https://avaloniaui.net/blog/release-12-1)。

执行日期晚于本文日期时，先复核上述来源；后续新版本不能自动替代已经验证并记录的版本。
