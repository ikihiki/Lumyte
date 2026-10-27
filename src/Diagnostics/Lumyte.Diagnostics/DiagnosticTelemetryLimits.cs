namespace Lumyte.Diagnostics;

/// <summary>Shared collection and transport bounds for diagnostic event fields.</summary>
public static class DiagnosticTelemetryLimits
{
    /// <summary>The maximum number of application fields visited while collecting one event.</summary>
    public const int MaximumUserFields = 32;

    /// <summary>The maximum field count including log level, event ID and three exception fields.</summary>
    public const int MaximumEventFields = MaximumUserFields + 5;
}
