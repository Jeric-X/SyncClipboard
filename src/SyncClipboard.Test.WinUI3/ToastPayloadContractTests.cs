using Microsoft.Toolkit.Uwp.Notifications;
using NativeNotification.Interface;
using NativeNotification.Windows;
using System.Xml.Linq;

namespace SyncClipboard.Test.WinUI3;

[TestClass]
[TestCategory("NonUI")]
public class ToastPayloadContractTests
{
    private static readonly string[] TextBindings =
        ["{TOAST_BINDING_TITLE}", "{TOAST_BINDING_TEXT1}", "{TOAST_BINDING_TEXT2}"];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NotificationPayload_PreservesTextBindingsAndOptionalImage(bool includeImage)
    {
        var image = new Uri("file:///C:/clipboard/%E5%9B%BE%E7%89%87%20%26%20test.png");
        var session = new PayloadSession
        {
            Title = "同步 <完成>",
            Message = "第一行 & 第二行",
            Image = includeImage ? image : null
        };

        var xml = Parse(session.Build());

        Assert.AreEqual("toast", xml.Name.LocalName);
        var binding = xml.Element("visual")!.Element("binding")!;
        Assert.AreEqual("ToastGeneric", (string?)binding.Attribute("template"));
        Assert.AreSequenceEqual(TextBindings, binding.Elements("text").Select(x => x.Value).ToArray());
        Assert.AreEqual(includeImage ? image.OriginalString : null, (string?)binding.Element("image")?.Attribute("src"));
        Assert.IsNull(xml.Element("actions"));
        Assert.IsFalse(session.IsAlive);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("61ee73cf-19f0-41b6-8187-a5e4a905aa35")]
    [DataRow("action=copy;path=C:\\文件 & 图片\\a.png")]
    [DataRow("<open>\"quoted\"=100%25&x+y;✓")]
    public void Buttons_RoundTripOpaqueActivationIdsWithoutInvokingCallbacks(string actionId)
    {
        var calls = 0;
        var session = new PayloadSession
        {
            Buttons =
            [
                new ActionButton("复制 <内容> & \"图片\"", () => calls++, actionId),
                new ActionButton("打开", () => calls++, "second")
            ]
        };

        var actions = Parse(session.Build()).Element("actions")!.Elements("action").ToArray();

        Assert.HasCount(2, actions);
        Assert.AreEqual(session.Buttons[0].Text, (string?)actions[0].Attribute("content"));
        Assert.AreEqual(actionId, (string?)actions[0].Attribute("arguments"));
        Assert.AreEqual("second", (string?)actions[1].Attribute("arguments"));
        Assert.IsTrue(actions.All(x => (string?)x.Attribute("activationType") == "background"));
        Assert.AreEqual(0, calls);
        Assert.IsFalse(session.IsAlive);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProgressPayload_PreservesBindingsAndIndeterminateMode(bool indeterminate)
    {
        var session = new PayloadProgressSession { IsIndeterminate = indeterminate };

        var binding = Parse(session.Build()).Element("visual")!.Element("binding")!;
        var progress = binding.Element("progress")!;

        Assert.AreSequenceEqual(TextBindings, binding.Elements("text").Select(x => x.Value).ToArray());
        Assert.AreEqual("{PROGRESS_BINDING_TITLE}", (string?)progress.Attribute("title"));
        Assert.AreEqual(indeterminate ? "indeterminate" : "{PROGRESS_BINDING_VALUE}", (string?)progress.Attribute("value"));
        Assert.AreEqual("{PROGRESS_BINDING_VALUE_TIP}", (string?)progress.Attribute("valueStringOverride"));
        Assert.AreEqual("{PROGRESS_BINDING_STATUS}", (string?)progress.Attribute("status"));
        Assert.IsFalse(session.IsAlive);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SilentPayload_AcceptsTheNullAudioSourceUsedByNativeNotification(bool silent)
    {
        var session = new PayloadProgressSession();
        var builder = session.Build();
        // Exercise only the payload operation used by ToastSession.Show, never Show itself.
        if (silent)
            builder.AddAudio(null!, null, true);

        var audio = Parse(builder).Element("audio");

        if (silent)
        {
            Assert.IsNotNull(audio);
            Assert.AreEqual("true", (string?)audio.Attribute("silent"));
            Assert.IsNull(audio.Attribute("src"));
        }
        else
            Assert.IsNull(audio);
        Assert.IsFalse(session.IsAlive);
    }

    [TestMethod]
    public void RebuildingPayload_RemovesOldImagesAndActionsWithoutMutatingPreviousPayload()
    {
        var session = new PayloadSession
        {
            Image = new Uri("file:///C:/clipboard/previous.png"),
            Buttons = [new ActionButton("旧操作", "old")]
        };
        var previous = session.Build();
        session.Image = null;
        session.Buttons = [];

        var current = Parse(session.Build());

        Assert.IsNull(current.Descendants("image").SingleOrDefault());
        Assert.IsNull(current.Element("actions"));
        Assert.IsNotNull(Parse(previous).Descendants("image").Single());
        Assert.AreEqual("old", (string?)Parse(previous).Descendants("action").Single().Attribute("arguments"));
        Assert.IsFalse(session.IsAlive);
    }

    private static XElement Parse(ToastContentBuilder builder) => XElement.Parse(builder.GetToastContent().GetContent());

    // These constructors only retain the unused manager reference. GetBuilder builds managed data;
    // do not call Show, Update, Remove, GetToast, GetXml, or construct a notification manager here.
    private sealed class PayloadSession() : ToastSession(null!)
    {
        public ToastContentBuilder Build() => GetBuilder();
    }

    private sealed class PayloadProgressSession() : ProgressSession(null!)
    {
        public ToastContentBuilder Build() => GetBuilder();
    }
}
