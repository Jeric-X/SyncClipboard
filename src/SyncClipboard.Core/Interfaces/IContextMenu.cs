namespace SyncClipboard.Core.Interfaces
{
    public class MenuItem(string? text, Action? action = null)
    {
        public string? Text { get; set; } = text;
        public Action? Action { get; set; } = action;
    }

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

    public interface IContextMenu
    {
        public const string DefaultGroupName = "Default Group";
        public void AddMenuItemGroup(MenuItem[] menuItems, string? group = null);
        public void AddMenuItem(MenuItem menuItem, string? group = null);
    }
}