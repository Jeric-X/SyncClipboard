using SyncClipboard.Core.Clipboard;
using System;
using System.Runtime.InteropServices;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal partial class ClipboardFactory : ClipboardFactoryBase
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZEL
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OBJECTDESCRIPTOR
    {
        public uint cbSize;
        public Guid clsid;
        public uint dwDrawAspect;
        public SIZEL sizel;
        public POINTL pointl;
        public uint dwStatus;
        public uint dwFullUserTypeName;
        public uint dwSrcOfCopy;
    }
}
