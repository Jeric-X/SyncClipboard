using SyncClipboard.Core;
using System;
using System.IO;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
namespace SyncClipboard.WinUI3.ClipboardWinUI;

class UnusualStorageItem : IStorageItem
{
    public global::Windows.Storage.FileAttributes Attributes => _storageFile.Attributes;
    public DateTimeOffset DateCreated => _storageFile.DateCreated;

    public string Name { get; private set; }
    public string Path { get; private set; }

    private readonly StorageFile _storageFile;
    private const string StorageItemTempFolder = "StorageItem Temp Folder";

    public UnusualStorageItem(string path)
    {
        Name = System.IO.Path.GetFileName(path);
        Path = path;

        var tempFolder = System.IO.Path.Combine(Core.Commons.Env.TemplateFileFolder, StorageItemTempFolder);
        if (!Directory.Exists(tempFolder))
        {
            Directory.CreateDirectory(tempFolder);
        }
        var tempfilePath = System.IO.Path.Combine(tempFolder, Name + '2');
        File.Copy(path, tempfilePath, true);
        _storageFile = StorageFile.GetFileFromPathAsync(tempfilePath).AsTask().Result;
    }

    public IAsyncAction DeleteAsync()
    {
        AppCore.Current.Logger.Write("Unexpected calling.");
        throw new NotImplementedException();
    }

    public IAsyncAction DeleteAsync(StorageDeleteOption option)
    {
        AppCore.Current.Logger.Write("Unexpected calling.");
        throw new NotImplementedException();
    }

    public IAsyncOperation<BasicProperties> GetBasicPropertiesAsync()
    {
        return _storageFile.GetBasicPropertiesAsync();
    }

    public bool IsOfType(StorageItemTypes type)
    {
        AppCore.Current.Logger.Write("Unexpected calling.");
        throw new NotImplementedException();
    }

    public IAsyncAction RenameAsync(string desiredName)
    {
        AppCore.Current.Logger.Write("Unexpected calling.");
        throw new NotImplementedException();
    }

    public IAsyncAction RenameAsync(string desiredName, NameCollisionOption option)
    {
        AppCore.Current.Logger.Write("Unexpected calling.");
        throw new NotImplementedException();
    }
}
