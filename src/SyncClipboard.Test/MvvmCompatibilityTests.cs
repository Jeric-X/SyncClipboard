using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moq;
using SyncClipboard.Core.ViewModels;
using System.ComponentModel.DataAnnotations;

namespace SyncClipboard.Test;

[TestClass]
[TestCategory("NonUI")]
public partial class MvvmCompatibilityTests
{
    private static readonly string[] ValueEvents = ["changing:Value:before", "changed:Value:after ✓"];
    private static readonly string[] TreeEvents = ["TreeList", "ShowTreeList", "TreeList", "ShowTreeList"];
    private static readonly string[] ValidationEvents = ["Name", "Name"];
    private static readonly string[] RecordedValues = ["first", "second"];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void GeneratedProperty_NotifiesBeforeAndAfterMutationOnlyWhenValueChanges()
    {
        var model = new PropertyInputViewModel { Value = "before" };
        List<string> events = [];
        model.PropertyChanging += (_, e) => events.Add($"changing:{e.PropertyName}:{model.Value}");
        model.PropertyChanged += (_, e) => events.Add($"changed:{e.PropertyName}:{model.Value}");

        model.Value = "after ✓";
        model.Value = "after ✓";

        CollectionAssert.AreEqual(ValueEvents, events);
    }

    [TestMethod]
    public void DependentProperty_TracksListReplacementWithoutResolvingPlatformServices()
    {
        var services = new Mock<IServiceProvider>(MockBehavior.Strict);
        var model = new NextCloudLogInViewModel(services.Object);
        List<string?> events = [];
        model.PropertyChanged += (_, e) => events.Add(e.PropertyName);
        List<FileTreeViewModel> nodes = [new("/folder", "文件夹", true)];

        model.TreeList = nodes;
        Assert.IsTrue(model.ShowTreeList);
        model.TreeList = nodes;
        model.TreeList = null;

        Assert.IsFalse(model.ShowTreeList);
        CollectionAssert.AreEqual(TreeEvents, events);
        services.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(typeof(uint), 0d, true)]
    [DataRow(typeof(uint), -1d, false)]
    [DataRow(typeof(ushort), 65535d, true)]
    [DataRow(typeof(ushort), 65536d, false)]
    [DataRow(typeof(int), -2147483648d, true)]
    [DataRow(typeof(int), 2147483648d, false)]
    [DataRow(typeof(ushort?), -1d, false)]
    public void IntegerInput_ValidatesTheValueAssignedThroughGeneratedProperties(Type type, double value, bool valid)
    {
        var model = new PropertyInputViewModel
        {
            InputType = PropertyInputType.Integer,
            PropertyType = type,
            NumericValue = value
        };

        Assert.AreEqual(value, model.NumericValue);
        Assert.AreEqual(valid, model.IsValid);
    }

    [TestMethod]
    public void GeneratedValidation_ReportsAndClearsErrorsForTheChangedProperty()
    {
        var model = new ValidationProbe();
        List<string?> errors = [];
        model.ErrorsChanged += (_, e) => errors.Add(e.PropertyName);

        model.Name = "x";

        Assert.IsTrue(model.HasErrors);
        Assert.HasCount(1, model.GetErrors(nameof(model.Name)));
        model.Name = "valid";
        Assert.IsFalse(model.HasErrors);
        Assert.IsEmpty(model.GetErrors(nameof(model.Name)));
        CollectionAssert.AreEqual(ValidationEvents, errors);
    }

    [TestMethod]
    public void GeneratedCommand_ReusesInstanceAndNotifiesWhenItsPredicateChanges()
    {
        var model = new CommandProbe();
        var command = model.RecordCommand;
        var changes = 0;
        command.CanExecuteChanged += (_, _) => changes++;

        Assert.AreSame(command, model.RecordCommand);
        Assert.IsTrue(command.CanExecute("first"));
        command.Execute("first");
        model.Enabled = false;
        Assert.IsFalse(command.CanExecute("second"));
        model.Enabled = false;
        model.Enabled = true;
        Assert.IsTrue(command.CanExecute("second"));
        command.Execute("second");

        Assert.AreEqual(2, changes);
        CollectionAssert.AreEqual(RecordedValues, model.Recorded);
    }

    [TestMethod]
    public async Task GeneratedAsyncCommand_CancelsPendingWorkAndCanRunAgain()
    {
        var model = new CommandProbe();
        CancellationToken observedToken = default;
        model.Operation = token =>
        {
            observedToken = token;
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        var command = model.RunCommand;
        var cancel = model.RunCancelCommand;
        Assert.IsFalse(cancel.CanExecute(null));
        var execution = command.ExecuteAsync(null);
        try
        {
            Assert.AreSame(execution, command.ExecutionTask);
            Assert.IsTrue(command.IsRunning);
            Assert.IsFalse(command.CanExecute(null));
            Assert.IsTrue(cancel.CanExecute(null));
            cancel.Execute(null);
            Assert.IsTrue(observedToken.IsCancellationRequested);
            await Assert.ThrowsAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token));
            Assert.IsFalse(command.IsRunning);
            Assert.IsFalse(cancel.CanExecute(null));
            Assert.IsTrue(command.CanExecute(null));

            model.Operation = token =>
            {
                Assert.IsFalse(token.IsCancellationRequested);
                return Task.CompletedTask;
            };
            await command.ExecuteAsync(null);
            Assert.IsFalse(command.IsCancellationRequested);
            Assert.IsTrue(command.ExecutionTask!.IsCompletedSuccessfully);
        }
        finally
        {
            command.Cancel();
            try { await execution.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token); }
            catch (OperationCanceledException) { }
        }
    }

    [TestMethod]
    public async Task GeneratedAsyncCommand_ExposesFailureAndRecoversForTheNextExecution()
    {
        var failure = new InvalidOperationException("expected failure");
        var model = new CommandProbe { Operation = _ => Task.FromException(failure) };
        var command = model.RunCommand;

        var actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => command.ExecuteAsync(null));

        Assert.AreSame(failure, actual);
        Assert.IsTrue(command.ExecutionTask!.IsFaulted);
        Assert.IsFalse(command.IsRunning);
        Assert.IsTrue(command.CanExecute(null));
        model.Operation = _ => Task.CompletedTask;
        await command.ExecuteAsync(null);
        Assert.IsTrue(command.ExecutionTask!.IsCompletedSuccessfully);
    }

    internal partial class ValidationProbe : ObservableValidator
    {
        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required]
        [MinLength(3)]
        public partial string Name { get; set; } = string.Empty;
    }

    private partial class CommandProbe : ObservableObject
    {
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RecordCommand))]
        public partial bool Enabled { get; set; } = true;
        public List<string> Recorded { get; } = [];
        public Func<CancellationToken, Task> Operation { get; set; } = _ => Task.CompletedTask;

        private bool CanRecord() => Enabled;

        [RelayCommand(CanExecute = nameof(CanRecord))]
        private void Record(string text) => Recorded.Add(text);

        [RelayCommand(IncludeCancelCommand = true)]
        private Task RunAsync(CancellationToken token) => Operation(token);
    }
}
