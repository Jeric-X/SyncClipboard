using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SyncClipboard
{
    class ClipboardListener : System.Windows.Forms.Control
    {
        public delegate void ClipBoardChangedHandler(); 
        
        private bool switchOn = false;
        private static readonly int WM_CLIPBOARDUPDATE = 0x031D;
        private event ClipBoardChangedHandler ClipBoardChanged;

        public ClipboardListener()
        {
            Enable();
        }

        [DllImport("user32.dll")]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        public void Enable()
        {
            if (!switchOn)
            {
                AddClipboardFormatListener(this.Handle);
                switchOn = true;
            }
        }

        [DllImport("user32.dll")]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        public void Disable()
        {
            if (switchOn)
            {
                RemoveClipboardFormatListener(this.Handle);
                switchOn = false;
            }
        }

        public void AddHandler(ClipBoardChangedHandler handler)
        {
            ClipBoardChanged += handler;
        }

        public void RemoveHandler(ClipBoardChangedHandler handler)
        {
            ClipBoardChanged -= handler;
        }

        protected override void DefWndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                ClipBoardChanged.Invoke();
            }
            else
            {
                base.DefWndProc(ref m);
            }
        }
    }

}
