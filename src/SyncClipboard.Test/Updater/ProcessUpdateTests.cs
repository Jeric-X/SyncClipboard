using SyncClipboard.Updater;
using System.Diagnostics;

namespace SyncClipboard.Test.Updater;

[TestClass]
[TestCategory("PlatformWindows")]
[TestCategory("PlatformMacOS")]
[TestCategory("PlatformLinux")]
public class ProcessUpdateTests : UpdaterTestBase
{
    [TestMethod]
    public void InstallationLock_RejectsSameTargetAndAllowsDifferentTargetsAndReacquisition()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Requires a supported updater platform.");
            return;
        }
        var sameTarget = OperatingSystem.IsWindows() ? target.ToUpperInvariant() + "\\" : target + "/";
        using (UpdateWorker.AcquireInstallationLock(target))
        {
            Assert.Throws<IOException>(() => UpdateWorker.AcquireInstallationLock(sameTarget));
            using var other = UpdateWorker.AcquireInstallationLock(stage);
        }
        using var reacquired = UpdateWorker.AcquireInstallationLock(target);
    }

    [TestMethod]
    public async Task ParentWait_HonorsCancellationAndDoesNotWaitForReusedPid()
    {
        using var current = Process.GetCurrentProcess();
        await UpdateWorker.WaitForProcessAsync(current.Id, current.StartTime.ToUniversalTime().Ticks + 1, CancellationToken.None);
        await Assert.ThrowsAsync<OperationCanceledException>(() => UpdateWorker.WaitForProcessAsync(current.Id, 0,
            new CancellationToken(true)));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ParentWait_RequiresConfirmationBeforeForcingExit(bool confirm)
    {
        using var process = StartWaitingProcess();
        var prompted = false;
        try
        {
            var wait = UpdateWorker.WaitForProcessAsync(process.Id, process.StartTime.ToUniversalTime().Ticks,
                TestContext.CancellationTokenSource.Token, _ =>
                {
                    prompted = true;
                    Assert.IsFalse(process.HasExited);
                    return Task.FromResult(confirm ? ForceExitAction.Yes : ForceExitAction.No);
                }, TimeSpan.FromMilliseconds(50));
            if (confirm)
            {
                await wait;
                Assert.IsTrue(process.HasExited);
            }
            else
            {
                await Assert.ThrowsAsync<UpdateProcessExitException>(() => wait);
                Assert.IsFalse(process.HasExited);
            }
            Assert.IsTrue(prompted);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task ParentWait_RetryWaitsAgainWithoutKillingProcess()
    {
        using var process = StartWaitingProcess();
        var prompts = 0;
        try
        {
            await UpdateWorker.WaitForProcessAsync(process.Id, process.StartTime.ToUniversalTime().Ticks,
                TestContext.CancellationTokenSource.Token, async _ =>
                {
                    Assert.IsFalse(process.HasExited);
                    if (++prompts == 1)
                        return ForceExitAction.Retry;
                    process.Kill();
                    await process.WaitForExitAsync(CancellationToken.None);
                    return ForceExitAction.Retry;
                }, TimeSpan.FromMilliseconds(50));
            Assert.AreEqual(2, prompts);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task ParentWait_ContinuesWithoutPromptWhenProcessExits()
    {
        using var process = StartWaitingProcess();
        try
        {
            var wait = UpdateWorker.WaitForProcessAsync(process.Id, process.StartTime.ToUniversalTime().Ticks,
                TestContext.CancellationTokenSource.Token, _ => throw new AssertFailedException("Unexpected force-exit prompt."));
            process.Kill();
            await wait;
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task ParentWait_CancellationDoesNotPromptOrKillProcess()
    {
        using var process = StartWaitingProcess();
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => UpdateWorker.WaitForProcessAsync(process.Id,
                process.StartTime.ToUniversalTime().Ticks, new CancellationToken(true),
                _ => throw new AssertFailedException("Cancellation must not request a forced exit.")));
            Assert.IsFalse(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}
