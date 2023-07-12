using SyncClipboard.Core.Clipboard;
using SyncClipboard.Service;
using System;

namespace SyncClipboard.Utility
{
    internal class FileClipboardSetter : IClipboardSetter<FileProfile>
    {
        public object CreateClipboardObjectContainer(MetaInfomation metaInfomation)
        {
            throw new NotImplementedException();
        }

        public void SetLocalClipboard(object obj)
        {
            throw new NotImplementedException();
        }
    }
}
