using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Threading;

namespace SyncClipboard.Desktop.ClipboardAva;

// 负责释放包内所有实现 IDisposable 的对象；填充时须存入已创建的对象，不能使用延迟创建对象的工厂函数。
// 资源须支持后台终结清理；平台不再持有包且没有显式释放时，由终结器兜底。
public sealed class AutoDisposeDataTransfer(DataTransfer data) : IDataTransfer, IAsyncDataTransfer
{
    private DataTransfer? _data = data;

    public DataTransfer Data => _data ?? throw new ObjectDisposedException(nameof(AutoDisposeDataTransfer));
    public IReadOnlyList<DataFormat> Formats => Data.Formats;
    public IReadOnlyList<IDataTransferItem> Items => Data.Items;
    IReadOnlyList<IAsyncDataTransferItem> IAsyncDataTransfer.Items => Data.Items;

    ~AutoDisposeDataTransfer()
    {
        try
        {
            DisposeCore(throwOnError: false);
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            DisposeCore(throwOnError: true);
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    private void DisposeCore(bool throwOnError)
    {
        var transfer = Interlocked.Exchange(ref _data, null);
        if (transfer is null)
            return;

        var disposed = new HashSet<IDisposable>(ReferenceEqualityComparer.Instance);
        List<Exception>? errors = null;
        foreach (var item in transfer.Items)
        {
            foreach (var format in item.Formats)
            {
                try
                {
                    if (item.TryGetRaw(format) is IDisposable resource && disposed.Add(resource))
                        resource.Dispose();
                }
                catch (Exception ex)
                {
                    if (throwOnError)
                        (errors ??= []).Add(ex);
                }
            }
        }
        if (errors is not null)
            throw new AggregateException(errors);
    }
}
