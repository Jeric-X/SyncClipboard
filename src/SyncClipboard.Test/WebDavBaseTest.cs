using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Web;
using System.Net;
using System.Net.Http.Headers;

namespace SyncClipboard.Test;

[TestClass]
public class WebDavBaseTest
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RequestDisposesTimeoutCancellationSourceAfterCompletion()
    {
        using var handler = new RecordingHandler();
        using var webDav = new TestWebDav(handler);

        var text = await webDav.GetText("test", TestContext.CancellationTokenSource.Token);

        Assert.AreEqual("ok", text);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = handler.CapturedToken.WaitHandle);
    }

    [TestMethod]
    public async Task RequestLinksCallerCancellationToken()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(started);
        using var webDav = new TestWebDav(handler);
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);

        var request = webDav.GetText("test", cancellationSource.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);
        cancellationSource.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => request);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = handler.CapturedToken.WaitHandle);
    }

    [TestMethod]
    public async Task TestAliveForwardsCallerCancellationToken()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(started);
        using var webDav = new TestWebDav(handler);
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);

        var request = webDav.TestAlive(cancellationSource.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);
        cancellationSource.Cancel();

        var result = await request.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);

        Assert.IsFalse(result);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = handler.CapturedToken.WaitHandle);
    }

    [TestMethod]
    public async Task GetJsonWithVersionReturnsResponseETag()
    {
        using var handler = new DelegateHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":\"ok\"}")
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"version-1\"");
            return response;
        });
        using var webDav = new TestWebDav(handler);

        var (payload, version) = await webDav.GetJsonWithVersion<TestPayload>(
            "test",
            TestContext.CancellationTokenSource.Token);

        Assert.AreEqual("ok", payload?.Value);
        Assert.AreEqual("\"version-1\"", version);
    }

    [TestMethod]
    public async Task PutJsonIfVersionUsesIfMatchAndReportsPreconditionFailure()
    {
        string? ifMatch = null;
        using var handler = new DelegateHandler(request =>
        {
            ifMatch = request.Headers.TryGetValues("If-Match", out var values)
                ? values.Single()
                : null;
            return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
        });
        using var webDav = new TestWebDav(handler);

        var updated = await webDav.PutJsonIfVersion(
            "test",
            new TestPayload("new"),
            "\"version-1\"",
            TestContext.CancellationTokenSource.Token);

        Assert.IsFalse(updated);
        Assert.AreEqual("\"version-1\"", ifMatch);
    }

    private sealed class RecordingHandler(TaskCompletionSource? started = null) : HttpMessageHandler
    {
        public CancellationToken CapturedToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedToken = cancellationToken;
            if (started is not null)
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            };
        }
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(send(request));
        }
    }

    private sealed class TestWebDav(HttpMessageHandler handler) : WebDavBase
    {
        protected override IAppConfig AppConfig { get; } = new TestAppConfig();
        protected override string User => "user";
        protected override string Token => "token";
        protected override string BaseAddress => "http://localhost/";
        protected override bool TrustInsecureCertificate => false;

        protected override HttpClient CreateHttpClient()
        {
            return new HttpClient(handler)
            {
                BaseAddress = new Uri(BaseAddress)
            };
        }
    }

    private sealed class TestAppConfig : IAppConfig
    {
        public string AppId => "test";
        public string AppStringId => "test";
        public string AppVersion => "1.0.0";
        public string UpdateApiUrl => string.Empty;
        public string UpdateUrl => string.Empty;
    }

    private sealed record TestPayload(string Value);
}
