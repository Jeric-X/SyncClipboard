using SyncClipboard.Core.Utilities;
using System.Text.Json.Serialization;

namespace SyncClipboard.Core.Models.Keyboard;

public class Hotkey
{
    public static readonly Hotkey Nothing = new Hotkey();

    public Key[] Keys { get; }
    public Hotkey(IEnumerable<Key> keys)
    {
        Keys = keys.Distinct().ToArray();
        Array.Sort(Keys);
    }

    [JsonConstructor]
    public Hotkey(params Key[] keys)
    {
        Keys = keys.Distinct().ToArray();
        Array.Sort(Keys);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        if (obj is not Hotkey other) return false;

        return Keys.SequenceEqual(other.Keys);
    }

    public override int GetHashCode()
    {
        return Keys.ListHashCode();
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
