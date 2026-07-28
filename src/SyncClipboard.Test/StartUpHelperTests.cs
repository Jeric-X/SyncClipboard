using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class StartUpHelperTests
{
    private const string UserSid = "S-1-5-21-1000";
    private const string ProgramPath = @"C:\Apps\SyncClipboard\SyncClipboard.exe";
    private const string ProgramDirectory = @"C:\Apps\SyncClipboard";

    [TestMethod]
    [DataRow(false, "LeastPrivilege")]
    [DataRow(true, "HighestAvailable")]
    public void BuildWindowsTaskXmlContainsRequiredStartupSettings(bool runAsAdmin, string expectedRunLevel)
    {
        var xml = StartUpHelper.BuildWindowsTaskXml(
            UserSid,
            runAsAdmin,
            ProgramPath,
            ProgramDirectory);
        var document = XDocument.Parse(xml);
        var ns = document.Root!.Name.Namespace;

        Assert.AreEqual(UserSid, document.Descendants(ns + "Principal").Single().Element(ns + "UserId")!.Value);
        Assert.AreEqual("InteractiveToken", document.Descendants(ns + "LogonType").Single().Value);
        Assert.AreEqual(expectedRunLevel, document.Descendants(ns + "RunLevel").Single().Value);
        Assert.AreEqual(UserSid, document.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
        Assert.AreEqual("false", document.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.AreEqual("false", document.Descendants(ns + "StopIfGoingOnBatteries").Single().Value);
        Assert.AreEqual("PT0S", document.Descendants(ns + "ExecutionTimeLimit").Single().Value);
        Assert.AreEqual(ProgramPath, document.Descendants(ns + "Command").Single().Value);
        Assert.IsFalse(document.Descendants(ns + "Arguments").Any());
        Assert.AreEqual(ProgramDirectory, document.Descendants(ns + "WorkingDirectory").Single().Value);

        var securityDescriptor = document.Descendants(ns + "SecurityDescriptor").SingleOrDefault();
        if (runAsAdmin)
        {
            Assert.IsNull(securityDescriptor);
        }
        else
        {
            Assert.IsNotNull(securityDescriptor);
            Assert.Contains($"(A;;FA;;;{UserSid})", securityDescriptor.Value);
        }
    }

    [TestMethod]
    public void GetWindowsTaskNameScopesTaskToCurrentUser()
    {
        Assert.AreEqual(
            $"SyncClipboard-{UserSid}",
            StartUpHelper.GetWindowsTaskName("SyncClipboard", UserSid));
    }

    [TestMethod]
    public void NormalizeWindowsUserIdResolvesCurrentAccountToSid()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows account translation is only available on Windows.");
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var expectedSid = identity.User!.Value;

        Assert.AreEqual(expectedSid, StartUpHelper.NormalizeWindowsUserId(expectedSid));
        Assert.AreEqual(expectedSid, StartUpHelper.NormalizeWindowsUserId(identity.Name));
        Assert.AreEqual(expectedSid, StartUpHelper.NormalizeWindowsUserId(Environment.UserName));
    }

    [TestMethod]
    [DataRow(false, false, "")]
    [DataRow(true, true, "runas")]
    public void CreateSchtasksStartInfoRequestsElevationOnlyWhenRequired(
        bool elevate,
        bool expectedUseShellExecute,
        string expectedVerb)
    {
        var arguments = new[] { "/Create", "/TN", $"SyncClipboard-{UserSid}", "/F" };

        var startInfo = StartUpHelper.CreateSchtasksStartInfo(arguments, elevate);

        Assert.AreEqual("schtasks.exe", startInfo.FileName);
        Assert.AreEqual(expectedUseShellExecute, startInfo.UseShellExecute);
        Assert.AreEqual(expectedVerb, startInfo.Verb);
        CollectionAssert.AreEqual(arguments, startInfo.ArgumentList.ToArray());
    }

    [TestMethod]
    public void CreateElevatedPowerShellStartInfoUsesSingleRunAsProcess()
    {
        const string command = "Write-Output 'trusted command'";

        var startInfo = StartUpHelper.CreateElevatedPowerShellStartInfo(command);

        Assert.AreEqual("powershell.exe", startInfo.FileName);
        Assert.IsTrue(startInfo.UseShellExecute);
        Assert.AreEqual("runas", startInfo.Verb);
        Assert.AreEqual("-NoProfile", startInfo.ArgumentList[0]);
        Assert.AreEqual("-NonInteractive", startInfo.ArgumentList[1]);
        Assert.AreEqual("-ExecutionPolicy", startInfo.ArgumentList[2]);
        Assert.AreEqual("Bypass", startInfo.ArgumentList[3]);
        Assert.AreEqual("-EncodedCommand", startInfo.ArgumentList[4]);

        var bootstrap = Encoding.Unicode.GetString(
            Convert.FromBase64String(startInfo.ArgumentList[5]));
        Assert.IsFalse(bootstrap.Contains(command, StringComparison.Ordinal));
        Assert.IsTrue(bootstrap.Contains("GZipStream", StringComparison.Ordinal));
        Assert.IsTrue(bootstrap.Contains("ScriptBlock", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WindowsStartupMigrationRequestsElevationForEveryPrivilegedTaskTransition()
    {
        var normalTask = CreateTaskInfo(runAsAdmin: false);
        var adminTask = CreateTaskInfo(runAsAdmin: true);

        Assert.IsFalse(StartUpHelper.NeedsWindowsStartupMigrationElevation(
            runAsAdmin: false,
            currentTask: normalTask,
            legacyTask: normalTask));
        Assert.IsTrue(StartUpHelper.NeedsWindowsStartupMigrationElevation(
            runAsAdmin: true,
            currentTask: normalTask,
            legacyTask: normalTask));
        Assert.IsTrue(StartUpHelper.NeedsWindowsStartupMigrationElevation(
            runAsAdmin: false,
            currentTask: adminTask,
            legacyTask: normalTask));
        Assert.IsTrue(StartUpHelper.NeedsWindowsStartupMigrationElevation(
            runAsAdmin: false,
            currentTask: normalTask,
            legacyTask: adminTask));
    }

    [TestMethod]
    public void AutomaticMigrationRequiresDefinitivelyMissingNewTaskAndReadableLegacyState()
    {
        Assert.IsTrue(StartUpHelper.CanAutomaticallyMigrateWindowsStartup(
            WindowsTaskLookupStatus.NotFound,
            WindowsTaskLookupStatus.NotFound,
            legacyTaskIsExpectedAndUnprivileged: false,
            registryEntryExists: true));
        Assert.IsTrue(StartUpHelper.CanAutomaticallyMigrateWindowsStartup(
            WindowsTaskLookupStatus.NotFound,
            WindowsTaskLookupStatus.Found,
            legacyTaskIsExpectedAndUnprivileged: true,
            registryEntryExists: false));
        Assert.IsFalse(StartUpHelper.CanAutomaticallyMigrateWindowsStartup(
            WindowsTaskLookupStatus.Found,
            WindowsTaskLookupStatus.NotFound,
            legacyTaskIsExpectedAndUnprivileged: false,
            registryEntryExists: true));
        Assert.IsFalse(StartUpHelper.CanAutomaticallyMigrateWindowsStartup(
            WindowsTaskLookupStatus.Error,
            WindowsTaskLookupStatus.NotFound,
            legacyTaskIsExpectedAndUnprivileged: false,
            registryEntryExists: true));
        Assert.IsFalse(StartUpHelper.CanAutomaticallyMigrateWindowsStartup(
            WindowsTaskLookupStatus.NotFound,
            WindowsTaskLookupStatus.Error,
            legacyTaskIsExpectedAndUnprivileged: false,
            registryEntryExists: true));
        Assert.IsFalse(StartUpHelper.CanAutomaticallyMigrateWindowsStartup(
            WindowsTaskLookupStatus.NotFound,
            WindowsTaskLookupStatus.Found,
            legacyTaskIsExpectedAndUnprivileged: false,
            registryEntryExists: true));
    }

    [TestMethod]
    public void AdministratorCapabilityUsesTokenGroupMembership()
    {
        Assert.IsTrue(StartUpHelper.ContainsBuiltinAdministratorsSid(
            ["S-1-5-32-545", "S-1-5-32-544"]));
        Assert.IsFalse(StartUpHelper.ContainsBuiltinAdministratorsSid(
            ["S-1-5-32-545"]));
    }

    [TestMethod]
    public void IsExpectedLegacyWindowsTaskRejectsUnexpectedBehavior()
    {
        const string userName = @"DOMAIN\User";
        var task = CreateTaskInfo(runAsAdmin: false) with { TriggerUserId = string.Empty };

        Assert.IsTrue(StartUpHelper.IsExpectedLegacyWindowsTask(
            task,
            UserSid,
            userName,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedLegacyWindowsTask(
            task with { Arguments = "--unexpected" },
            UserSid,
            userName,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedLegacyWindowsTask(
            task with { ActionCount = 2 },
            UserSid,
            userName,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedLegacyWindowsTask(
            task with { TriggerCount = 2 },
            UserSid,
            userName,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedLegacyWindowsTask(
            task with { ExecutionTimeLimit = "P3D" },
            UserSid,
            userName,
            ProgramPath,
            ProgramDirectory));
    }

    [TestMethod]
    public void BuildElevatedPowerShellCommandEmbedsCompressedPayload()
    {
        const string manifestJson = """{"ProgramPath":"C:\\应用\\SyncClipboard.exe"}""";
        const string script = "if ([string]::IsNullOrEmpty($ManifestJson)) { exit 1 }";

        var command = StartUpHelper.BuildElevatedPowerShellCommand(
            manifestJson,
            script);

        Assert.IsFalse(command.Contains(manifestJson, StringComparison.Ordinal));
        Assert.IsTrue(command.EndsWith(script, StringComparison.Ordinal));
        Assert.IsTrue(command.Contains("GZipStream", StringComparison.Ordinal));
    }

    [TestMethod]
    public void IsExpectedWindowsTaskAcceptsExactTask()
    {
        var task = CreateTaskInfo(runAsAdmin: true);

        Assert.IsTrue(StartUpHelper.IsExpectedWindowsTask(
            task,
            UserSid,
            runAsAdmin: true,
            ProgramPath,
            ProgramDirectory));
    }

    [TestMethod]
    public void IsExpectedWindowsTaskRejectsMismatchedTaskFields()
    {
        var task = CreateTaskInfo(runAsAdmin: true);

        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { ExecutablePath = @"C:\Old\SyncClipboard.exe" },
            UserSid,
            runAsAdmin: true,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { WorkingDirectory = @"C:\Old" },
            UserSid,
            runAsAdmin: true,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { PrincipalUserId = "S-1-5-21-2000" },
            UserSid,
            runAsAdmin: true,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { TriggerUserId = "S-1-5-21-2000" },
            UserSid,
            runAsAdmin: true,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { RunLevel = 0 },
            UserSid,
            runAsAdmin: true,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { Arguments = "--unexpected" },
            UserSid,
            runAsAdmin: true,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { ActionCount = 2 },
            UserSid,
            runAsAdmin: true,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { TriggerCount = 2 },
            UserSid,
            runAsAdmin: true,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { PrincipalLogonType = 2 },
            UserSid,
            runAsAdmin: true,
            ProgramPath,
            ProgramDirectory));
    }

    [TestMethod]
    public void IsExpectedWindowsTaskCanIgnoreRunLevelForStartupStatus()
    {
        var task = CreateTaskInfo(runAsAdmin: true);

        Assert.IsTrue(StartUpHelper.IsExpectedWindowsTask(
            task,
            UserSid,
            runAsAdmin: null,
            ProgramPath,
            ProgramDirectory));
    }

    [TestMethod]
    public void IsExpectedWindowsTaskRejectsDisabledOrRestrictedTask()
    {
        var task = CreateTaskInfo(runAsAdmin: false);

        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { Enabled = false },
            UserSid,
            runAsAdmin: false,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { TriggerEnabled = false },
            UserSid,
            runAsAdmin: false,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { DisallowStartIfOnBatteries = true },
            UserSid,
            runAsAdmin: false,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { StopIfGoingOnBatteries = true },
            UserSid,
            runAsAdmin: false,
            ProgramPath,
            ProgramDirectory));
        Assert.IsFalse(StartUpHelper.IsExpectedWindowsTask(
            task with { ExecutionTimeLimit = "P3D" },
            UserSid,
            runAsAdmin: false,
            ProgramPath,
            ProgramDirectory));
    }

    private static WindowsStartupTaskInfo CreateTaskInfo(bool runAsAdmin)
    {
        return new WindowsStartupTaskInfo(
            ProgramPath,
            Arguments: string.Empty,
            ProgramDirectory,
            ActionCount: 1,
            UserSid,
            PrincipalLogonType: 3,
            UserSid,
            TriggerCount: 1,
            runAsAdmin ? 1 : 0,
            Enabled: true,
            TriggerEnabled: true,
            DisallowStartIfOnBatteries: false,
            StopIfGoingOnBatteries: false,
            ExecutionTimeLimit: "PT0S");
    }
}
