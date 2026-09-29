using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SyncClipboard.Updater.Core;

namespace SyncClipboard.Updater.Tests;

[TestClass]
public class UpdateFrameworkTests
{
    public TestContext TestContext { get; set; } = null!;

    private static UpdateRequest Request => new()
    {
        PackageKind = UpdatePackageKind.WindowsZip,
        TargetVersion = "3.4.0-beta1",
        PackagePath = Path.Combine(Path.GetTempPath(), "更新 package.zip"),
        InstallationPath = Path.Combine(Path.GetTempPath(), "安装 directory"),
        ParentProcessId = Environment.ProcessId
    };

    [TestMethod]
    [DataRow("3.4.0-beta1")]
    [DataRow(null)]
    public void GeneratedJsonPreservesRequestAndResult(string? resultVersion)
    {
        var request = Request with { Language = "zh-CN" };
        var json = JsonSerializer.Serialize(request, UpdateJsonContext.Default.UpdateRequest);
        Assert.AreEqual(request, JsonSerializer.Deserialize(json, UpdateJsonContext.Default.UpdateRequest));
        Assert.Contains("WindowsZip", json);
        var result = new UpdateResult(resultVersion, UpdateOutcome.Failed, "无法更新");
        json = JsonSerializer.Serialize(result, UpdateJsonContext.Default.UpdateResult);
        Assert.AreEqual(result, JsonSerializer.Deserialize(json, UpdateJsonContext.Default.UpdateResult));
    }

    [TestMethod]
    public void RequestRejectsUnsupportedOrIncompleteInput()
    {
        UpdateRequest[] invalid =
        [
            Request with { ProtocolVersion = 2 },
            Request with { PackageKind = (UpdatePackageKind)999 },
            Request with { TargetVersion = "" },
            Request with { PackagePath = "relative.zip" },
            Request with { InstallationPath = "relative" },
            Request with { PackagePath = null! },
            Request with { Language = null! },
            Request with { ParentProcessId = 0 }
        ];
        foreach (var request in invalid)
            Assert.ThrowsExactly<InvalidDataException>(request.Validate);
        Assert.ThrowsExactly<JsonException>(() =>
            JsonSerializer.Deserialize("{}", UpdateJsonContext.Default.UpdateRequest));
    }

    [TestMethod]
    public void FactorySelectsOneStrategyAndRejectsMissingOrDuplicateRegistrations()
    {
        var strategy = new TestStrategy();
        var factory = new UpdateStrategyFactory([strategy]);
        Assert.AreSame(strategy, factory.Create(UpdatePackageKind.WindowsZip));
        Assert.ThrowsExactly<NotSupportedException>(() => factory.Create(UpdatePackageKind.MacDmg));
        Assert.ThrowsExactly<ArgumentException>(() => new UpdateStrategyFactory([strategy, strategy]));
    }

    [TestMethod]
    public async Task RunnerReportsStrategyProgressAndOutcome()
    {
        var strategy = new TestStrategy();
        var progress = new RecordedProgress();
        var runner = new UpdateRunner(new UpdateStrategyFactory([strategy]));
        var result = await runner.RunAsync(Request, progress, CancellationToken.None);
        Assert.AreEqual(UpdateOutcome.Succeeded, result.Outcome);
        Assert.AreEqual(1, strategy.Calls);
        CollectionAssert.AreEqual(new[] { UpdatePhase.Preparing, UpdatePhase.Installing }, progress.Phases);
    }

    [TestMethod]
    public async Task RunnerKeepsFailuresAndDoesNotInvokeStrategyForInvalidInput()
    {
        var strategy = new TestStrategy { Failure = new IOException("Expected failure") };
        var runner = new UpdateRunner(new UpdateStrategyFactory([strategy]));
        var result = await runner.RunAsync(Request, new RecordedProgress(), CancellationToken.None);
        Assert.AreEqual(UpdateOutcome.Failed, result.Outcome);
        Assert.AreEqual("Expected failure", result.Error);
        result = await runner.RunAsync(Request with { ProtocolVersion = 2 }, new RecordedProgress(), CancellationToken.None);
        Assert.AreEqual(UpdateOutcome.Failed, result.Outcome);
        Assert.AreEqual(1, strategy.Calls);
    }

    [TestMethod]
    public async Task CancellationBeforeExecutionDoesNotInvokeStrategy()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var strategy = new TestStrategy();
        var runner = new UpdateRunner(new UpdateStrategyFactory([strategy]));
        var result = await runner.RunAsync(Request, new RecordedProgress(), cancellation.Token);
        Assert.AreEqual(UpdateOutcome.Canceled, result.Outcome);
        Assert.AreEqual(0, strategy.Calls);
    }

    [TestMethod]
    public async Task MissingStrategyReturnsFailure()
    {
        var runner = new UpdateRunner(new UpdateStrategyFactory([]));
        var result = await runner.RunAsync(Request, new RecordedProgress(), CancellationToken.None);
        Assert.AreEqual(UpdateOutcome.Failed, result.Outcome);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task TaskFilesRoundtripAndNeverOverwritePreviousFailure()
    {
        var directory = Directory.CreateTempSubdirectory("updater 测试 ").FullName;
        try
        {
            var path = Path.Combine(directory, "request.json");
            var request = Request;
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(request, UpdateJsonContext.Default.UpdateRequest),
                TestContext.CancellationTokenSource.Token);
            Assert.AreEqual(request, await UpdateTaskFile.ReadAsync(path, CancellationToken.None));
            var failure = new UpdateResult(request.TargetVersion, UpdateOutcome.Failed, "Keep this error");
            await UpdateTaskFile.WriteResultAsync(directory, failure);
            await Assert.ThrowsExactlyAsync<IOException>(() => UpdateTaskFile.WriteResultAsync(directory,
                new UpdateResult(request.TargetVersion, UpdateOutcome.Succeeded)));
            var saved = await File.ReadAllTextAsync(Path.Combine(directory, "result.json"), TestContext.CancellationTokenSource.Token);
            Assert.AreEqual(failure, JsonSerializer.Deserialize(saved, UpdateJsonContext.Default.UpdateResult));
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task InvalidTaskStillPersistsFailureWithoutInvokingStrategy()
    {
        UpdateRequest[] invalid =
        [
            Request with { ProtocolVersion = 2 },
            Request with { PackagePath = "relative.zip" },
            Request with { Language = null! }
        ];
        foreach (var request in invalid)
        {
            var directory = Directory.CreateTempSubdirectory("updater invalid ").FullName;
            try
            {
                var path = Path.Combine(directory, "request.json");
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(request, UpdateJsonContext.Default.UpdateRequest),
                    TestContext.CancellationTokenSource.Token);
                var restored = await UpdateTaskFile.ReadAsync(path, TestContext.CancellationTokenSource.Token);
                var strategy = new TestStrategy();
                var runner = new UpdateRunner(new UpdateStrategyFactory([strategy]));
                var result = await runner.RunAsync(restored, new RecordedProgress(), TestContext.CancellationTokenSource.Token);
                await UpdateTaskFile.WriteResultAsync(directory, result);
                var saved = await File.ReadAllTextAsync(Path.Combine(directory, "result.json"), TestContext.CancellationTokenSource.Token);
                var persisted = JsonSerializer.Deserialize(saved, UpdateJsonContext.Default.UpdateResult);
                Assert.IsNotNull(persisted);
                Assert.AreEqual(UpdateOutcome.Failed, persisted.Outcome);
                Assert.AreEqual(request.TargetVersion, persisted.TargetVersion);
                Assert.IsFalse(string.IsNullOrWhiteSpace(persisted.Error));
                Assert.AreEqual(0, strategy.Calls);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class TestStrategy : IUpdateStrategy
    {
        public UpdatePackageKind PackageKind => UpdatePackageKind.WindowsZip;
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }

        public Task ExecuteAsync(UpdateRequest request, IProgress<UpdateProgress> progress, CancellationToken token)
        {
            Calls++;
            progress.Report(new(UpdatePhase.Installing, 50));
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class RecordedProgress : IProgress<UpdateProgress>
    {
        public List<UpdatePhase> Phases { get; } = [];
        public void Report(UpdateProgress value) => Phases.Add(value.Phase);
    }
}
