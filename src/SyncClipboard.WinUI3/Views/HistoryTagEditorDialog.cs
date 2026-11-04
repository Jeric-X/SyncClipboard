using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using System;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.Views;

public sealed class HistoryTagEditorDialog : ContentDialog
{
    public HistoryTagEditorDialog(HistoryTagEditorViewModel editor, Func<Task> save)
    {
        Title = editor.Title;
        PrimaryButtonText = Strings.HistoryTagsApply;
        CloseButtonText = Strings.Cancel;
        DefaultButton = ContentDialogButton.Primary;
        var search = new TextBox { PlaceholderText = Strings.HistoryTagsSearch };
        var rows = new StackPanel { Spacing = 4 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var add = new Button { Content = Strings.HistoryTagsAdd };
        void Render()
        {
            rows.Children.Clear();
            foreach (var choice in editor.FilteredChoices)
            {
                var check = new CheckBox { Content = choice.Name, IsThreeState = true, IsChecked = choice.IsChecked };
                check.Click += (_, _) =>
                {
                    choice.IsChecked = choice.IsChecked != true;
                    check.IsChecked = choice.IsChecked;
                };
                rows.Children.Add(check);
            }
            error.Text = editor.Error;
        }
        search.TextChanged += (_, _) =>
        {
            editor.Search = search.Text;
            Render();
        };
        add.Click += (_, _) =>
        {
            editor.AddCommand.Execute(null);
            search.Text = editor.Search;
            Render();
        };
        Content = new StackPanel
        {
            Width = 420,
            Spacing = 10,
            Children =
            {
                search, add, new ScrollViewer { MaxHeight = 280, Content = rows }, error,
                new TextBlock { Text = Strings.HistoryTagsRetention, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 }
            }
        };
        PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                await save.Invoke();
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                error.Text = ex is ArgumentException ? Strings.HistoryTagsInvalid : ex.Message;
            }
            finally
            {
                deferral.Complete();
            }
        };
        Render();
    }
}
