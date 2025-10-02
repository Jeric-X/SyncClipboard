using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels.Sub;
using System.ComponentModel;

namespace SyncClipboard.Core.ViewModels;

public partial class ServiceStatusViewModel(IThreadDispatcher dispatcher) : ObservableObject
{
    public BindingList<ServiceStatus> StatusList { get; } = [];

    public void SetStatusString(string name, string statusStr, bool? error = null)
    {
        dispatcher.RunOnMainThreadAsync(() =>
        {
            foreach (var item in StatusList)
            {
                if (item.Name == name)
                {
                    item.IsError = error ?? item.IsError;
                    item.StatusString = statusStr;
                    return;
                }
            }
            var newitem = StatusList.AddNew();
            newitem.StatusString = statusStr;
            newitem.Name = name;
            newitem.IsError = error ?? newitem.IsError;
        });
    }
}
