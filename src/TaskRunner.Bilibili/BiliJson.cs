using System.Text.Json;

namespace TaskRunner.Bilibili;

internal static class BiliJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
