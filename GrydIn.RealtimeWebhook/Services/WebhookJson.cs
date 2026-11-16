namespace GrydIn.RealtimeWebhook.Services
{
#if NET8_0_OR_GREATER
    using System.Text.Json;
    using System.Text.Json.Serialization;
#else
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
#endif

    /// <summary>Single place for JSON settings: camelCase names, null values omitted.</summary>
    internal static class WebhookJson
    {
#if NET8_0_OR_GREATER
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string Serialize(object value)
        {
            return JsonSerializer.Serialize(value, value.GetType(), Options);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
#else
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Settings);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
#endif
    }
}
