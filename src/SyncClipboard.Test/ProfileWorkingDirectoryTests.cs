using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileWorkingDirectoryTests
{
    [TestMethod]
    public void GetWorkingDirName_ReturnsTypeAndHash()
    {
        var directoryName = Profile.GetWorkingDirName(ProfileType.Group, "ABC123");

        Assert.AreEqual("Group_ABC123", directoryName);
    }

    [TestMethod]
    public void QueryGetWorkingDir_UsesWorkingDirName()
    {
        var persistentDirectory = Path.Combine("root", "history");

        var workingDirectory = Profile.QueryGetWorkingDir(
            persistentDirectory,
            ProfileType.File,
            "ABC123");

        Assert.AreEqual(Path.Combine(persistentDirectory, "File_ABC123"), workingDirectory);
    }

    [TestMethod]
    public void GetWorkingDirName_HashContainsDirectorySeparator_Throws()
    {
        var invalidHash = $"ABC{Path.DirectorySeparatorChar}123";

        Assert.ThrowsExactly<ArgumentException>(
            () => Profile.GetWorkingDirName(ProfileType.Text, invalidHash));
    }
}
