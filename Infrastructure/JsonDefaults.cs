using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace HorseVillage.Infrastructure;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Apply(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Apply(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
        // Emit Arabic text as-is instead of \uXXXX escapes.
        options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        return options;
    }
}
