using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace RimTalk.Util
{
    public static class JsonUtil
    {
        public static string SerializeToJson<T>(T obj)
        {
            // Create a memory stream for serialization
            using (var stream = new MemoryStream())
            {
                // Create a DataContractJsonSerializer
                var serializer = new DataContractJsonSerializer(typeof(T));

                // Serialize the ApiRequest object
                serializer.WriteObject(stream, obj);

                // Convert the memory stream to a string
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static T DeserializeFromJson<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                // Create an instance of DataContractJsonSerializer
                var serializer = new DataContractJsonSerializer(typeof(T));

                // Deserialize the JSON data
                return (T)serializer.ReadObject(stream);
            }
        }
        
        public static string Sanitize(string text)
        {
            text = text.Replace("```json", "").Replace("```", "").Trim();
            text = Regex.Replace(text, @"[“”""]+", "\"");
            text = Regex.Replace(text, @"\n\s*|\\", "");
            text = Regex.Replace(text, @".*[\r\n]*\[{", "[{");
            text = Regex.Replace(text, @"\}][\r\n]*.*", "}]");
            return text;
        }
    }
}