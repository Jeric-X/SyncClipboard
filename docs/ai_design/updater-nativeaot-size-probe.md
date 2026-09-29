# 独立升级助手 NativeAOT 体积实验

本实验测量含最小 Avalonia 界面、文件操作、SHA256 和 ZIP 解压的独立程序。
允许原生库在启动时释放，交付目录只有一个可执行文件。它是体积验证程序，不接入正式升级流程，
不操作已安装的 SyncClipboard，也不实现 DMG 挂载、提权、进程退出等待或升级回滚。

## 固定条件

- .NET SDK 使用仓库 `global.json`（10.0.302，允许 latestPatch 滚动），Avalonia 使用中央包版本（12.1.3）。
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

## 三平台结果

Windows/Linux 在专用分支提交 `8e177b221b2182753ea0e053813b405cdc7878f8` 的
[CI 运行 36513015212](https://github.com/Jeric-X/SyncClipboard/actions/runs/36513015212) 完成。
该分支只触发了 `updater-size-probe`，两项任务均成功，无 trimming/AOT 编译警告。
macOS 本地发布出现 NuGet 漏洞信息源不可达的 NU1900 警告，未影响构建和运行验证。

| 平台 | 实际 SDK | 单文件字节 | 单文件 MiB | ZIP MiB | 运行时磁盘 MiB |
|---|---|---:|---:|---:|---:|
| macOS arm64 | 10.0.302 | 13,813,328 | 13.17 | 7.70 | 22.06 |
| Windows x64 | 10.0.303 | 16,249,344 | 15.50 | 9.32 | 28.32 |
| Linux x64 | 10.0.303 | 19,630,672 | 18.72 | 11.09 | 32.05 |

CI 遵循仓库 latestPatch 策略使用了 10.0.303，因此结果保留实际 SDK 信息；这些数字不是固定同一 SDK 补丁的跨平台对照。
三个平台均通过单文件隔离启动、真实桌面后端、文件/SHA256/ZIP 以及释放目录清理验证。
Windows 成功省去 5,394,096 字节的 ANGLE DLL；Windows/Linux 仅释放 SkiaSharp 与 HarfBuzzSharp，
macOS 额外释放 Avalonia.Native。原生库不会因托管 trimming 自动缩小；这里使用 NuGet 提供的原生库，
没有自行精简或重新编译 Skia/HarfBuzz。

这说明独立助手采用 NativeAOT 并在运行时释放原生库可行，当前最小实用界面配置的分发文件约 13–19 MiB，
下载 ZIP 约 8–11 MiB。后续正式助手可沿用启动封装，但应另行验证真实 DMG/AppImage/ZIP 的升级、回滚与重启逻辑。

## 同级动态库 ZIP 对照实验

按后续要求，新增 `--layout adjacent`：生成未内嵌原生库、未编译自解包与父子进程启动代码的 NativeAOT 可执行文件，
将所需原生库同级放置，统一使用 ZIP DEFLATE level 9 压缩。主程序未来负责解压，助手直接加载自身目录中的库。
这次只实现测试封装，没有改动正式主程序的释放或升级逻辑。

```sh
python3 build/updater-size/measure.py --rid osx-arm64 --layout adjacent
python3 build/updater-size/measure.py --rid linux-x64 --layout adjacent
python build/updater-size/measure.py --rid win-x64 --layout adjacent
```

结果写入 `artifacts/updater-size/<rid>/adjacent/`，保留首轮结果。
测试从实际生成的 ZIP 解压到含中文、空格的新目录，逐文件校验 SHA256，再在不同的工作目录运行功能和 GUI 冒烟测试。
须加载全部同级原生库且不出现内部解包行为。Unix 解压后显式设置主可执行文件权限为 `0755`；正式主程序也需要这一步。
CI 仍仅运行测试分支的 Windows/Linux 实验，macOS 在本机验证。

首轮 macOS 对照：ZIP 8,046,535 字节（7.67 MiB），解压后 19,105,392 字节（18.22 MiB），功能和 GUI 测试通过。
相比此前自解包可执行文件再压 ZIP 的 7.70 MiB，压缩体积变化很小；主要收益是去掉运行时内部解包及父子进程启动流程。

同级文件模式三平台结果（2026-09-29）：

| 平台 | SDK | 主程序 MiB | 解压后合计 MiB | ZIP 字节 | ZIP MiB | 旧自解包模式 ZIP MiB |
|---|---|---:|---:|---:|---:|---:|
| macOS arm64 | 10.0.302 | 9.33 | 18.22 | 8,046,535 | 7.67 | 7.70 |
| Windows x64 | 10.0.303 | 9.98 | 22.81 | 9,722,920 | 9.27 | 9.32 |
| Linux x64 | 10.0.303 | 13.18 | 26.51 | 11,586,519 | 11.05 | 11.09 |

Windows/Linux 来自提交 `5e35c50d849fce1508343c62ae3ae19843e40542` 的
[CI 运行 36514982300](https://github.com/Jeric-X/SyncClipboard/actions/runs/36514982300)，两项均成功，无编译警告。
产物名为 `updater-adjacent-size-win-x64` 和 `updater-adjacent-size-linux-x64`。
Linux ZIP 内恰好三个同级文件：`SyncClipboard.Updater.SizeProbe`、`libSkiaSharp.so`、`libHarfBuzzSharp.so`。
主程序未来可将 ZIP 释放到独立工作目录、设置执行权限、启动助手；本轮仅用测试脚本模拟该流程。
