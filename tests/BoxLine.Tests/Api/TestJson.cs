using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoxLine.Tests.Api;

/// <summary>JSON settings matching the API (web defaults, enums as strings).</summary>
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
