namespace SyncClipboard.Core.Utilities.Runner;

public class MultiTimesEventSimulator(TimeSpan _interval)
{
    public uint MaximumSimulatedCount { get; set; } = 2;
    private CancellationTokenSource? _cts;
    private uint _clickCount = 0;

    private readonly Dictionary<uint, Action> _events = [];

    public Action this[uint key]
    {
        get
        {
            if (!_events.TryGetValue(key, out Action? value))
            {
                value = new Action(() => { });
                _events[key] = value;
            }
            return value;
        }
        set => _events[key] = value;
    }

    public void TriggerOriginalEvent()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = WaitForNextOriginalEvent(_cts.Token);
    }

    public void SetEvent(uint count, Action action)
    {
        _events[count] += action;
    }

    private async Task WaitForNextOriginalEvent(CancellationToken token)
    {
        _clickCount++;
        if (_clickCount >= MaximumSimulatedCount)
        {
            TriggleFinalEvent();
            return;
        }

        await Task.Delay(_interval, token);
        TriggleFinalEvent();
    }

    private void TriggleFinalEvent()
    {
        if (_events.TryGetValue(_clickCount, out var action))
        {
            action?.Invoke();
        }
        _clickCount = 0;
    }
}
