using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test;

[TestClass]
public class RootDirectoryProfileTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    public async Task CreateProfileFromMeta_RootDirectory_PreservesGroupAndSelectedPaths(bool contentControl, bool mixedSelection)
    {
        using var configurationServices = new ConfigurationTestServices();
        var directory = Directory.CreateTempSubdirectory("RootDirectoryProfile-");
        try
        {
            var configPath = Path.Combine(directory.FullName, "config.json");
            await File.WriteAllTextAsync(configPath, """{"ConfigVersion":1}""", TestContext.CancellationTokenSource.Token);
            var config = new ConfigManager(configPath, configurationServices.Upgrader);
            using var services = new ServiceCollection().AddSingleton(config).BuildServiceProvider();
            var root = Path.GetPathRoot(directory.FullName)!;
            string[] paths = mixedSelection ? [directory.FullName, root] : [root];
            var factory = new TestClipboardFactory(services, new ClipboardMetaInfomation { Files = paths });

            var profile = await factory.CreateProfileFromMeta(factory.Meta, contentControl, TestContext.CancellationTokenSource.Token);

            Assert.IsInstanceOfType<GroupProfile>(profile);
            var group = (GroupProfile)profile;
            Assert.IsTrue(group.ContainsRootDirectory);
            CollectionAssert.AreEquivalent(paths, group.Files);
            Assert.IsTrue(group.DisplayText.Split('\n').Contains(root));
            Assert.IsInstanceOfType<GroupProfile>(await factory.CreateProfileFromLocal(TestContext.CancellationTokenSource.Token));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void RootDirectoryFlag_RecognizesNormalizedPathsAndFollowsLocalPaths()
    {
        var root = Path.GetPathRoot(Directory.GetCurrentDirectory())!;
        string[] roots =
        [
            root,
            Path.Combine(root, "."),
            Path.Combine(root, "folder", ".."),
            Path.GetRelativePath(Directory.GetCurrentDirectory(), root)
        ];
        foreach (var path in roots)
        {
            var profile = new GroupProfile([path]);
            Assert.IsTrue(profile.ContainsRootDirectory);
            Assert.AreEqual(root, profile.DisplayText);
        }

        var rootProfile = new GroupProfile(new ProfilePersistentInfo
        {
            Type = ProfileType.Group,
            FilePaths = [root],
            Hash = new string('A', 64),
            Text = root,
            Size = 0
        });
        var ordinaryProfile = new GroupProfile([Path.Combine(root, "ordinary") + Path.DirectorySeparatorChar]);
        var copy = new GroupProfile([]);
        Assert.IsTrue(rootProfile.ContainsRootDirectory);
        Assert.IsFalse(ordinaryProfile.ContainsRootDirectory);
        rootProfile.CopyTo(copy);
        Assert.IsTrue(copy.ContainsRootDirectory);
        ordinaryProfile.CopyTo(copy);
        Assert.IsFalse(copy.ContainsRootDirectory);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Upload_RootDirectory_SkipsBeforeHashingEvenWhenContentControlIsDisabled(bool contentControl)
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var profile = new NoContentReadGroupProfile([root]);
        var logger = new Mock<ILogger>();
        var tray = new Mock<ITrayIcon>();
        // Exercise the upload entry point without starting application services or connecting to a server.
        var upload = (UploadService)RuntimeHelpers.GetUninitializedObject(typeof(UploadService));
        foreach (var (name, value) in new (string, object)[]
        {
            ("_logger", logger.Object), ("_trayIcon", tray.Object)
        })
        {
            typeof(UploadService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(upload, value);
        }
        var checkAndUpload = typeof(UploadService).GetMethod("CheckAndUpload", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var result = await (Task<UploadResult>)checkAndUpload.Invoke(upload,
            [new ClipboardMetaInfomation { Files = [root] }, profile, contentControl, TestContext.CancellationTokenSource.Token, false])!;

        Assert.IsFalse(result.Success);
        Assert.AreEqual(SyncClipboard.Core.I18n.Strings.RootDirectoryNotSupported, result.Reason);
        tray.Verify(item => item.ShowUploadAnimation(), Times.Never);
    }

    [TestMethod]
    public async Task Upload_LocalSelectionChangedToRoot_IsObsoleteWithoutHashing()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows skips the local profile obsolescence check.");
        }

        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var clipboard = new Mock<IClipboardFactory>();
        clipboard.Setup(factory => factory.CreateProfileFromLocal(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoContentReadGroupProfile([root]));
        var upload = (UploadService)RuntimeHelpers.GetUninitializedObject(typeof(UploadService));
        typeof(UploadService).GetField("_clipboardFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(upload, clipboard.Object);
        var isObsolete = typeof(UploadService).GetMethod("IsObsoleteProfile", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var obsolete = await (Task<bool>)isObsolete.Invoke(upload,
            [new NoContentReadGroupProfile([Path.Combine(root, "ordinary")]), TestContext.CancellationTokenSource.Token])!;

        Assert.IsTrue(obsolete);
    }

    internal sealed class NoContentReadGroupProfile(IEnumerable<string> paths) : GroupProfile(paths)
    {
        protected override Task ComputeHash(CancellationToken token)
            => throw new AssertFailedException("Root directory contents must not be hashed by this service.");

        protected override Task ComputeSize(CancellationToken token)
            => throw new AssertFailedException("Root directory contents must not be scanned by this service.");
    }

    private sealed class TestClipboardFactory(IServiceProvider services, ClipboardMetaInfomation meta) : ClipboardFactoryBase
    {
        public ClipboardMetaInfomation Meta { get; } = meta;
        protected override ILogger Logger { get; set; } = Mock.Of<ILogger>();
        protected override IServiceProvider ServiceProvider { get; set; } = services;

        public override Task<ClipboardMetaInfomation> GetMetaInfomation(CancellationToken ctk) => Task.FromResult(Meta);
    }
}
