namespace SyncClipboard.Core.Interfaces;

public class ToggleMenuItem : MenuItem
{
    private bool @checked;

    public event Action<bool>? CheckedChanged;
    public bool Checked
    {
        get => @checked;
        set
        {
            if (@checked != value)
            {
                @checked = value;
                CheckedChanged?.Invoke(@checked);
            }
        }
    }

    public ToggleMenuItem(string? text, bool initialStatus, Action<bool>? action = null) : base(text)
    {
        Checked = initialStatus;
        Action = () =>
        {
            ChangeStatus();
            action?.Invoke(Checked);
        };
    }

    private void ChangeStatus()
    {
        Checked = !Checked;
    }
}