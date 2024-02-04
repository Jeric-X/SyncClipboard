using System.Text;

namespace SyncClipboard.Core.Models;

public class Hotkey
{
    public Key[] Keys { get; }
    public Hotkey(IEnumerable<Key> keys)
    {
        Keys = keys.ToArray();
        Array.Sort(Keys);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        if (obj is not Hotkey other) return false;

        return Enumerable.SequenceEqual(Keys, other.Keys);
    }

    public override int GetHashCode()
    {
        var stringBuilder = new StringBuilder();
        foreach (var key in Keys)
        {
            stringBuilder.Append(key.ToString());
        }
        return stringBuilder.ToString().GetHashCode();
    }

    public static bool operator ==(Hotkey? a, Hotkey? b)
    {
        return Equals(a, b);
    }

    public static bool operator !=(Hotkey? a, Hotkey? b)
    {
        return !Equals(a, b);
    }
}
