# Core 非 UI 测试范围

核对日期：2026-09-14，升级步骤 13b。适用项目：`src/SyncClipboard.Test`。

本项目的 38 个测试类已核对测试入口、字段初始化、初始化/清理方法及使用的替身，均标为 `TestCategory("NonUI")`。本地与 CI 使用 `--filter TestCategory=NonUI` 正向筛选。分类后的 427 项全部通过，包含前一阶段全部 411 项和新增 16 项集合回归，没有因筛选漏掉已有用例。

项目没有 AssemblyInitialize/ClassInitialize、动态数据源或平台服务数据源的测试入口。SystemServiceProviderDataSource/PlatformServiceProviderDataSource 在本项目仅定义，未被测试方法使用；不能据此运行其他项目的混合 DI 测试。被测项目引用 Core，无桌面入口或 Avalonia/WinUI 控件初始化。测试涉及图片时只处理数据、文件和哈希。

| 测试类 | 实际用例数 | 非 UI 边界 |
| --- | ---: | --- |
| [ClipboardChangingListenerBaseTest](../../src/SyncClipboard.Test/ClipboardChangingListenerBaseTest.cs) | 2 | 事件注册替身与内存剪贴板工厂；不订阅系统剪贴板。 |
| [ConfigRecoveryServiceTests](../../src/SyncClipboard.Test/ConfigRecoveryServiceTests.cs) | 12 | 临时配置文件与 FakeGlobalDialog/FakeLogger。 |
| [DelegateExtentionTests](../../src/SyncClipboard.Test/DelegateExtentionTests.cs) | 6 | 委托、异常与取消令牌。 |
| [FileFilterHelperTests](../../src/SyncClipboard.Test/FileFilterHelperTests.cs) | 8 | 规则匹配和纯数据编辑模型；不创建控件。 |
| [FileSyncFilterSettingViewModelTests](../../src/SyncClipboard.Test/FileSyncFilterSettingViewModelTests.cs) | 3 | 临时 ConfigBase 与规则列表；不解析窗口服务。 |
| [ForegroundWindowMonitorTests](../../src/SyncClipboard.Test/ForegroundWindowMonitorTests.cs) | 4 | FakeWatcher/FakeProvider；不读取真实前台窗口。 |
| [ForegroundWindowTrackingServiceTests](../../src/SyncClipboard.Test/ForegroundWindowTrackingServiceTests.cs) | 13 | FakeMonitor/FakeProvider 与窗口信息记录；不激活真实窗口。 |
| [GroupProfileFileNamesTests](../../src/SyncClipboard.Test/GroupProfileFileNamesTests.cs) | 8 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [GroupProfileTransferTests](../../src/SyncClipboard.Test/GroupProfileTransferTests.cs) | 30 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [HistorySqliteTimeTests](../../src/SyncClipboard.Test/HistorySqliteTimeTests.cs) | 6 | 内存 SQLite 与时间值读写。 |
| [HistoryTransferDataHashTests](../../src/SyncClipboard.Test/HistoryTransferDataHashTests.cs) | 24 | 临时 SQLite/文件、服务器控制器和 SignalR 测试替身。 |
| [HistoryViewModelSelectionTests](../../src/SyncClipboard.Test/HistoryViewModelSelectionTests.cs) | 3 | 只调用静态索引计算，不构造 HistoryViewModel。 |
| [LoggerTests](../../src/SyncClipboard.Test/LoggerTests.cs) | 2 | 临时日志目录与 DecliningGlobalDialog。 |
| [MainViewModelNavigationTests](../../src/SyncClipboard.Test/MainViewModelNavigationTests.cs) | 4 | RecordingWindow 仅记录导航参数，其余界面方法抛出异常。 |
| [MvvmCompatibilityTests](../../src/SyncClipboard.Test/MvvmCompatibilityTests.cs) | 13 | 生成属性/命令与纯输入模型；不解析平台服务。 |
| [MvvmInitializationTests](../../src/SyncClipboard.Test/MvvmInitializationTests.cs) | 12 | 临时配置、严格 mock 和注入的启动项状态；不使用真实桌面后端。 |
| [NativeNotificationTests](../../src/SyncClipboard.Test/NativeNotificationTests.cs) | 5 | 严格通知接口 mock；回调仅修改内存记录，不创建通知管理器。 |
| [NetworkRuleMatcherTests](../../src/SyncClipboard.Test/NetworkRuleMatcherTests.cs) | 25 | 网络快照、规则状态与 FakeNetworkContextProvider；不查询设备或权限。 |
| [ObservableCollectionsCompatibilityTests](../../src/SyncClipboard.Test/ObservableCollectionsCompatibilityTests.cs) | 16 | 纯 HistoryRecord、同步集合与假的调度器；不构造历史窗口/ViewModel。 |
| [PrepareTransferDataInfoTests](../../src/SyncClipboard.Test/PrepareTransferDataInfoTests.cs) | 9 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [ProfileActionBuilderTest](../../src/SyncClipboard.Test/ProfileActionBuilderTest.cs) | 7 | 只构造和检查动作描述，不执行打开浏览器/文件等真实动作。 |
| [ProfileDataCompletenessTests](../../src/SyncClipboard.Test/ProfileDataCompletenessTests.cs) | 18 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [ProfileDtoTests](../../src/SyncClipboard.Test/ProfileDtoTests.cs) | 2 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [ProfileDtoTransferDataHashTests](../../src/SyncClipboard.Test/ProfileDtoTransferDataHashTests.cs) | 14 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [ProfileLocalizationTests](../../src/SyncClipboard.Test/ProfileLocalizationTests.cs) | 18 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [ProfileTransferDataReplacementTests](../../src/SyncClipboard.Test/ProfileTransferDataReplacementTests.cs) | 21 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [ProfileTransferValidationTests](../../src/SyncClipboard.Test/ProfileTransferValidationTests.cs) | 17 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [ProfileTryLocalizeTests](../../src/SyncClipboard.Test/ProfileTryLocalizeTests.cs) | 22 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [ProfileWorkingDirectoryTests](../../src/SyncClipboard.Test/ProfileWorkingDirectoryTests.cs) | 3 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [SetTransferDataInfoTests](../../src/SyncClipboard.Test/SetTransferDataInfoTests.cs) | 21 | Profile/DTO/传输数据、序列化与临时文件检查；不访问系统剪贴板或启动应用。 |
| [SharpHookKeyboardTests](../../src/SyncClipboard.Test/SharpHookKeyboardTests.cs) | 25 | 键码映射与注入的 backend/simulation/hook provider mock，构造和释放均不访问设备。 |
| [SingletonTaskTest](../../src/SyncClipboard.Test/SingletonTaskTest.cs) | 2 | 隔离的任务互斥和取消逻辑。 |
| [StorageBasedServerHelperTests](../../src/SyncClipboard.Test/StorageBasedServerHelperTests.cs) | 14 | TestStorageAdapter 复制临时文件，TestTrayIcon/TestLogger 为空操作。 |
| [SyncClipboardConfigRegistryTests](../../src/SyncClipboard.Test/SyncClipboardConfigRegistryTests.cs) | 3 | 配置注册表和类型元数据。 |
| [SyncClipboardConfigUpgraderTests](../../src/SyncClipboard.Test/SyncClipboardConfigUpgraderTests.cs) | 18 | 临时 JSON、迁移锁和备份目录。 |
| [UtilityExceptionTests](../../src/SyncClipboard.Test/UtilityExceptionTests.cs) | 1 | 异常类型及错误原因映射。 |
| [UtilitySHA256Tests](../../src/SyncClipboard.Test/UtilitySHA256Tests.cs) | 11 | 内存/临时文件哈希和取消。 |
| [WebDavBaseTest](../../src/SyncClipboard.Test/WebDavBaseTest.cs) | 5 | HttpClient 使用 DelegateHandler/RecordingHandler，不访问真实 WebDAV 服务。 |

验证证据：`/tmp/syncclipboard-stage13b-final-results/core/` 的 TRX；与步骤 13a 用例逐项比较的结果为 `/tmp/syncclipboard-stage13b-core-nonui-inventory.json`，缺失项为 0。源码清单与核对时的 SHA256 记录于 `/tmp/syncclipboard-stage13b-core-nonui-sources.json`。

`NonUI` 分类适用于测试的初始化、执行和清理全过程。修改上述类或加入用例时须重新核对边界；需要 UI 的用例应放入单独的类，不能放入已有类级 NonUI 分类中再依靠 RequiresUI 标签抵消。本次不执行 UI 用例，也不把 mock/编译结果视为实际界面验收。
