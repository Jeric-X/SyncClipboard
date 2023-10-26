namespace SyncClipboard.Core.Clipboard;

public class ProfileTypeHelper
{
    public static ProfileType StringToProfileType(string typeStringName)
    {
        ProfileType type;
        try
        {
            type = (ProfileType)Enum.Parse(typeof(ProfileType), typeStringName);
        }
        catch
        {
            throw new ArgumentException("Profile Type is Wrong");
        }

        return type;
    }

    public static string ClipBoardTypeToString(ProfileType type)
    {
        return Enum.GetName(typeof(ProfileType), type) ?? throw new ArgumentException("Type object is not ProfileTyle");
    }
}
