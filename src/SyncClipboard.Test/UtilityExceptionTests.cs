using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
[TestCategory("NonUI")]
public class UtilityExceptionTests
{
    [TestMethod]
    public void ShouldWrapLocalReadFailure_OnlyWrapsUnclassifiedReadFailures()
    {
        Exception[] readFailures = [new IOException(), new FileNotFoundException(), new UnauthorizedAccessException()];
        Exception[] otherFailures =
        [
            new LocalProfileDataUnavailableException("Already classified"),
            new InvalidOperationException(),
            new OperationCanceledException(),
        ];

        foreach (var exception in readFailures)
            Assert.IsTrue(Utility.ShouldWrapLocalReadFailure(exception, CancellationToken.None));
        foreach (var exception in otherFailures)
            Assert.IsFalse(Utility.ShouldWrapLocalReadFailure(exception, CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var exception in readFailures)
            Assert.IsFalse(Utility.ShouldWrapLocalReadFailure(exception, cancellation.Token));
    }
}
