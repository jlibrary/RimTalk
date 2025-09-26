using System;
using System.IO;
using JsonRepairSharp;
using Newtonsoft.Json;
using System.Runtime.Serialization.Json;
using System.Text;

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
            string sanitizedAndRepairedJson = SanitizeAndRepair(json);

            try
            {
                // Configure Newtonsoft.Json to be more tolerant of certain issues
                var settings = new JsonSerializerSettings
                {
                    Error = (sender, args) =>
                    {
                        // Log the error but mark it as handled to continue parsing if possible
                        Logger.Warning($"JSON deserialization error: {args.ErrorContext.Error.Message}");
                        args.ErrorContext.Handled = true;
                    }
                };

                return JsonConvert.DeserializeObject<T>(sanitizedAndRepairedJson, settings);
            }
            catch (JsonException ex)
            {
                Logger.Error($"Json deserialization failed for {typeof(T).Name} after repair. Error: {ex.Message}\nRepaired JSON:\n{sanitizedAndRepairedJson}");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error($"An unexpected error occurred during deserialization for {typeof(T).Name}. Error: {ex.Message}\nOriginal JSON:\n{json}");
                throw;
            }
        }

        public static string SanitizeAndRepair(string text)
        {
            // Initial rough cleaning
            text = text.Replace("```json", "").Replace("```", "").Trim();

            // Attempt to find the start and end of the JSON object/array
            int startIndex = text.IndexOfAny(new char[] { '{', '[' });
            int endIndex = text.LastIndexOfAny(new char[] { '}', ']' });

            if (startIndex >= 0 && endIndex > startIndex)
            {
                text = text.Substring(startIndex, endIndex - startIndex + 1);
            }

            // Use JsonRepairSharp to fix common structural issues
            try
            {
                text = JsonRepair.RepairJson(text);
            }
            catch (Exception ex)
            {
                Logger.Warning($"JsonRepairSharp failed: {ex.Message}. Proceeding with the roughly sanitized string.");
            }

            return text;
        }
    }
}