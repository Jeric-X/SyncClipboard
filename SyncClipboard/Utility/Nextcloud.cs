using System.Threading.Tasks;
using SyncClipboard.Control;

namespace SyncClipboard.Utility
{
    static class Nextcloud
    {
        private const string VERIFICATION_URL = "/index.php/login/v2";
        public static void SignIn()
        {
            var server = InputBox.Show("Please input Nextcloud server address");
        }

        private static async Task VerifyNextcloud(string server)
        {
        }
    }
}
