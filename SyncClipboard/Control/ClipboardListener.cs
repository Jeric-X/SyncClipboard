using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SyncClipboard
{
    public class ClipboardListener : System.Windows.Forms.Control, IClipboardChangingListener
    {
        public delegate void ClipBoardChangedHandler();

        private const int WM_CLIPBOARDUPDATE = 0x031D;
        public event Action<ClipboardMetaInfomation> Changed;

        private readonly IClipboardFactory _clipboardFactory;

        [DllImport("user32.dll")]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        public ClipboardListener(IClipboardFactory clipboardFactory)
        {
            AddClipboardFormatListener(this.Handle);
            _clipboardFactory = clipboardFactory;
        }

        protected override void DefWndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                Changed?.Invoke(_clipboardFactory.GetMetaInfomation());
            }
            base.DefWndProc(ref m);
        }

        private bool _disposed = false;

        protected override void Dispose(bool disposing)
        {
            if (!this._disposed)
            {
                RemoveClipboardFormatListener(this.Handle);
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
