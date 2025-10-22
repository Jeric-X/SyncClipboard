using SyncClipboard.Abstract;

namespace SyncClipboard.Abstract.Profiles;

public abstract class Profile
{
    public abstract ProfileType Type { get; }
    public abstract string GetDisplayText();
    public abstract ValueTask<string> GetLogId(CancellationToken token);
    public abstract Task<bool> IsLocalDataValid(bool quick, CancellationToken token);
    public abstract Task<ClipboardProfileDTO> ToDto(CancellationToken token);
    protected abstract Task<bool> Same(Profile rhs, CancellationToken token);

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

    public override bool Equals(object? obj)
    {
        throw new NotSupportedException("Use Profile.Same to compare two profiles.");
    }

    public override int GetHashCode()
    {
        throw new NotImplementedException();
    }
}
