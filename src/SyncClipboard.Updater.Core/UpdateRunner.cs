namespace SyncClipboard.Updater.Core;

public sealed class UpdateRunner(UpdateStrategyFactory factory)
{
    public async Task<UpdateResult> RunAsync(UpdateRequest request, IProgress<UpdateProgress> progress, CancellationToken token)
    {
        try
        {
            request.Validate();
            token.ThrowIfCancellationRequested();
            var strategy = factory.Create(request.PackageKind);
            progress.Report(new(UpdatePhase.Preparing));
            await strategy.ExecuteAsync(request, progress, token);
            return new(request.TargetVersion, UpdateOutcome.Succeeded);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(request.TargetVersion, UpdateOutcome.Canceled);
        }
        catch (Exception error)
        {
            return new(request.TargetVersion, UpdateOutcome.Failed, error.Message);
        }
    }
}
