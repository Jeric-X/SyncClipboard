using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class NetworkRuleEditDialog : ContentDialog
{
    private readonly NetworkAccountSwitchViewModel _viewModel;

    public NetworkRuleEditor Editor { get; }
    public IReadOnlyList<NetworkInterfaceChoice> Interfaces { get; }

    public NetworkRuleEditDialog(NetworkAccountSwitchViewModel viewModel, NetworkRuleEditor editor)
    {
        _viewModel = viewModel;
        Editor = editor;
        Interfaces = viewModel.Interfaces.ToArray();
        InitializeComponent();
        Editor.PropertyChanged += EditorPropertyChanged;
        Closed += DialogClosed;
        UpdateValidation();
    }

    private void UseCurrentWifiClick(object _, RoutedEventArgs _1)
    {
        _viewModel.UseCurrentWifi(Editor);
        UpdateValidation();
    }

    private void UseCurrentIpClick(object _, RoutedEventArgs _1)
    {
        _viewModel.UseCurrentIp(Editor);
        UpdateValidation();
    }

    private void EditorPropertyChanged(object? _, PropertyChangedEventArgs _1) => UpdateValidation();

    private void UpdateValidation()
    {
        var message = _viewModel.ValidateRuleEditor(Editor, Interfaces);
        _ValidationMessage.Text = message;
        IsPrimaryButtonEnabled = string.IsNullOrEmpty(message);
    }

    private void DialogClosed(ContentDialog _, ContentDialogClosedEventArgs _1)
    {
        Editor.PropertyChanged -= EditorPropertyChanged;
        Closed -= DialogClosed;
    }
}
