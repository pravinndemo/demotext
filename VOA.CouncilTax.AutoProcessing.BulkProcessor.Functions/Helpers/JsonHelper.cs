using System.Text.Json;

namespace VOA.CouncilTax.AutoProcessing.Helpers;

internal static class JsonHelper
{
    public static string Serialize<T>(T sourceObject)
    {
        return JsonSerializer.Serialize(sourceObject);
    }

    public static T? Deserialize<T>(string jsonStringObject)
    {
        return JsonSerializer.Deserialize<T>(jsonStringObject);
    }
}
