using System.Diagnostics;

namespace OsoujiSystem.Application.Observability;

public static class ApplicationTelemetry
{
    public const string ActivitySourceName = "OsoujiSystem.Application";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
