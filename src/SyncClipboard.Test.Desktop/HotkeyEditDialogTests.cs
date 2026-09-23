using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.Styling;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Desktop.Views;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[DoNotParallelize]
public class HotkeyEditDialogTests
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Editor_FocusesInput_ClearsWithoutClosing_AndKeepsBindings(bool showErrorMessage, bool confirm)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HotkeyEditDialogTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var editor = new EditorState();
            var saves = 0;
            static Binding Bind(string path, BindingMode mode = BindingMode.OneWay) =>
                new(path) { Mode = mode };
            var dialog = new HotkeyEditDialog
            {
                DataContext = editor,
                SaveCommand = new RelayCommand(() => saves++, () => editor.CanSave),
                [!HotkeyEditDialog.HotkeyProperty] = Bind(nameof(editor.Hotkey), BindingMode.TwoWay),
                [!HotkeyEditDialog.IsErrorProperty] = Bind(nameof(editor.HasError)),
                [!HotkeyEditDialog.IsPrimaryButtonEnabledProperty] = Bind(nameof(editor.CanSave))
            };
            if (showErrorMessage)
                dialog.Bind(HotkeyEditDialog.ErrorMessageProperty, Bind(nameof(editor.ErrorMessage)));
            var window = new Window { Width = 800, Height = 600 };
            try
            {
                window.Show();
                var result = dialog.ShowAsync(window);
                Dispatcher.UIThread.RunJobs();
                var input = dialog.GetVisualDescendants().OfType<HotkeyInput>().Single();
                var error = dialog.FindControl<TextBlock>("_ErrorMessage")!;
                Assert.IsTrue(input.IsFocused);
                Assert.AreEqual(editor.Hotkey, input.Hotkey);
                Assert.IsFalse(error.IsVisible);

                editor.HasError = true;
                Assert.IsTrue(input.IsError);
                Assert.IsFalse(dialog.IsPrimaryButtonEnabled);
                Assert.AreEqual(showErrorMessage, error.IsVisible);
                if (showErrorMessage)
                    Assert.AreEqual(editor.ErrorMessage, error.Text);

                Click(dialog, Strings.Clear);
                Assert.AreEqual(Hotkey.Nothing, editor.Hotkey);
                Assert.IsTrue(input.IsFocused);
                Assert.IsTrue(dialog.IsVisible);
                Assert.IsFalse(result.IsCompleted);
                Assert.AreEqual(0, saves);

                editor.Hotkey = new Hotkey(Key.F3);
                Assert.AreEqual(editor.Hotkey, input.Hotkey);
                input.SetCurrentValue(HotkeyInput.HotkeyProperty, new Hotkey(Key.F4));
                Assert.AreEqual(new Hotkey(Key.F4), editor.Hotkey);
                editor.HasError = false;
                Assert.IsFalse(input.IsError);
                Assert.IsTrue(dialog.IsPrimaryButtonEnabled);
                Assert.IsFalse(error.IsVisible);

                Click(dialog, confirm ? Strings.Confirm : Strings.Cancel);
                await result;
                Assert.AreEqual(confirm ? 1 : 0, saves);

                editor.Hotkey = new Hotkey(Key.F5);
                var reopened = dialog.ShowAsync(window);
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(input.IsFocused);
                Assert.AreEqual(editor.Hotkey, input.Hotkey);
                Click(dialog, Strings.Clear);
                Assert.AreEqual(Hotkey.Nothing, editor.Hotkey);
                Assert.IsFalse(reopened.IsCompleted);
                Click(dialog, Strings.Cancel);
                await reopened;
                Assert.AreEqual(confirm ? 1 : 0, saves);
            }
            finally
            {
                dialog.Hide();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static void Click(HotkeyEditDialog dialog, string text)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, text));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class EditorState : ObservableObject
    {
        private Hotkey hotkey = new(Key.F2);
        private bool hasError;

        public Hotkey Hotkey { get => hotkey; set => SetProperty(ref hotkey, value); }
        public string ErrorMessage => HasError ? "Invalid shortcut" : string.Empty;
        public bool CanSave => !HasError;
        public bool HasError
        {
            get => hasError;
            set
            {
                if (SetProperty(ref hasError, value))
                {
                    OnPropertyChanged(nameof(CanSave));
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            }
        }
    }
}
