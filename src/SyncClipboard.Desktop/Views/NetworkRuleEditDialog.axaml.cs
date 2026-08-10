using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SyncClipboard.Desktop.Views;

public partial class NetworkRuleEditDialog : ContentDialog
{
    protected override Type StyleKeyOverride => typeof(ContentDialog);

    public NetworkAccountSwitchViewModel ViewModel { get; }
    public NetworkRuleEditor Editor { get; }
    public IReadOnlyList<NetworkInterfaceChoice> Interfaces { get; }

    public NetworkRuleEditDialog()
        : this(App.Current.Services.GetRequiredService<NetworkAccountSwitchViewModel>())
    {
    }

    private NetworkRuleEditDialog(NetworkAccountSwitchViewModel viewModel)
        : this(viewModel, viewModel.CreateRuleEditor())
    {
    }

    public NetworkRuleEditDialog(NetworkAccountSwitchViewModel viewModel, NetworkRuleEditor editor)
    {
        ViewModel = viewModel;
        Editor = editor;
        Interfaces = viewModel.Interfaces.ToArray();
        DataContext = this;
        InitializeComponent();
        Editor.PropertyChanged += EditorPropertyChanged;
        Closed += DialogClosed;
        UpdateValidation();
    }

    private void UseCurrentWifiClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.UseCurrentWifi(Editor);
        UpdateValidation();
    }

    private void UseCurrentIpClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.UseCurrentIp(Editor);
        UpdateValidation();
    }

    private void EditorPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateValidation();

    private void UpdateValidation()
    {
        var message = ViewModel.ValidateRuleEditor(Editor, Interfaces);
        _ValidationMessage.Text = message;
        IsPrimaryButtonEnabled = string.IsNullOrEmpty(message);
    }

    private void DialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        Editor.PropertyChanged -= EditorPropertyChanged;
        Closed -= DialogClosed;
    }
}
