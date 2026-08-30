# Issue #340：历史窗口保持显示时恢复粘贴目标窗口

## 背景

[Issue #340](https://github.com/Jeric-X/SyncClipboard/issues/340) 描述了如下问题：历史窗口置顶后，普通复制不会关闭窗口，但“复制并粘贴”仍会关闭窗口。若仅将“复制并粘贴”改为不关闭窗口，历史窗口仍占有键盘焦点，模拟的 `Ctrl+V` / `Cmd+V` 会被发送给历史窗口，而不是用户原本希望接收内容的窗口。

因此，该功能的核心不是单纯改变历史窗口的关闭条件，而是：

1. Windows 和 macOS 持续记录最近使用的、非历史窗口的前台窗口。
2. Windows 和 macOS 仅在历史窗口置顶时，才在“复制并粘贴”前将该窗口恢复为前台窗口。
3. 历史窗口需要保持显示时，粘贴后仍保持显示和置顶状态。
4. 平台不支持记录窗口，或者目标窗口激活失败时，隐藏历史窗口完成粘贴；非置顶时保持隐藏，置顶时再恢复。

Linux 的历史记录粘贴功能不启用前台窗口监听和目标窗口恢复。Linux 上的“复制并粘贴”固定依赖隐藏历史窗口后的系统焦点回退：非置顶时隐藏后不恢复，置顶时临时隐藏并在粘贴后恢复，X11 和 Wayland 行为一致；全局快捷键黑名单仍独立监听前台程序。

`CloseWhenLostFocus` 继续保留。它仍用于控制未置顶的历史窗口是否在失焦时关闭，不属于本功能的替代项。

## 目标行为

| 场景 | 历史窗口行为 | 粘贴目标处理 |
| --- | --- | --- |
| 未置顶，普通复制 | 按现有逻辑关闭 | 不发送粘贴快捷键 |
| 已置顶，普通复制 | 保持显示 | 不发送粘贴快捷键 |
| 未置顶，复制并粘贴 | 按现有逻辑关闭 | 所有平台统一依赖关闭后的系统焦点回退，不主动激活目标窗口 |
| 已置顶，复制并粘贴，目标可恢复 | 保持显示和置顶，但不占有键盘焦点 | 激活目标窗口，确认成功后粘贴 |
| 已置顶，复制并粘贴，目标不可记录或激活失败 | 临时隐藏，粘贴后恢复显示和置顶 | 让系统回退到原窗口后粘贴 |
| Linux，复制并粘贴 | 非置顶时隐藏后不恢复；置顶时临时隐藏并恢复 | 不记录或激活目标窗口，依赖窗口隐藏后的系统焦点回退 |

## 总体设计

设计分为五层：

1. `INativeWindowController` 负责读取当前前台窗口，并提供平台原生的窗口级引用；同时负责使用该引用重新激活窗口。
2. `INativeForegroundWindowWatcher` 保持平台 native 职责和 `Start/Stop` 接口，但事件升级为窗口级识别，尽可能携带发生变化的原生窗口引用。
3. `ForegroundWindowMonitor` 是通用中间层，统一控制 watcher 的启停、通过 provider 主动查询当前前台窗口，并向业务服务提供变化事件。
4. Windows 和 macOS 上，`ForegroundWindowTrackingService` 订阅中间层，持续保存最近一个可用的非历史窗口，供置顶模式的复制并粘贴功能使用；Linux 上该 tracking service 不订阅。`HotkeyBlacklistService` 在所有平台仍按配置独立订阅中间层。
5. `HistoryViewModel.CopyToClipboard` 负责粘贴流程编排：设置剪贴板、决定历史窗口是否保持显示；非置顶时统一隐藏后粘贴，置顶时再按平台决定恢复目标窗口或执行临时隐藏兜底。

## 1. 扩展前台窗口信息

### 1.1 平台原生窗口引用

现有 `WindowInfo` 只包含进程名、窗口标题和可执行文件名。这些字段适合匹配规则，但不能可靠地定位并重新激活一个具体窗口。应增加只在本次进程运行期间有效、不可序列化的平台原生窗口引用。

建议定义一个不暴露平台实现细节的基类：

```csharp
public abstract record NativeWindowInfo
{
    public required int ProcessId { get; init; }
}
```

Core 提供几个运行时实现，平台 provider 只创建并解释与自身平台匹配的类型：

```csharp
public sealed record WindowsNativeWindowInfo : NativeWindowInfo
{
    public required nint Hwnd { get; init; }
}

public sealed record X11NativeWindowInfo : NativeWindowInfo
{
    public required string DisplayName { get; init; }
    public required nuint WindowId { get; init; }
}

public sealed record MacNativeWindowInfo : NativeWindowInfo
{
    public required string BundleIdentifier { get; init; }
    public long? WindowNumber { get; init; }
    public string? WindowTitle { get; init; }
    public ScreenPosition? WindowBounds { get; init; }
}
```

Windows 可以保存精确的窗口句柄。macOS 应优先使用 `NSWindow.WindowNumber`/`AXWindowNumber` 作为窗口级身份，并使用 Accessibility API 重新定位具体窗口；如果目标应用（例如部分 Electron 应用）不提供窗口身份，则以应用 PID 激活和前台确认作为降级结果。不要长期保存裸的 `AXUIElementRef`，避免所有权、释放和窗口失效问题；可以保存 PID、窗口编号、标题、窗口位置等信息，在恢复时重新枚举并匹配窗口。Linux 的 X11 Window ID 仅用于前台程序监听和快捷键黑名单，不用于历史记录粘贴目标恢复。

### 1.2 扩展 `WindowDetail`

```csharp
public readonly struct WindowDetail
{
    public WindowInfo? WindowInfo { get; init; }
    public ScreenPosition? Bounds { get; init; }
    public NativeWindowInfo? NativeWindowInfo { get; init; }
}
```

`NativeWindowInfo` 只用于运行时窗口操作：

- 不写入用户配置。
- 不参与历史记录同步。
- 不跨进程传递。
- 无需序列化，也不要求各平台使用统一的字段布局。
- 每次使用前必须验证窗口仍然存在，并验证它没有被句柄复用为其他进程的窗口。

### 1.3 由 provider 恢复窗口

原生信息的解释和操作应留在平台 provider 内，调用者不直接判断 `HWND` 或 macOS 窗口信息。

```csharp
public interface INativeWindowController
{
    WindowDetail? GetForegroundWindowDetail();
    WindowDetail? GetWindowDetail(NativeWindowInfo window);
    WindowInfo? GetForegroundWindowInfo();

    bool TryActivateWindow(NativeWindowInfo window);
}
```

`GetWindowDetail` 用于把 watcher 携带的平台原生窗口引用扩展为完整快照，避免 native 事件到达后再次查询“当前窗口”时读到另一扇窗口。无法读取时返回 `null`。

`TryActivateWindow` 暂时只返回 `bool`，不引入结果枚举或失败原因对象。provider 内部完成窗口有效性检查和平台激活，并在失败时自行记录足够的诊断日志。返回 `true` 表示平台认为激活成功；返回 `false` 时由上层进入临时隐藏兜底。

## 2. 前台窗口中间层与目标跟踪

### 2.1 watcher 保持 native 职责并升级为窗口级事件

`INativeForegroundWindowWatcher` 仍只负责平台 native 监听和启停，但允许调整接口以提供窗口级识别：

```csharp
public interface INativeForegroundWindowWatcher : IDisposable
{
    event Action<NativeWindowInfo?>? ForegroundWindowChanged;

    void Start();
    void Stop();
}
```

它只处理平台 native 监听，不缓存业务窗口详情，不判断历史窗口，也不知道热键黑名单或粘贴目标。其他业务服务不直接依赖它。

事件参数尽可能表示触发变化的具体窗口：

- Windows `WinEvent` 直接使用回调提供的 `HWND` 构造 `WindowsNativeWindowInfo`，实现窗口级而不是进程级识别。
- macOS 尽可能从激活应用和 Accessibility API 获得焦点窗口信息；只能识别应用时允许退化为应用级引用。
- Linux/X11 watcher 按顶层 Window ID 识别变化，供全局快捷键黑名单使用；历史记录粘贴目标 tracking service 在 Linux 不订阅 monitor。
- 读取失败时允许传入 `null`。

基于系统 native 事件的 watcher 收到一次事件就直接转发一次，即使连续事件指向同一个窗口也不去重。上层业务可能需要知道同一窗口重新激活或焦点重新确认。

只有轮询型 watcher 需要避免每个周期都产生重复事件；其去重键必须优先使用窗口级 `NativeWindowInfo`，不能只比较进程名。轮询从窗口 A 切换到同一进程的窗口 B 时必须发出变化事件。

### 2.2 新增通用中间层

新增 `ForegroundWindowMonitor`，作为 `INativeForegroundWindowWatcher` 和业务服务之间的唯一入口：

```csharp
public interface IForegroundWindowMonitor
{
    WindowDetail? GetCurrentForegroundWindow();

    event Action<WindowDetail?>? ForegroundWindowChanged;
}

public sealed class ForegroundWindowMonitor
    : IForegroundWindowMonitor, IDisposable
{
    // 具体实现省略
}
```

中间层职责：

- 持有 singleton `INativeForegroundWindowWatcher` 和 `INativeWindowController`。
- 统一订阅 watcher 的原始变化事件。
- 每次收到通知时只调用一次 provider：有 native 事件窗口时调用 `GetWindowDetail`，没有时调用 `GetForegroundWindowDetail`，生成完整快照。
- 提供 `GetCurrentForegroundWindow()`，供新订阅者主动读取当前快照。
- 将同一份窗口快照发送给全部业务订阅者，避免各业务服务重复读取 provider 得到不一致结果。
- 根据自身 `ForegroundWindowChanged` 是否存在订阅者，控制 watcher 的 `Start/Stop`。

订阅事件本身不立即向新订阅者推送当前窗口。需要初始化状态的业务服务必须采用“先订阅，再主动调用 `GetCurrentForegroundWindow()`”的方式；之后只处理新的变化事件。主动读取时 monitor 调用 provider，读取失败或 provider 抛出异常时记录日志并返回 `null`，且不因此触发变化事件。

其他服务只能注入 `IForegroundWindowMonitor`，不能直接注入 `INativeForegroundWindowWatcher`。native watcher 的 `Start/Stop` 使用方式保持不变，但事件可以升级为携带窗口级原生信息；其使用范围被限制在中间层内部。

### 2.3 中间层根据业务订阅状态控制 watcher

中间层使用自定义事件访问器维护业务订阅者：

- 第一个业务事件处理器订阅时，中间层先订阅 native watcher，再调用 `watcher.Start()`。
- 最后一个业务事件处理器解除订阅时，中间层调用 `watcher.Stop()`，再解除 native watcher 订阅。
- `Dispose()` 无条件停止 watcher、解除 native 事件并清理中间层状态。
- 业务服务只订阅或退订中间层事件，不能调用 watcher 的 `Start/Stop`。

建议实现轮廓：

```csharp
private readonly object _syncRoot = new();
private Action<WindowDetail?>? _foregroundWindowChanged;
private bool _isWatching;

public event Action<WindowDetail?>? ForegroundWindowChanged
{
    add
    {
        if (value is null)
            return;

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            var shouldStart = _foregroundWindowChanged is null;
            _foregroundWindowChanged += value;

            if (shouldStart)
                StartWatchingLocked();
        }
    }
    remove
    {
        if (value is null)
            return;

        lock (_syncRoot)
        {
            _foregroundWindowChanged -= value;

            if (_foregroundWindowChanged is null)
                StopWatchingLocked();
        }
    }
}

private void StartWatchingLocked()
{
    if (_isWatching)
        return;

    _watcher.ForegroundWindowChanged += OnNativeForegroundWindowChanged;
    _watcher.Start();
    _isWatching = true;
}

private void StopWatchingLocked()
{
    if (!_isWatching)
        return;

    _watcher.Stop();
    _watcher.ForegroundWindowChanged -= OnNativeForegroundWindowChanged;
    _isWatching = false;
}
```

实现约束：

- 启停必须幂等，订阅 native 事件必须早于 `Start()`，避免丢失启动时的首次通知。
- 同一个 delegate 重复订阅时遵循标准 .NET event 语义，停止条件以调用列表为空为准。
- native 回调在哪个原始线程到达，monitor 就在哪个线程读取 provider 并触发业务回调。monitor 不负责委托到主线程；需要 UI 线程的订阅者自行使用 `IThreadDispatcher`。
- provider 读取失败或抛出异常时，本次窗口快照为 `null`，仍向业务订阅者广播 `null`。
- monitor 不对 native 事件做额外去重。native watcher 转发的相同窗口事件也原样广播。
- monitor 不建立事件队列，也不对多次回调进行串行化；每次回调独立读取和广播自己的快照。
- 触发业务回调时复制 invocation list，并逐个调用。每个订阅者使用独立的 `try/catch` 隔离；一个订阅者抛出异常不能阻止其他订阅者收到事件，异常由 monitor 记录日志。
- 业务回调在锁外执行，允许回调内部退订，不能因此死锁。
- `Stop()` 与已经进入的 native 回调允许并发完成，无需引入 generation/version 或专用串行队列。
- macOS watcher 的内部主线程调度逻辑保持在 native 实现中，中间层不感知平台差异。应用激活通知之外，使用窗口级轮询补足同一应用内窗口切换；轮询只过滤身份相同的窗口。

业务回调隔离的实现轮廓：

```csharp
private void OnNativeForegroundWindowChanged(NativeWindowInfo? nativeWindow)
{
    WindowDetail? snapshot;
    try
    {
        snapshot = nativeWindow is null
            ? _provider.GetForegroundWindowDetail()
            : _provider.GetWindowDetail(nativeWindow);
    }
    catch (Exception ex)
    {
        _logger.Write(Tag, ex.Message);
        snapshot = null;
    }

    Delegate[] callbacks;
    lock (_syncRoot)
    {
        callbacks = _foregroundWindowChanged?
            .GetInvocationList() ?? [];
    }

    foreach (var callback in callbacks)
    {
        try
        {
            ((Action<WindowDetail?>)callback)(snapshot);
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, ex.Message);
        }
    }
}
```

### 2.4 持续跟踪粘贴目标

新增 `ForegroundWindowTrackingService`，建议作为系统级 `IService` 注册并随应用启动：

```csharp
public sealed class ForegroundWindowTrackingService : Service
{
    public WindowDetail? LastExternalWindow { get; }

    public bool TryActivateLastExternalWindow();
}
```

它只依赖 `IForegroundWindowMonitor` 和 `INativeWindowController`：

- 服务启动时先订阅中间层，再主动调用 `GetCurrentForegroundWindow()` 初始化目标；停止时退订。
- 每次变化时判断窗口是否为 SyncClipboard 历史窗口。
- 保存最近一个带有可用 `NativeWindowInfo` 的外部窗口。
- 对外提供线程安全的目标快照。
- 调用 provider 验证并恢复目标窗口，只向调用方返回成功或失败；具体失败原因由 provider 记录。

Windows 和 macOS 上，该服务在应用正常运行期间始终订阅中间层，因此中间层会保持 native watcher 运行。Linux 上服务不订阅中间层。

`HotkeyBlacklistService` 仍是独立业务服务：

- 配置启用且黑名单非空时，订阅 `IForegroundWindowMonitor.ForegroundWindowChanged`。
- 订阅后立即调用 `GetCurrentForegroundWindow()` 完成第一次黑名单判断，不能等待下一次变化事件。
- 功能关闭时只解除自己的中间层订阅。
- 保留现有防抖和黑名单匹配逻辑。
- 不直接依赖 provider 或 native watcher，也不调用 watcher 的 `Start/Stop`。
- Linux 上仍按配置订阅 monitor，保证前台程序黑名单继续动态生效；这不代表历史记录粘贴功能会订阅或使用这些事件。

### 2.5 排除历史窗口

服务只排除历史窗口本身，不排除 SyncClipboard 当前进程的其他窗口。这样设置窗口、对话框或未来新增的应用窗口仍可在需要时成为普通前台窗口。

为避免 `ForegroundWindowTrackingService` 反向注入 keyed `IWindow` 造成 `HistoryWindow → HistoryViewModel → TrackingService → HistoryWindow` 的 DI 循环，由现有 `HistoryViewModel.Init(IWindow window)` 或历史窗口显示完成事件主动注册历史窗口的原生身份：

```csharp
public interface IWindow
{
    NativeWindowInfo? GetNativeWindowInfo();
}

foregroundWindowTrackingService.SetHistoryWindow(
    window.GetNativeWindowInfo());
```

原生句柄可能只有窗口真正创建或显示后才能获得，因此每次历史窗口完成创建或重新创建 native handle 时都要刷新注册值。如果注册的新身份与当前 `LastExternalWindow` 相同，立即清除该错误目标。

粘贴目标比较使用平台 `NativeWindowInfo` 的窗口级身份：Windows 比较 `HWND` 并核对 PID，macOS 优先比较 Window Number。部分应用无法从 AX 侧读取 Window Number，此时退化为 PID 与窗口标题；如果两边都没有窗口级信息，才退化为应用 PID。窗口位置和尺寸只作为描述信息，任何情况下都不能参与窗口身份判断，因为所有应用都允许移动和调整窗口大小。Linux 的 X11 Window ID 比较只用于 watcher 轮询去重和快捷键黑名单，不用于粘贴目标。

即使平台无法提供 `NativeWindowInfo`，monitor 仍可向 `ForegroundWindowChanged` 广播描述性的窗口详情供黑名单功能使用，但不能将它作为可直接恢复的目标。

### 2.6 并发和失效处理

- `LastExternalWindow` 的读写需要加锁，或使用不可变引用配合原子替换。
- 激活开始时先复制一份目标快照，避免激活期间被中间层事件更新。
- 窗口关闭、进程退出或句柄被复用时，由 provider 在 `TryActivateWindow` 内验证并返回 `false`。
- 激活历史窗口本身触发的中间层事件不能覆盖 `LastExternalWindow`。
- 历史记录粘贴目标不应依赖固定的一秒轮询来捕获快速切换；Windows 和 macOS 使用系统事件。Linux 的轮询只服务于全局快捷键黑名单。

## 3. 复制并粘贴流程

### 3.1 正常路径

建议将 `CopyToClipboard` 的流程拆成“复制”和“粘贴目标切换”两个阶段：

```csharp
public async Task CopyToClipboard(
    HistoryRecordVM record,
    bool paste,
    CancellationToken token)
{
    // 现有的文件和内容有效性检查保持不变。
    var profile = await GetValidatedProfileAsync(record, token);
    if (profile is null)
        return;

    if (!paste)
    {
        CloseHistoryWindowWhenNeeded();
        await localClipboardSetter.Set(profile, token);
        return;
    }

    await localClipboardSetter.Set(profile, token);
    await PasteToLastExternalWindowAsync(token);
}
```

粘贴阶段的伪代码：

```csharp
private async Task PasteToLastExternalWindowAsync(CancellationToken token)
{
    if (!IsTopmost)
    {
        ClearSelectedItem();
        window.ScrollToTop();
        window.Hide();
        await PasteAfterHistoryWindowHiddenOrClosedAsync(token);
        return;
    }

    if (OperatingSystem.IsLinux())
    {
        await PasteWithTemporarilyHiddenHistoryWindowAsync(token);
        return;
    }

    if (foregroundWindowTrackingService
        .TryActivateLastExternalWindow())
    {
        keyboard.Paste();
        return;
    }

    await PasteWithTemporarilyHiddenHistoryWindowAsync(token);
}
```

这里的 `IsTopmost` 只决定一次显式“复制并粘贴”操作是否保持历史窗口显示；`CloseWhenLostFocus` 仍只在 `OnLostFocus` 中生效。

### 3.2 激活成功的判定

以下激活流程只用于置顶模式。以 Windows 为例：

1. 验证 `HWND` 仍有效，且所属 PID 与捕获时一致。
2. 若窗口最小化，按平台策略决定是否恢复。
3. 调用 `SetForegroundWindow`。
4. 在较短的超时时间内检查 `GetForegroundWindow() == targetHwnd`。
5. 确认后才发送粘贴快捷键。

建议轮询 10～20ms 一次，总超时 200～300ms。使用状态确认而不是只写一个固定延迟，既降低竞态，也避免无意义等待。

### 3.3 临时隐藏兜底

以下情况进入兜底：

- 平台无法提供窗口级原生信息。
- 没有记录到外部前台窗口。
- 目标窗口已经关闭或失效。
- 平台拒绝激活目标窗口。
- 激活调用完成，但超时后目标仍未成为前台窗口。

兜底步骤：

1. ViewModel 读取历史窗口是否可见、是否处于活动状态。
2. ViewModel 调用基础 `Hide()` 隐藏历史窗口。
3. 给系统一次焦点回退机会；允许无法确认具体目标，但不能在历史窗口仍然可见时发送粘贴键。
4. 发送 `Ctrl+V` / `Cmd+V`。
5. 等待模拟键盘事件发送完成。
6. ViewModel 在 `finally` 中调用 `Show(wasActive)` 恢复历史窗口及其原有激活状态。

窗口的显示编排属于 ViewModel。`IWindow` 只提供 UI 框架层的基础能力：

```csharp
public interface IWindow
{
    bool IsVisible { get; }
    bool IsActive { get; }
    void Show(bool activate);
    void Hide();
}
```

`HistoryViewModel.RunTemporarilyHiddenAsync` 负责检查窗口是否可见、记录原有激活状态、隐藏、执行操作，并在内部的 `finally` 中恢复。返回 `false` 表示窗口当时不可见；粘贴发送或取消过程中发生异常也不会让历史窗口永久消失。

恢复时应以临时隐藏前的激活状态为准，而不是固定使用“显示但不激活”：如果历史窗口原本处于活动状态，可在所有模拟按键释放后重新激活；如果原本没有激活，则只恢复显示。窗口实例没有销毁，因此位置、尺寸、`Topmost` 和窗口状态由底层 UI 框架自然保留，不在业务层重复记录。

所有平台上，如果历史窗口非置顶，则隐藏并在粘贴后保持隐藏。Linux 不尝试激活目标窗口；历史窗口置顶时才走临时隐藏并恢复路径。

### 3.4 兜底允许无法确认目标

直接激活失败后，只要历史窗口已经成功临时隐藏，兜底路径允许在无法确认当前具体目标窗口的情况下继续发送粘贴快捷键。其依据是系统通常会把焦点回退给历史窗口之前的活动窗口；该路径是显式接受的 best-effort 行为。

兜底不要求 provider 能读取当前窗口，也不因读取结果为 `null` 而取消粘贴。相关信息只用于日志诊断。若历史窗口本身无法隐藏，才应避免把粘贴快捷键发送给仍然持有焦点的历史窗口。

## 4. 平台实现建议

### Windows（WinUI3 和 Avalonia）

- 捕获：`GetForegroundWindow`、`GetWindowThreadProcessId`。
- watcher：WinEvent 回调直接转发事件提供的 `HWND`，不因与上次 `HWND` 相同而去重。
- 验证：`IsWindow`，并再次核对 PID。
- 激活：`SetForegroundWindow`；最小化窗口可结合 `ShowWindowAsync(SW_RESTORE)`。
- 确认：轮询 `GetForegroundWindow`。
- `TryActivateWindow` 失败时由 provider 记录 `HWND`、PID、平台返回值和最后错误等日志，并只向上层返回 `false`。
- WinUI3 与 Avalonia 当前各有 provider，实现可以分别接入相同的 Core 接口；后续再考虑合并 Win32 代码。

### macOS

- 捕获应用：`NSWorkspace.SharedWorkspace.FrontmostApplication`。
- 捕获窗口：通过 Accessibility API 读取主窗口或焦点窗口的标题和边界。
- watcher：native 激活通知直接转发；另以轻量轮询识别同一应用内的主窗口变化。能获得焦点窗口时携带窗口级信息，否则允许应用级信息或 `null`。
- 在历史窗口显示或重新激活之前同步读取一次当前前台窗口，补足同一应用内窗口切换发生在下一次轮询之前的竞态。
- 恢复应用：`NSRunningApplication.Activate`。
- 恢复窗口：重新枚举目标应用窗口，匹配捕获时的信息，设置 AX frontmost/main/focused 属性并执行 `AXRaise`；如果捕获时没有可用窗口身份则退化为应用级恢复，存在身份但无法精确恢复时返回失败并进入隐藏兜底。
- 捕获窗口时优先读取 `AXFocusedWindow`，再回退到 `AXMainWindow`；历史窗口使用原生 `NSWindow.WindowNumber` 注册身份，避免标题或坐标系差异导致误识别。
- `NSRunningApplication.Activate`/`AXRaise` 返回成功只代表请求已被接受。业务层异步等待并再次读取前台窗口，确认具体目标已获得前台后才发送粘贴键；这段等待不能阻塞 macOS 主线程。
- 隐藏兜底发送 SharpHook 按键后不能立即恢复历史窗口；需要保留短暂派发窗口，让 macOS 消费完整的按下/释放事件序列后再恢复。
- 恢复失败由 provider 记录日志并返回 `false`。
- 根据 PID 取得运行中应用后必须核对捕获时的 Bundle Identifier，拒绝已经被其他应用复用的 PID。
- UI 操作必须通过主线程 dispatcher。

### Linux（X11/Wayland）

- `ForegroundWindowTrackingService` 不订阅 monitor；历史记录复制并粘贴功能不监听前台窗口。
- `HotkeyBlacklistService` 仍按配置订阅 monitor；X11 provider 和轮询 watcher 保留窗口身份与描述读取，供该功能使用。
- Linux provider 的 `TryActivateWindow` 固定返回 `false`，X11 Window ID 不用于恢复粘贴目标。
- 删除 `_NET_ACTIVE_WINDOW`、`XSendEvent`、`XFlush` 及相关 ClientMessage 结构。
- “复制并粘贴”固定先隐藏历史窗口并等待系统把焦点交给下层窗口；非置顶时粘贴后保持隐藏，置顶时在按键派发完成后恢复历史窗口。
- 隐藏历史窗口到发送粘贴快捷键之间的等待时间从 runtime `HistoryWindow` 配置的 `PasteDelayAfterWindowHiddenMilliseconds` 读取，默认 200ms；该内部调节项不在设置界面展示。

## 5. 预计代码改动

### Core

- `Models/WindowDetail.cs`
  - 增加 `NativeWindowInfo`。
- `Models/NativeWindowInfo.cs`
  - 新增运行时平台窗口引用基类。
- `Interfaces/INativeWindowController.cs`
  - 增加返回 `bool` 的窗口激活方法，失败日志由 provider 自己记录。
- `Interfaces/INativeForegroundWindowWatcher.cs` 及平台实现
  - 保持 native 监听职责和 `Start/Stop`。
  - 事件增加窗口级 `NativeWindowInfo`，native 事件直接转发不去重，轮询按窗口级身份去重。
- 新增 `IForegroundWindowMonitor` 和 `ForegroundWindowMonitor`
  - 统一控制 watcher 的 `Start/Stop`。
  - 提供主动读取当前前台窗口快照的方法及变化事件。
  - 保持原始回调线程，读取失败广播 `null`，并隔离每个业务回调的异常。
- `Interfaces/IWindow.cs`
  - 只提供 `Show(bool activate)`、`Hide()`、可见性/激活状态和定位等 UI 框架基础能力。
- 新增 `ForegroundWindowTrackingService`。
- `HotkeyBlacklistService`
  - 独立订阅 foreground window monitor，不再直接操作 watcher 或 provider。
- `HistoryViewModel`
  - 注入 tracking service。
  - 重构 `CopyToClipboard` 的窗口恢复和兜底流程。
  - 提供 `ShowWithAutoPosition` 和 `SwitchVisible`，统一编排显示、自动定位、搜索框聚焦和显示前目标捕获。
  - 负责临时隐藏操作，并在 `finally` 中按原激活状态恢复窗口。
  - 保留 `CloseWhenLostFocus` 和现有 `OnLostFocus` 判断。
- `AppCore.ConfigCommonService`
  - 将 `ForegroundWindowMonitor` 注册为 singleton `IForegroundWindowMonitor`。
- `AppCore.ConfigurateUserService`
  - 注册并启动 `ForegroundWindowTrackingService`。

### 平台项目

- WinUI3 `Win32NativeWindowController`
  - 返回 `HWND` 原生信息，实现验证和激活。
- Avalonia Windows `WindowsNativeWindowController`
  - 同上。
- Avalonia Linux `LinuxNativeWindowController`
  - 保留前台窗口身份、描述和边界读取供快捷键黑名单使用，但不实现窗口激活。
- Avalonia Linux `PollingForegroundWindowWatcher`
  - 仍可由快捷键黑名单通过 monitor 启停；历史记录粘贴目标 tracking 不订阅。
- macOS `MacNativeWindowController`
  - 返回 PID 和窗口级匹配信息，实现应用/窗口激活。
- Avalonia、WinUI3、macOS 历史窗口
  - 仅实现 `Show(bool activate)`、`Hide()`、定位和搜索框聚焦等底层窗口能力。

## 6. 测试方案

### Core 单元测试

使用 mock watcher、monitor、provider、tracking service、`IWindow` 和键盘发送器覆盖：

1. 已置顶且 provider 返回可恢复窗口时，先设置剪贴板，再激活窗口，最后发送粘贴键。
2. 已置顶且激活成功时，历史窗口不关闭、不临时隐藏。
3. 已置顶但平台不支持原生窗口时，临时隐藏、粘贴，并尽量恢复原有窗口状态。
4. 已置顶但激活超时时，进入相同兜底流程。
5. 临时隐藏后即使无法确认有效目标，也继续发送粘贴键并恢复历史窗口。
6. 未置顶时所有平台都保持原来的关闭行为，隐藏后直接等待并粘贴，不调用目标窗口激活。
7. `CloseWhenLostFocus = false` 时，失焦仍不关闭。
8. `CloseWhenLostFocus = true` 且未置顶时，失焦关闭。
9. tracking service 不会用历史窗口覆盖最近的外部窗口，但允许记录同进程的其他窗口。
10. 目标窗口关闭或句柄被复用后，服务拒绝激活并进入兜底。
11. 黑名单功能关闭时，前台窗口 tracking 仍持续运行。
12. monitor 的第一个业务订阅者加入时只启动一次 native watcher，最后一个业务订阅者移除时只停止一次。
13. 多个业务服务独立退订 monitor 时，不会提前停止 native watcher。
14. monitor 的事件回调内部退订不会死锁，也不会破坏其他订阅者。
15. 每次 native 变化只读取一次 provider，所有业务订阅者收到同一个 `WindowDetail` 快照。
16. 新订阅者不会立即收到事件；主动调用 `GetCurrentForegroundWindow()` 才获得当前快照。
17. provider 读取失败时所有订阅者收到 `null`。
18. 一个订阅者抛出异常时，其余订阅者仍能收到相同事件。
19. monitor 不切换线程，业务回调运行在 native watcher 的原始回调线程。
20. native watcher 连续报告同一个窗口时，monitor 仍逐次转发；轮询 watcher 只过滤窗口身份完全相同的轮询结果。
21. 只排除历史窗口；同进程内其他 SyncClipboard 窗口仍可被记录。
22. ViewModel 的临时隐藏流程在正常、异常和取消路径上都恢复原有窗口可见性和激活状态。
23. Linux 的历史记录粘贴 tracking 不订阅 monitor；复制并粘贴在非置顶时按“隐藏、粘贴”执行，置顶时按“隐藏、粘贴、恢复”执行；快捷键黑名单仍能按需启动 watcher。

建议为 `VirtualKeyboard` 增加接口或将粘贴发送器抽象化，以便测试调用顺序、直接激活路径和“目标无法确认但临时隐藏成功时仍发送粘贴键”的兜底路径。

### 手工平台验证

- 在两个编辑器窗口间切换，确保恢复的是具体窗口而不只是应用。
- 历史窗口置顶时连续从多条记录交替粘贴，窗口始终显示。
- 目标窗口最小化、关闭或切换桌面时验证降级行为。
- 验证鼠标点击历史记录、键盘回车、快捷键打开历史窗口三种入口。
- 验证 Windows UAC/不同完整性级别导致激活失败时进入隐藏兜底；非置顶时保持隐藏，置顶时在粘贴后恢复历史窗口。
- 验证 macOS 没有辅助功能权限时的应用级恢复和隐藏兜底。
- 验证 X11 与 Wayland 的历史记录粘贴功能不监听或激活前台窗口；非置顶时隐藏后不恢复，置顶时粘贴后恢复；同时验证全局快捷键黑名单监听不回归。

## 7. 验收标准

1. `CloseWhenLostFocus` 配置及其 UI 保持存在，行为不回归。
2. 历史窗口置顶时执行“复制并粘贴”，历史窗口保持显示。
3. Windows 和 macOS 上，内容被粘贴到最近使用的具体外部窗口。
4. 无法记录或激活目标窗口时，隐藏历史窗口仍能完成粘贴；非置顶时保持隐藏，置顶时恢复显示。
5. 兜底路径无法确认具体目标时，只要历史窗口已成功隐藏，仍按 best-effort 方式发送粘贴快捷键。
6. native watcher 保持平台监听职责和 `Start/Stop`，事件升级为窗口级识别；由 foreground window monitor 根据业务订阅状态统一启停，任一业务服务的退订都不会中断仍在使用 monitor 的其他服务。
7. 仅历史窗口自身被排除，同一进程的其他窗口不被误排除。
8. monitor 不切换线程、不串行化事件、不对 native 事件额外去重；读取失败广播 `null`，并隔离订阅者异常。
9. provider 激活接口只返回 `bool`，失败原因由平台 provider 写入日志。
10. 临时隐藏后恢复原有可见性和激活状态；同一窗口实例的位置、尺寸、置顶和窗口状态由 UI 框架自然保留。
11. Linux 的历史记录粘贴功能不监听或激活前台窗口；复制并粘贴在非置顶时隐藏后保持隐藏，置顶时临时隐藏并在粘贴后恢复。全局快捷键黑名单仍可独立监听前台程序。
