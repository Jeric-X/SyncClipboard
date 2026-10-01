# 更新器平台测试

业务测试统一位于 `src/SyncClipboard.Test`，引用 `SyncClipboard.Updater.Core`，以测试用 `IUpdateInteraction` 提供选择、记录结果，不启动更新器 UI。GUI 只检查已发布更新器的 `--smoke-test`：真实创建窗口，Windows 还创建并关闭对话框。

## 分类与 CI

| 分类 | 执行位置 | 覆盖内容 |
| --- | --- | --- |
| 无平台分类 | `core-test.yml` | 参数解析、包校验、工厂选择、状态转换，以及不依赖特定系统的业务测试 |
| `PlatformWindows` | `windows-test.yml` | ZIP 准备和替换、ACL/只读/占用错误、回滚、进程等待、锁、自清理和 core 的 Windows 分支 |
| `PlatformMacOS` | `macos-test.yml` | 真实 DMG、签名检查、bundle 交换和回滚、重启、卸载挂载点、清理和 core 的 macOS 分支 |
| `PlatformLinux` | `linux-test.yml` | 真实 AppImage、执行权限、替换和回滚、重启、进程取消，以及输入设备权限等 core 的 Linux 分支 |
| `UpdaterSmoke` + 平台分类 | 对应 workflow 的 smoke 步骤 | 使用主程序产物组装并启动 NativeAOT 更新器，确认 `GUI_SMOKE=PASS` 和退出码 |

目录用于组织代码，`TestCategory` 决定筛选。适用于多个系统的测试标注多个平台分类；core 流程排除全部平台分类，业务步骤排除 `UpdaterSmoke`。CI 依据 `dotnet test` 的退出码判定结果，测试诊断通过 CI 控制台日志查看，不生成或上传 TRX 附件。修改 filter 时使用 `--list-tests` 核对实际选择的用例。

Windows 保留 `function-test` 和 `smoke-test` 两个 job。Linux 将两类测试合并到 `function-test` job，内部步骤分别为 `function-test`、`smoke-test`。macOS 同样使用一个 `function-test` job，在 ARM64 runner 上依次执行功能测试和 smoke。

三个平台 workflow 接收所有需要对应操作系统的测试，更新器集成测试只是其中一部分。它们由 push/PR 构建入口调用，等待对应系统的构建产物，不再编译一套测试专用更新器。正式 release 等待它们成功。

- 功能测试在 Windows/Linux 的原生 x64 runner、macOS 的原生 ARM64 runner 上执行。
- Windows smoke 只使用 x64、携带 .NET 的产物，覆盖是否携带 Windows App SDK，共 2 组。未携带 App SDK 的环境按 NuGet 锁定版本安装运行时 MSIX；若当前用户已安装相同包名、发布者和架构的相同或更高版本，则跳过该包。
- macOS smoke 仅在 ARM64 执行，挂载真实构建 DMG，从 `.app` 提取更新器依赖，最终卸载 DMG。
- Linux smoke 只使用 x64 self-contained 产物，与功能测试共用一个 job；只有 smoke 使用 Xvfb，功能测试不需要显示服务器。
- Windows、Linux 选择的 self-contained 指主程序发行产物的打包方式；运行测试宿主的 runner 安装了 .NET SDK，并非无 .NET 的裸系统测试。

## 行为验证边界

业务测试调用实际包准备、文件替换、进程等待、重启和清理方法，验证安装文件、用户数据、备份、可执行权限及重启标记。DMG 和 AppImage 使用临时目录中的最小应用；ZIP 使用系统原生可执行文件验证包内容和架构。Windows 自清理使用真实子进程和文件占用，验证等待退出后删除。

业务测试不通过已发布 GUI 更新器驱动完整 `UpdateWorker.RunAsync`，因此与 smoke 分别覆盖业务步骤和发布依赖。现有 workspace 校验不为测试绕过；不增加生产测试参数、故障注入或 UI 自动点击。交互选择由测试实现返回，系统提权授权窗口仍需人工验证。

## 本地执行

```bash
# 跨平台 core 测试
dotnet test src/SyncClipboard.Test --filter 'TestCategory!=PlatformWindows&TestCategory!=PlatformMacOS&TestCategory!=PlatformLinux'

# 本机平台业务测试，按系统替换分类
dotnet test src/SyncClipboard.Test --filter 'TestCategory=PlatformMacOS&TestCategory!=UpdaterSmoke'

# 已发布 GUI smoke，按系统替换分类
dotnet test src/SyncClipboard.Test --filter 'TestCategory=PlatformMacOS&TestCategory=UpdaterSmoke'
```

Smoke 前设置：Windows 的 `SYNC_CLIPBOARD_UPDATER_TEST_EXE` 指向主程序输出中的 `SyncClipboard.Updater.exe`；macOS 的 `SYNC_CLIPBOARD_UPDATER_TEST_SOURCE` 指向 `.app`；Linux 的同名变量指向主程序发布目录。所有依赖通过生产代码的 `Prepare*UpdaterAsync` 复制，测试不维护第二份依赖列表。

Linux 业务测试还需要 `SYNC_CLIPBOARD_APPIMAGE_TEST_TOOL` 指向本机架构的 appimagetool 1.9.1。CI 校验固定 SHA-256；生成的 AppImage 使用 extract-and-run 启动，避免依赖 FUSE。

测试失败原因、异常堆栈和诊断输出通过 CI 日志查看；子进程测试和 smoke 有超时，测试只清理自己创建的临时资源。
