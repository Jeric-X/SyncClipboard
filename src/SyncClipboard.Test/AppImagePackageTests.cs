using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Updater;
using SyncClipboard.Updater.AppImage;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SyncClipboard.Test;

[TestClass]
public class AppImagePackageTests
{
    public TestContext TestContext { get; set; } = null!;
    private string directory = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateTempSubdirectory("SyncClipboard AppImage 中文 ' ").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, true);

    [TestMethod]
    [DoNotParallelize]
    [DataRow("current", true)]
    [DataRow("parent", true)]
    [DataRow(null, false)]
    [DataRow("unrelated", false)]
    [DataRow("missing", false)]
    [DataRow("relative", false)]
    public void AppImagePath_RequiresProgramInsideAppDirectory(string? location, bool supported)
    {
        var image = Path.Combine(directory, "SyncClipboard.AppImage");
        File.WriteAllBytes(image, ImageBytes());
        var programDirectory = Env.ProgramDirectory;
        var appDirectory = location switch
        {
            "current" => programDirectory,
            "parent" => Path.GetFullPath(Path.Combine(programDirectory, "..")),
            "unrelated" => directory,
            "missing" => Path.Combine(directory, "missing"),
            "relative" => ".",
            _ => null
        };
        var originalImage = Environment.GetEnvironmentVariable("APPIMAGE");
        var originalDirectory = Environment.GetEnvironmentVariable("APPDIR");
        try
        {
            Environment.SetEnvironmentVariable("APPIMAGE", image);
            Environment.SetEnvironmentVariable("APPDIR", appDirectory);
            Assert.AreEqual(supported ? image : null, Env.GetAppImageExecPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPIMAGE", originalImage);
            Environment.SetEnvironmentVariable("APPDIR", originalDirectory);
        }
    }

    [TestMethod]
    [DataRow(null, true)]
    [DataRow("SyncClipboard.Updater", false)]
    [DataRow("libSkiaSharp.so", false)]
    public void Factory_RequiresInstalledAppImageAndCurrentUpdaterFiles(string? missing, bool supported)
    {
        var target = Path.Combine(directory, "renamed application");
        File.WriteAllBytes(target, ImageBytes());
        foreach (var file in new[] { "SyncClipboard.Updater", "libSkiaSharp.so", "libHarfBuzzSharp.so" })
        {
            if (file != missing)
                File.WriteAllText(Path.Combine(directory, file), file);
        }
        var info = new UpdateInfoConfig { ManageType = "manual", UpdateSrc = "github", PackageName = "update.AppImage" };
        Assert.AreEqual(supported, UpdateInstallerFactory.Create(info, false, directory, appImagePath: target) is not null);
        Assert.IsNull(UpdateInstallerFactory.Create(info, false, directory, appImagePath: null));
    }

    [TestMethod]
    public async Task Preparation_VerifiesCopiedImageBeforeReplacingTarget()
    {
        var target = Path.Combine(directory, "installed.AppImage");
        var package = Path.Combine(directory, "update.AppImage");
        File.WriteAllText(target, "old");
        var bytes = ImageBytes();
        File.WriteAllBytes(package, bytes);
        var update = Arguments(package, target);
        var attempt = Directory.CreateDirectory(Path.Combine(directory, "attempt")).FullName;
        var snapshot = await AppImagePackage.PrepareAsync(update, attempt, Token);
        File.WriteAllText(package, "changed after preparation");
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(snapshot));
        Assert.AreEqual("old", File.ReadAllText(target));
        var retry = Directory.CreateDirectory(Path.Combine(directory, "retry")).FullName;
        await Assert.ThrowsAsync<InvalidDataException>(() => AppImagePackage.PrepareAsync(update, retry, Token));
        Assert.AreEqual("old", File.ReadAllText(target));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(8)]
    [DataRow(10)]
    [DataRow(18)]
    public async Task Payload_RejectsInvalidFormatOrArchitecture(int offset)
    {
        var path = Path.Combine(directory, "bad.AppImage");
        var bytes = ImageBytes();
        bytes[offset] = 0;
        File.WriteAllBytes(path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => AppImagePackage.ValidatePayloadAsync(path, Token));
    }

    private static UpdateArguments Arguments(string package, string target)
        => new(package, "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))), target, int.MaxValue, "en", []);

    private static byte[] ImageBytes()
    {
        var bytes = new byte[128];
        new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, (byte)'A', (byte)'I', 2 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18, 2), RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? (ushort)183 : (ushort)62);
        return bytes;
    }
}
