# NativeAOT 更新器的构建与依赖复用

## 项目职责

- `SyncClipboard.Updater.Core`：交互接口、参数、语言，以及 ZIP、DMG、AppImage 的校验、安装、回滚、重启和清理；不引用界面框架或主程序业务项目。
- `SyncClipboard.Updater.WinUI`：WinUI 主程序对应的 Windows 更新界面。
- `SyncClipboard.Updater.Avalonia`：Avalonia 桌面入口对应的更新界面，包括 macOS、Linux 和 Windows Avalonia。

两个界面项目引用 Core，导入 `src/SyncClipboard.Updater.Core/Updater.props` 共用 NativeAOT 发布配置。输出统一命名为 `SyncClipboard.Updater`，Core 被链接到可执行文件中，不向应用包添加 `SyncClipboard.Updater.Core.dll`。

## 构建与打包

桌面入口导入 `src/SyncClipboard.Updater.Core/Updater.targets`，随主程序发布同架构更新器，只收集一个可执行文件。Windows/Linux 放在主输出目录，macOS 通过 BundleResource 放在 `.app/Contents/Resources/Updater/` 并随应用签名。Server 不参与。

所有 WinUI 组合都打包更新器，包括未附带 .NET 或 Windows App SDK 的组合。更新器始终为 NativeAOT；只有 `WindowsAppSDKSelfContained` 跟随主程序，使用 SDK 的标准自动初始化机制：

- 自包含 SDK：EXE 含本地 WinRT 激活清单，使用临时目录里的原生库。
- 不包含 SDK：EXE 通过 bootstrap 使用系统安装的 SDK；原生 bootstrap DLL 来自主程序输出，托管 bootstrap 程序集被 AOT 链接进 EXE。

两种 SDK 模式分别编译 EXE。自包含构建的激活清单绑定本地 DLL，不能只在运行时调用 bootstrap 就切换成系统运行库。每个发布包仍只新增一个 EXE。

WinUI 更新器在 Debug 配置下也开启编译优化，避免 .NET 10 NativeAOT 在 WinUI 引用跟踪回调中触发 GC 死锁（[dotnet/runtime#121538](https://github.com/dotnet/runtime/issues/121538)）。界面冒烟测试在窗口和对话框存活时强制执行 GC，验证该路径能够完成。

发布在独立 dotnet 进程进行，`--artifacts-path` 按入口项目、配置、TFM、RID、WinUI SDK 模式隔离中间产物。主程序的 .NET `SelfContained` 等全局属性不会污染更新器。发布失败直接阻止主程序构建，设计时构建跳过。

## 运行时依赖复用

`FileReplacementPackageInstaller` 创建独立临时目录和标记，复制当前版本更新器及所需依赖，然后启动更新器。Windows 的依赖名单集中在 `WindowsUpdaterFiles`，只复制主程序输出中存在的 DLL，缺失时直接跳过，不据此禁止安装或漏打包更新器。无 SDK 的包使用系统运行库。

WinUI 自包含输出的 `SyncClipboard.pri` 含合并后的主题资源，复制为临时目录的 `resources.pri`。Debug 构建的 PRI 引用外部 `App.xbf`，该文件存在时一并复制；Release 通常将其嵌入 PRI。只有 EXE 和 DLL 不足以绘制 WinUI 控件；PRI 和 XBF 同样复用主程序编译产物，不在发布包新增另一份。Windows Avalonia 复用 Skia/HarfBuzz，macOS/Linux 保留原有 dylib/so 复制及权限处理。

更新器替换安装目录时，不加载原安装目录里的 DLL。WinUI 会持续映射临时目录的 DLL，因此成功更新后启动系统 PowerShell 清理进程，等待更新器退出，再验证目录命名和标记，以 LiteralPath 删除。路径、PID 和进程启动时间通过环境变量传递，不拼接进脚本。安装失败或主动终止仍保留工作目录和备份。

## 验证

平台无关测试由 core workflow 执行；依赖真实系统行为的测试由 Windows、macOS、Linux 独立 workflow 按平台分类执行。各平台复用主程序构建产物运行更新器 GUI smoke，业务逻辑通过无 UI 的交互接口验证。

更改 Windows App SDK、WinUI 控件或 Avalonia 版本后，应重新验证原生依赖集合。运行库初始化遵循 [Microsoft 官方文档](https://learn.microsoft.com/windows/apps/windows-app-sdk/use-windows-app-sdk-run-time)。
