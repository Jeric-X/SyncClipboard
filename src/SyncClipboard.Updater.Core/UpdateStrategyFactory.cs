namespace SyncClipboard.Updater.Core;

// The host explicitly registers strategies; no reflection or platform business dependencies are needed.
public sealed class UpdateStrategyFactory(IEnumerable<IUpdateStrategy> strategies)
{
    private readonly Dictionary<UpdatePackageKind, IUpdateStrategy> strategies = strategies.ToDictionary(item => item.PackageKind);

    public IUpdateStrategy Create(UpdatePackageKind kind)
        => strategies.TryGetValue(kind, out var strategy) ? strategy
            : throw new NotSupportedException("This updater does not yet support installing this package.");
}
