# 独立升级助手 NativeAOT 体积实验

本实验测量含最小 Avalonia 界面、文件操作、SHA256 和 ZIP 解压的独立程序。
允许原生库在启动时释放，交付目录只有一个可执行文件。它是体积验证程序，不接入正式升级流程，
不操作已安装的 SyncClipboard，也不实现 DMG 挂载、提权、进程退出等待或升级回滚。

## 固定条件

- .NET SDK 使用仓库 `global.json`（10.0.302），Avalonia 使用中央包版本（12.1.3）。
- `net10.0`、Release、NativeAOT、`TrimMode=full`、`IlcOptimizationPreference=Size`、剥离调试符号。
- 开启 invariant globalization；测试中英文显示和中文文件名，但不验证区域相关排序、日期格式。
- 仅引用 `Avalonia.Desktop`，不引用业务项目、DI、MVVM、完整主题或打包字体。
- 软件渲染，使用系统字体；文字、简单进度条及错误信息全部由 C# 创建。
- Windows 的 ANGLE DLL 不进入内嵌包，须通过真实桌面后端的隔离启动测试验证。
- macOS 将 NuGet 的 universal dylib 裁成 arm64 并重新 ad-hoc 签名。尚不包含发行签名、公证。

## 单文件实现

先常规 AOT 发布，收集原生动态库，使用固定顺序、时间戳和 DEFLATE level 9 生成 `native.zip`；
然后第二次 AOT 发布，将该 ZIP 内嵌为托管资源。最终只分发第二次发布的原生可执行文件。
这不是 .NET `PublishSingleFile` 打包器，也不依赖已安装的 .NET 运行时。

启动父进程在随机私有临时目录解包，重新启动同一可执行文件作为 UI 子进程。
子进程为 SkiaSharp、HarfBuzzSharp、Avalonia.Native 和 Win32 注册原生库路径解析器。
父进程等待子进程结束后删除释放目录，避免 Windows 的 DLL 文件占用。
多个实例使用各自目录。强制杀死父进程可能残留临时目录，正式集成前还需完善异常清理。
系统动态库、系统字体仍由操作系统提供，Linux 仍要求相应桌面库。

## 重现

在对应系统及架构执行（Linux 还需安装工作流列出的桌面库、clang 和 Xvfb）：

```sh
python3 build/updater-size/measure.py --rid osx-arm64
python3 build/updater-size/measure.py --rid linux-x64
python build/updater-size/measure.py --rid win-x64
```

macOS 在本机编译并运行；CI 只构建 Linux x64、Windows x64 两个实验项目。
`codex/updater-size-probe` 分支被主构建工作流排除；不创建 PR，避免触发 PR 工作流。
所有输出位于忽略的 `artifacts/updater-size/<rid>/`：

- `delivery/`：只有一个可执行文件；`delivery.zip`：可下载的压缩分发物。
- `report.json` / `report.md`：原始字节、MiB、SHA256、库清单和测试结论。
- `*-publish.log`：完整构建输出，保留 trimming/AOT 警告用于审查。
- `self-test.log` / `smoke-test.log`：功能、原生库加载和退出结果。

## 验证与统计口径

脚本将唯一可执行文件复制到新的空目录，从该目录直接运行，不通过 `dotnet`。
先执行 `--self-test`，再执行 `--smoke-test`，Linux GUI 使用 Xvfb 和真实 X11 后端。
两次执行都要求返回 0、有成功标记且释放目录已删除；GUI 还必须实际加载内嵌的 Skia 库。
GUI 启动测试不等价于人工视觉验收，CI 也不是完全没有 SDK 的操作系统镜像。

功能校验：写入/读取文件、SHA256 `abc` 已知向量、复制、覆盖移动、删除、ZIP 打包解压、
目录移动和清理，路径包含中文和空格。界面显示测试状态、进度及错误信息。

同时报告原生主程序、原始发布所需文件总和、原生库释放大小、单文件大小、ZIP 大小及运行时磁盘占用。
大小按文件字节之和统计，1 MiB = 1,048,576 字节，不使用 GitHub artifact 页面显示的压缩大小。
不计入 `.pdb` / `.dSYM` 等调试符号。macOS 原始发布总和含 universal 库，不能把裁架构带来的收益归因于 trimming。
最终数字是本实验配置的实测值，不代表所有可能配置的理论最小值或完整升级程序的大小。

## macOS 首轮实测

2026-09-29，本机 macOS 26.6.2 / arm64，文件和真实窗口冒烟测试通过：

| 项目 | 字节 | MiB |
|---|---:|---:|
| 原生主程序（未内嵌库） | 9,784,400 | 9.33 |
| 常规发布总和（含 universal 库） | 29,474,224 | 28.11 |
| 释放的 arm64 原生库 | 9,321,008 | 8.89 |
| 单文件 | 13,813,328 | 13.17 |
| 单文件 ZIP | 8,074,611 | 7.70 |
| 运行时磁盘占用（单文件 + 释放库） | 23,134,336 | 22.06 |

Windows/Linux 结果以 CI 的 `report.json` 为准，完成后补充。
