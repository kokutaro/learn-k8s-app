namespace OsoujiSystem.Application.Abstractions;

internal static class NotFoundErrors
{
    public static ApplicationResult<T> Create<T>(string resource, string key, string value)
    {
        return ApplicationResult<T>.Failure(
            "NotFound",
            $"{resource} was not found.",
            new Dictionary<string, object?>
            {
                ["resource"] = resource,
                ["key"] = key,
                ["value"] = value
            });
    }
}
