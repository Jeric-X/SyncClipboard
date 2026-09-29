# 独立 NativeAOT 辅助程序的构建集成

本 PR 只包含辅助程序及主程序构建集成：Windows 使用控制台，macOS、Linux 使用 Avalonia 窗口和软件渲染。当前输出或显示占位说明，不包含更新协议、安装、回滚和重启流程，主程序尚未调用辅助程序。

## 构建与输出

三个桌面入口项目导入 `build/Updater.targets`。普通 `dotnet build` 和 `dotnet publish` 都自动发布同架构的 NativeAOT 辅助程序，再将可执行文件加入主程序输出。无需独立辅助程序 workflow、artifact 下载或按包类型注入；Windows EXE/ZIP、Linux AppImage/deb/rpm 均随桌面输出携带它，Server 不引用该构建目标。

主程序指定 RID 时辅助程序使用同一 RID；未指定时采用当前 SDK 的主机 RID。支持 win/linux/osx 的 x64、arm64。NativeAOT 需要对应系统的原生编译工具链，Linux CI 改用对应架构的 Ubuntu runner，Windows CI 同样使用对应架构 runner，以便启动验证。

辅助程序在独立 dotnet 进程中发布，避免继承主程序的 `net10.0-macos`/WinUI 目标框架、`OutDir`、`SelfContained=false` 等配置。发布到辅助项目自己的 bin 目录，由 SDK 的增量编译复用未变化的编译结果。设计时构建跳过发布，不影响 IDE 加载。主程序真实构建如果辅助程序发布失败会直接失败，不静默漏掉辅助文件。

Windows/Linux 输出：

```text
主程序输出目录/
  SyncClipboard.Updater[.exe]
  主程序及其已有依赖
```

macOS 使用 `BundleResource` 将可执行文件纳入 `.app/Contents/Resources/Updater/`，在 SDK 签名前复制。最终仍由 BundleTool 签名并制作 DMG，Homebrew 与直接下载共用产物。

## 依赖复用

Windows 辅助程序不引用 Avalonia。macOS/Linux 辅助程序沿用 `Directory.Packages.props` 的 Avalonia 版本，但主程序只收集辅助可执行文件，不复制其 publish 目录里的图形原生库、调试符号或其他文件，因此没有第二套 Skia/HarfBuzz。

Linux 运行需要主程序的 `libSkiaSharp.so`、`libHarfBuzzSharp.so`；macOS 还需要 `libAvaloniaNative.dylib`，对应另外两个库的 dylib 版本。库依然由主程序正常构建/打包提供。更改 Avalonia 或底层依赖后需再次验证完整依赖集合。

后续接入实际更新时，主程序应把当前版本的辅助可执行文件和所需原生库复制到独立工作目录，保留执行权限，再启动辅助程序。不得从新版本下载辅助程序却搭配旧版本的原生库，也不得在替换安装目录时持续依赖旧 `.app` 或 AppImage 挂载目录。本 PR 尚未实现该运行时复制流程。

## 验证

主程序 CI 在构建完成后做启动检查：Windows 只复制控制台 executable；macOS/Linux 从主程序输出复制辅助 executable 及上述原生库，到含中文和空格的临时目录，从其他工作目录运行 `--smoke-test`。macOS/Linux 显示真实 Avalonia 窗口后退出，Linux 使用 xvfb；Windows 验证控制台输出。检查直接验证主程序依赖复用，不使用辅助程序 publish 中的库。

不增加独立压缩包、产物报告或最终安装包解包检查。Windows portable ZIP 保持最高 Deflate 压缩级别。各平台仍使用已有格式检查和主程序打包 CI；实际安装能力由后续 PR 接入。
