using System.Text.Json;

namespace SyncClipboard.Utility
{
    public static class Json
    {
        public static T Decode<T>(string json)
        {
            T firstResponse;
            try
            {
                firstResponse = JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                firstResponse = default;
            }
            return firstResponse;
        }

        public static string Encode<T>(T jsonClass)
        {
            
            return JsonSerializer.Serialize(jsonClass);
        }
    }
}