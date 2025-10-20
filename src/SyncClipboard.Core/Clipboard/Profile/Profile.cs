using SyncClipboard.Abstract;

namespace SyncClipboard.Core.Clipboard;

public abstract class Profile
{
    #region abstract

    public abstract ProfileType Type { get; }

    public abstract ValueTask<string> GetLogId(CancellationToken token);

    public abstract string ShowcaseText();

    protected abstract Task<bool> Same(Profile rhs, CancellationToken token);

    #endregion

    public virtual Task CheckDownloadedData(CancellationToken token) => Task.CompletedTask;
    public virtual Task<bool> ValidLocalData(bool quick, CancellationToken token) => Task.FromResult(true);

    public abstract Task<ClipboardProfileDTO> ToDto(CancellationToken token);

    public static Task<bool> Same(Profile? lhs, Profile? rhs, CancellationToken token)
    {
        if (ReferenceEquals(lhs, rhs))
        {
            return Task.FromResult(true);
        }

        if (lhs is null)
        {
            return Task.FromResult(rhs is null);
        }

        if (rhs is null)
        {
            return Task.FromResult(false);
        }

        if (lhs.GetType() != rhs.GetType())
        {
            return Task.FromResult(false);
        }

        return lhs.Same(rhs, token);
    }
}
