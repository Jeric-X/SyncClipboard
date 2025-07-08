using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.Desktop.Views;

public partial class ProxySettingDialog : ContentDialog
{
    // workaround for https://github.com/amwx/FluentAvalonia/issues/24, https://github.com/amwx/FluentAvalonia/issues/674
    protected override Type StyleKeyOverride => typeof(ContentDialog);

    private readonly ProxySettingViewModel _viewModel;
    public ProxySettingDialog()
    {
        _viewModel = App.Current.Services.GetRequiredService<ProxySettingViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
    }
}