namespace SyncClipboard.Core.Clipboard
{
    //
    // 摘要:
    //     Specifies the possible effects of a drag-and-drop operation.
    [Flags]
    public enum DragDropEffects
    {
        //
        // 摘要:
        //     The target can be scrolled while dragging to locate a drop position that is not
        //     currently visible in the target.
        Scroll = int.MinValue,
        //
        // 摘要:
        //     The combination of the System.Windows.DragDropEffects.Copy, System.Windows.Forms.DragDropEffects.Move,
        //     and System.Windows.Forms.DragDropEffects.Scroll effects.
        All = -2147483645,
        //
        // 摘要:
        //     The drop target does not accept the data.
        None = 0,
        //
        // 摘要:
        //     The data from the drag source is copied to the drop target.
        Copy = 1,
        //
        // 摘要:
        //     The data from the drag source is moved to the drop target.
        Move = 2,
        //
        // 摘要:
        //     The data from the drag source is linked to the drop target.
        Link = 4
    }
}
