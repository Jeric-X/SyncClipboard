using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Desktop.Views;
using Key = SyncClipboard.Core.Models.Keyboard.Key;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[DoNotParallelize]
public class HotkeyEditDialogTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Reopening_AfterCloseAnimation_PreservesFocus(bool focusCancelButton)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HotkeyEditDialogTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var dialog = new HotkeyEditDialog();
            var edit = new Button { Content = "Edit shortcut" };
            var window = new Window { Width = 800, Height = 600, Content = edit };
            Task<FAContentDialogResult>? result = null;
            edit.Click += (_, _) => result = dialog.ShowAsync(window);
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                for (var opening = 0; opening < 3; opening++)
                {
                    ClickControl(edit);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                    var input = dialog.GetVisualDescendants().OfType<HotkeyInput>().Single();
                    Assert.IsTrue(input.IsFocused, $"Opening {opening + 1} must focus the recorder immediately.");
                    window.KeyPress(Avalonia.Input.Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
                    window.KeyRelease(Avalonia.Input.Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
                    Assert.AreEqual(new Hotkey(Key.F5), dialog.Hotkey);

                    Control expectedFocus = input;
                    if (focusCancelButton)
                    {
                        expectedFocus = dialog.GetVisualDescendants().OfType<Button>()
                            .Single(button => Equals(button.Content, Strings.Cancel));
                        expectedFocus.Focus();
                    }
                    // Render beyond the opening animation: its completion previously cleared focus.
                    for (var tick = 0; tick < 15; tick++)
                    {
                        await Task.Delay(50, TestContext.CancellationTokenSource.Token);
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        Dispatcher.UIThread.RunJobs();
                        Assert.AreSame(expectedFocus, window.FocusManager?.GetFocusedElement(),
                            $"Opening {opening + 1} lost focus after {(tick + 1) * 50}ms.");
                    }
                    if (focusCancelButton)
                        ClickControl(input);
                    window.KeyPress(Avalonia.Input.Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
                    window.KeyRelease(Avalonia.Input.Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
                    Assert.AreEqual(new Hotkey(Key.F6), dialog.Hotkey);
                    Click(dialog, Strings.Cancel);
                    await result!;
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                }
            }
            finally
            {
                if (result is { IsCompleted: false })
                {
                    dialog.Hide();
                    await result;
                }
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

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
            var backgroundClicks = 0;
            var background = new Button { Content = "Main window" };
            background.Click += (_, _) => backgroundClicks++;
            var window = new Window { Width = 800, Height = 600, Content = background };
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
                window.MouseDown(new Point(10, 10), MouseButton.Left);
                window.MouseUp(new Point(10, 10), MouseButton.Left);
                Assert.AreEqual(0, backgroundClicks, "Dialog must block clicks on the main window after reopening.");
                dialog.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, Strings.Cancel)).Focus();
                Assert.IsFalse(input.IsFocused);
                ClickControl(input);
                Assert.IsTrue(input.IsFocused, "Clicking the recorder must restore keyboard focus.");
                window.KeyPress(Avalonia.Input.Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
                window.KeyRelease(Avalonia.Input.Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
                Assert.AreEqual(new Hotkey(Key.F6), editor.Hotkey);
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
        ClickControl(button);
    }

    private static void ClickControl(Control control)
    {
        var window = (Window)TopLevel.GetTopLevel(control)!;
        window.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
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
