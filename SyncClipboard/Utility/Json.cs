using System.Web.Script.Serialization;

namespace SyncClipboard.Utility
{
    public static class Json
    {
        public static T Decode<T>(string json)
        {
            T firstResponse = default(T);
            try
            {
                firstResponse = new JavaScriptSerializer().Deserialize<T>(json);
            }
            catch
            {
                firstResponse = default(T);
            }
            return firstResponse;
        }

        public static string Encode<T>(T jsonClass)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            return serializer.Serialize(jsonClass);
        }
    }
}