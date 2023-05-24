namespace SyncClipboard.Server
{
    public interface IUserToken
    {
        public string UserName { get; set; }
        public string Password { get; set; }
        public void SetUserToken(string name, string pass);
    }

    public class UserToken : IUserToken
    {
        public string UserName { get; set; } = "admin";
        public string Password { get; set; } = "admin";
        public void SetUserToken(string name, string pass)
        {
            UserName = name;
            Password = pass;
        }
    }
}