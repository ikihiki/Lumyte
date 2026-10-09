namespace Lumyte.Settings;

/// <summary>Standard telemetry identities and optional game correlation for settings operations.</summary>
public static class SettingsTelemetry
{
    /// <summary>The settings meter name.</summary>
    public const string MeterName = "Lumyte.Settings";

    /// <summary>The settings activity source name.</summary>
    public const string ActivitySourceName = "Lumyte.Settings";

    /// <summary>The settings logger category.</summary>
    public const string LogCategoryName = "Lumyte.Settings";

    private static readonly AsyncLocal<Guid?> _instance = new();

    internal static Guid? InstanceId => _instance.Value;

    /// <summary>Correlates settings telemetry with a game until the returned scope is disposed.</summary>
    /// <param name="instanceId">The game instance ID.</param>
    /// <returns>A scope restoring the previous correlation across asynchronous calls.</returns>
    public static IDisposable BeginScope(Guid instanceId)
    {
        var scope = new Scope(_instance.Value);
        _instance.Value = instanceId;
        return scope;
    }

    private sealed class Scope(Guid? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _instance.Value = previous;
            }
        }
    }
}
