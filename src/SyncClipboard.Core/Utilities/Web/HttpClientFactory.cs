namespace SyncClipboard.Core.Utilities.Web;

public class HttpClientFactory
{
    public bool TrustInsecureCertificate { get; set; } = false;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    public HttpClient CreateClient()
    {
        var httpclientHandler = new HttpClientHandler();
        if (TrustInsecureCertificate)
        {
            httpclientHandler.ServerCertificateCustomValidationCallback = delegate { return true; };
        }

        return new HttpClient(httpclientHandler)
        {
            Timeout = Timeout
        };
    }
}
