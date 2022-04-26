using System;
using System.Net.Http;
using System.Text;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
#nullable enable

namespace SyncClipboard.Utility.Web
{
    public static class Http
    {
        public const string USER_AGENT = "SyncClipboard " + Env.VERSION;
        private static readonly Lazy<HttpClient> lazyHttpClient = new(
            () => {
                HttpClient client = new(
                    new SocketsHttpHandler()
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(60),
                    });
                client.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
                return client;
            }
        );
        public static HttpClient HttpClient => lazyHttpClient.Value;

        public static async Task<Type?> PostTextRecieveJson<Type>(string url,
            IEnumerable<KeyValuePair<string, string>>? list = null)
        {
            list ??= Array.Empty<KeyValuePair<string, string>>();
            var res = await HttpClient.PostAsync(url, new FormUrlEncodedContent(list));
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<Type>();
            //new JsonSerializerOptions(JsonSerializerDefaults.Web)
        }
    }
}