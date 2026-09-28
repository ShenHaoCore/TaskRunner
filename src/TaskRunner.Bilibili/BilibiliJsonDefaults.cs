using System.Text.Json;

namespace TaskRunner.Bilibili;

internal static class BilibiliJsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
