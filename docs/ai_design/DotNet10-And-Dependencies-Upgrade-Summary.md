# .NET 10 与主要依赖升级最终配置

核对日期：2026-09-14。升级基线 `cc7289d1bc7528ef1a8a63af4ccc7f8f55a6fd6a`。本文件汇总最终代码配置；分步证据见[执行记录](DotNet10-And-Dependencies-Upgrade-Execution.md)，最终提交 SHA、CI 运行链接和交付结论集中记录在 [PR #419 描述](https://github.com/Jeric-X/SyncClipboard/pull/419)，避免把旧提交的成功结果写成新提交证据。

## 工具链与框架

- SDK 固定 10.0.302，global.json 仅允许 latestPatch，不接受预发行；产品和三个测试项目全部使用 .NET 10。
- Shared、Core、Server.Core、Server、Desktop、Core/Desktop 测试为 net10.0。Desktop.Default 为 net10.0 和 net10.0-windows10.0.17763.0；WinUI3 与其测试为 net10.0-windows10.0.19041.0；MacOS 为 net10.0-macos。
- macOS CI 使用 Xcode 26.6、macos workload set 10.0.302.1；Windows CI 使用 windows-2025-vs2026 和 MSBuild。不能在 macOS 上构建整套 solution。
- PupNet 1.10.0 使用 .NET 10；AppImage 构建安装 appimagetool 的 1.9.1 发布包与 libfuse2，RPM 安装 rpm。保留原 Linux 10 种包组合及 arm64 RPM 排除。
- 容器使用 SDK 10.0.302-noble、aspnet 10.0-noble，原生 Ubuntu amd64/arm64 构建；运行时标签在 .NET 10 内接收补丁。ContainerBuildContext 为 `..`，对应 `src`。

## 直接依赖

以下版本来自[中央包配置](../../src/Directory.Packages.props)，各组的版本依据、迁移和独立 PR 验证见执行记录。保留旧版本的包已在对应步骤核定为可用稳定版，不虚构升级。

| 包 | 基线 | 最终版本 |
| --- | --- | --- |
| AWSSDK.S3 | 3.7.414 | 4.0.103.2 |
| AsyncImageLoader.Avalonia | 3.3.0 | 3.8.0 |
| Interop.UIAutomationClient | 10.19041.0 | 10.19041.0 |
| Microsoft.AspNetCore.SignalR.Client | 9.0.9 | 10.0.12 |
| Microsoft.Extensions.Logging.Console | 9.0.8 | 10.0.12 |
| NativeNotification | 1.0.5 | 1.0.5 |
| NativeNotification.Interface | 1.0.5 | 1.0.5 |
| Quartz | 3.14.0 | 4.1.0 |
| Magick.NET-Q16-x64 | 14.9.1 | 14.17.1 |
| Magick.NET-Q16-x86 | 14.9.1 | 14.17.1 |
| Magick.NET-Q16-arm64 | 14.9.1 | 14.17.1 |
| Magick.NET-Q16-AnyCPU | 14.9.1 | 14.17.1 |
| Magick.NET.SystemDrawing | 8.0.15 | 8.0.27 |
| System.Drawing.Common | 新增直接引用 | 10.0.0 |
| CommunityToolkit.Mvvm | 8.4.0 | 8.4.2 |
| ObservableCollections | 3.3.4 | 3.3.4 |
| Microsoft.Extensions.DependencyInjection | 9.0.9 | 10.0.12 |
| Microsoft.Toolkit.Uwp.Notifications | 7.1.3 | 7.1.3 |
| Swashbuckle.AspNetCore | 8.1.1 | 10.2.3 |
| Microsoft.OpenApi | 新增直接引用 | 2.12.2 |
| CodeCracker.CSharp | 1.1.0 | 1.1.0 |
| Microsoft.VisualStudio.Azure.Containers.Tools.Targets | 1.22.1 | 1.23.0 |
| Microsoft.EntityFrameworkCore | 9.0.8 | 10.0.12 |
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.8 | 10.0.12 |
| Microsoft.EntityFrameworkCore.Design | 9.0.8 | 10.0.12 |
| Avalonia | 11.3.18 | 12.1.2 |
| Avalonia.Desktop | 11.3.18 | 12.1.2 |
| Avalonia.Themes.Fluent | 11.3.18 | 12.1.2 |
| Avalonia.Fonts.Inter | 11.3.18 | 12.1.2 |
| FluentAvaloniaUI | 2.3.0 | 3.1.0 |
| SharpHook | 5.2.3 | 8.0.0 |
| Microsoft.NET.Test.Sdk | 17.14.1 | 18.10.0 |
| MSTest.TestAdapter | 3.10.4 | 4.4.0 |
| MSTest.TestFramework | 3.10.4 | 4.4.0 |
| coverlet.collector | 6.0.4 | 10.0.1 |
| Moq | 4.20.72 | 4.20.72 |
| CommunityToolkit.WinUI.Controls.SettingsControls | 8.2.250402 | 8.2.251219 |
| CommunityToolkit.WinUI.Converters | 8.2.250402 | 8.2.251219 |
| H.NotifyIcon.WinUI | 2.3.0 | 2.4.1 |
| Microsoft.WindowsAppSDK | 1.8.260529003 | 2.4.0 |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.4948 | 10.0.28000.2705 |
| WinUIEx | 2.3.4 | 2.9.3 |
| Vanara.PInvoke.ComCtl32 | 4.0.1 | 5.0.7 |
| Vanara.PInvoke.DbgHelp | 4.0.1 | 5.0.7 |
| Vanara.PInvoke.Kernel32 | 4.0.1 | 5.0.7 |
| Vanara.PInvoke.User32 | 4.0.1 | 5.0.7 |

移除的独立引用：`Quartz.Extensions.DependencyInjection`、`System.Net.Http.Json`、`Avalonia.Diagnostics`、`FluentAvalonia.BreadcrumbBar`。其中 System.Net.Http.Json 由 .NET 10 共享框架提供；面包屑控件改用 FluentAvalonia 的 FABreadcrumbBar；旧 Avalonia 调试工具被移除；Quartz DI 集成使用 Quartz 4 主包。

关键传递依赖经 NuGet assets 核对，保持实际解析：AWSSDK.Core 4.0.102.4；Microsoft.Data.Sqlite.Core 10.0.12、SQLitePCLRaw 2.1.12；SkiaSharp 3.119.4、HarfBuzzSharp 8.3.1.3；FluentAvalonia 配套 Avalonia.Controls.ColorPicker/DataGrid 12.1.0、Avalonia.BuildServices 11.3.2。不要把独立组件的版本机械替换为主包版本。

## 支持系统与安装说明

| 入口 | 系统与运行时 |
| --- | --- |
| Windows WinUI3 正式包 | 最低 Windows 10 2004/build 19041，安装器 MinVersion 与项目一致；实际 OS 还须处于 .NET 10 支持范围。提供 x64/arm64 现有组合。 |
| Windows Desktop.Default | 保留开发/兼容入口的 build 17763 TFM 和现有编译覆盖；不把它的 TFM 当作 WinUI3 安装包支持声明。 |
| macOS | 项目 SupportedOSPlatformVersion 为 14.0；Apple Silicon arm64、Intel x64；自包含 .NET 运行时。 |
| Linux desktop | x64/arm64、glibc >= 2.38、OpenSSL >= 1.1.1 和 README 所列 X11/XTest/Xt/Xrandr/xkbcommon 库；保留 XRecord/XWayland 行为，不启用原生 Wayland 或额外输入设备权限。 |
| Standalone Server | ASP.NET Core 10 运行时及其 .NET 10 支持系统；Linux 桌面的 SharpHook 原生依赖要求不适用于独立服务器。 |

Windows/Linux 的 `no-dotnet-runtime` 包需要相同架构的 ASP.NET Core 10 运行时（含 .NET）；Windows `no-win-app-sdk` 包还需要 Windows App SDK 2.4 运行时。两个标记同时出现就安装两者；未出现的标记对应运行时已随包携带。中英文 README 均已同步。

系统依据：[.NET 10 支持列表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)、[Avalonia 平台分级](https://docs.avaloniaui.net/docs/supported-platforms)、[Windows App SDK 运行时](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)、[Inno Setup 最低版本语法](https://jrsoftware.org/ishelp/topic_setup_minversion.htm)。Avalonia 将 macOS 26 列为 Tier 1、14/15 列为 Tier 2；分级不等于本项目执行过对应 UI 验收。

H.NotifyIcon 上游 #271 的实际 UI 行为限制沿用步骤 9c 记录。本次 UI 检查统一为“范围排除：按用户要求不验证”，没有人工验收、解锁补测或隐藏窗口测试任务。

## 活动构建配置

[build-entry-pr.yml](../../.github/workflows/build-entry-pr.yml) 与 [build-entry.yml](../../.github/workflows/build-entry.yml) 保留既有入口及构建矩阵。它们调用预处理、格式、非 UI 测试、Server/容器、Windows、Linux、macOS 构建和打包；CodeQL 独立执行。Windows 安装器使用 build/windows 的 PowerShell/Inno 脚本；Linux 使用 package.sh、PostPublish.sh 和 PupNet；macOS 使用项目发布及 BundleTool/打包脚本。发布工作流与更新源参数保持既有行为，不通过打标签或发布来验证。

`build/appveyor/` 保留为已标注的遗留参考，其旧目录/TFM/VS2022 配置不是受支持的构建入口；当前 GitHub 工作流不引用它，基线和本 PR 的检查也没有 AppVeyor。没有修改仓库外 AppVeyor 账户设置，也不声称已核实该外部账户。旧配置不作为本次升级的活动脚本或验证证据。

## 最终验证范围

| 要求 | 证据 |
| --- | --- |
| R1 数据逻辑 | Core 444 项 NonUI 回归覆盖 Profile/DTO、序列化、哈希、历史文件与转换逻辑；真实系统剪贴板不执行。 |
| R2 桌面代码兼容性 | macOS 发布、Desktop C#/XAML 编译及 PR 全平台构建；三平台 Desktop 各6项、WinUI58项 NonUI 回归；UI 显示/交互不执行。 |
| R3 旧数据 | 保留冻结 net8/EF9 数据基线，仅运行副本；核对旧记录、字段、时间、排序、分页、收藏、清理与文件数据。 |
| R4 协议 | 本地源码编译的旧/新客户端与服务器互通，隔离 HTTP 代理、TLS、实际内置服务器自动重连；独立 Server、Swagger JSON、S3 MinIO 与故障代理回归。 |
| R5 构建与打包 | 当前 head 全部必要 PR/push 任务实际成功；Windows、Linux、macOS 原矩阵保留，原生双架构容器验证成功。 |

CI 五份 TRX 合计为520项：444+3×6+58，均使用 `--filter TestCategory=NonUI`，不计入 UI 排除用例。覆盖率要求实际命中产品程序集；Quartz 三平台探针保留10组、六项提前取消、两项实际查询开始后的取消。清理取消/异步关闭验证在临时目录、数据库和内存任务中完成，不执行真实应用重启。

完全信任 CI 编译和打包产物，不下载或复用远程产物进行二进制、签名、包结构或哈希审计。正常还原依赖、安装构建工具、CI 内部传递打包输入、读取测试报告和协议数据完整性检查仍属于验证流程。只有具体问题确需额外二进制检查时才使用本地编译的 macOS 版本；不例行补做。

每次最终修复都必须重新验证受影响部分并等待新 head 的全部 CI 与完整评审。最终交付后删除 PR heartbeat，不自动合并、修改 master、强推、打标签或发布。回退使用独立 revert 提交；代码回退不能替代测试数据库/配置副本的恢复。
