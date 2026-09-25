namespace TaskRunner.Bilibili;

public static class BiliNameMask
{
    public static string Mask(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "(unknown)";
        }

        if (name.Length <= 2)
        {
            return name[0] + "*";
        }

        return $"{name[0]}***{name[^1]}";
    }
}
