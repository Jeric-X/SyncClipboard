# .NET 10 与主要依赖升级总结

升级围绕 .NET 10、桌面 UI 框架和开发工具展开，按技术主题拆分改动，保留业务行为与配置格式。本文记录版本变化、兼容性适配和升级范围的取舍。

## 版本变化

| 范围 | 升级内容 |
| --- | --- |
| .NET | 从 .NET 8 迁移到 .NET 10；SDK 使用 10.0.302 和 latestPatch，macOS workload 固定版本；同步调整目标框架、CI 与容器构建 |
| 微软基础库与 MVVM | Microsoft.Extensions 和 SignalR 客户端更新到 10.0.12；CommunityToolkit.Mvvm 更新到 8.4.2 |
| Windows UI | Windows App SDK 2.4.0、WinUI Toolkit 8.2.251219、WinUIEx 2.9.3、H.NotifyIcon.WinUI 2.4.1 |
| Avalonia | 11.3.18 → 12.1.2；FluentAvaloniaUI 2.3.0 → 3.1.0；AsyncImageLoader.Avalonia 3.3.0 → 3.8.0 |
| 数据库 | EF Core/Sqlite/Design 9.0.8 → 9.0.20；SQLitePCLRaw 传递依赖更新到 2.1.12，保留 EF Core 9 主版本 |
| 测试工具 | MSTest.TestFramework/TestAdapter 3.10.4 → 4.4.0；Microsoft.NET.Test.Sdk 经 18.4.0 更新到 18.10.1；coverlet.collector 更新到 10.0.1 |
| 容器开发工具 | Microsoft.VisualStudio.Azure.Containers.Tools.Targets 1.22.1 → 1.23.0 |

## 兼容性适配与问题修复

- **历史查询：** 显式使用适合表达式树的 LINQ Contains 调用，避免 .NET 10 下重载选择引入 ReadOnlySpan，导致 EF Core 在切换历史分类或统计选择项时抛出异常。
- **Avalonia 控件与 API：** 迁移 FluentAvalonia 的 FA 前缀控件和事件类型，以内置 FABreadcrumbBar 替换独立 BreadcrumbBar 包；适配布局、样式、绑定、焦点、窗口装饰和已移除的 API。
- **图片与拖拽：** 图片读取显式使用 PNG 编码；历史列表和预览面板保留首次按下事件并传入拖拽 API，在拖拽结束或取消后清理状态。
- **关于页进度动画：** 修复缓存页面离开后再次显示时，不确定进度动画不能恢复的问题，并在代码注释中关联上游 PR。
- **服务器配置：** 首次生成 appsettings.json 后重新加载已有配置源，避免追加 JSON 配置源覆盖环境变量和命令行参数，保持命令行 > 环境变量 > 配置文件的优先级。
- **测试兼容：** 修正阻塞 CI 的 ZIP 测试问题；测试工具版本升级保持既有业务测试范围。

## 保留的实现与依赖

| 范围 | 取舍 |
| --- | --- |
| 图片处理 | 保留 Magick.NET 14.9.1 和 Magick.NET.SystemDrawing 8.0.15 |
| S3 | 保留 AWSSDK.S3 3.7.414，不迁移 v4；S3 签名、初始化与进度修复不包含在此次升级范围内 |
| 全局输入与原生调用 | 保留 SharpHook 5.2.3、Vanara 4.0.1 |
| 后台调度 | 保留 Quartz 3.14.0，不引入 Quartz 4 的生命周期改造；额外的任务取消和调度初始化修复不包含在升级范围内 |
| API 文档 | 保留 Swashbuckle.AspNetCore 8.1.1 |
| Linux 打包 | 保留 PupNet 1.8.0 和既有 RPM/DEB 依赖声明，ARM64 RPM 打包问题不在升级范围内 |
| MVVM 属性 | 保留字段生成属性的写法和 MVVMTK0042 silent，避免批量 partial 属性迁移扩大改动 |

系统最低版本声明未作调整；依赖升级带来的实际系统兼容性由目标系统测试确认。升级验证采用本地编译、非 UI 测试及各平台 PR CI，UI 由人工验收，不下载 CI 产物进行额外二进制审计。

README 的运行时说明与正式发布版本对应：在 .NET 10 源码迁移与正式发布之间，服务器说明使用 ASP.NET Core 8.0，避免用户按开发分支信息安装错误的运行时；不新增 no-dotnet-runtime 使用说明。

## 关联 PR

- [#419](https://github.com/Jeric-X/SyncClipboard/pull/419)：集中升级探索，为拆分范围和兼容修复提供参考。
- [#420](https://github.com/Jeric-X/SyncClipboard/pull/420)：.NET 10 工具链、目标框架、构建与容器。
- [#421](https://github.com/Jeric-X/SyncClipboard/pull/421)：微软基础库、MVVM、WinUI 依赖及历史查询兼容修复。
- [#422](https://github.com/Jeric-X/SyncClipboard/pull/422)：Avalonia 12、配套控件适配与 EF Core 补丁升级。
