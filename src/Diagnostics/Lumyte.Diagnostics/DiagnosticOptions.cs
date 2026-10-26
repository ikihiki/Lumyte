using Microsoft.Extensions.Logging;

namespace Lumyte.Diagnostics;

/// <summary>Bounded diagnostic collection configuration.</summary>
public sealed class DiagnosticOptions
{
    /// <summary>Gets or sets a value indicating whether collection is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the allowed meter names.</summary>
    public IReadOnlyList<string> AllowedMeterNames { get; set; } = [];

    /// <summary>Gets or sets the allowed activity source names.</summary>
    public IReadOnlyList<string> AllowedActivitySourceNames { get; set; } = [];

    /// <summary>Gets or sets the allowed log category prefixes.</summary>
    public IReadOnlyList<string> AllowedLogCategoryPrefixes { get; set; } = [];

    /// <summary>Gets or sets the minimum log level.</summary>
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;

    /// <summary>Gets or sets the root span sampling ratio.</summary>
    public double TraceSampleRatio { get; set; } = 0.1;

    /// <summary>Gets or sets the event queue capacity.</summary>
    public int QueueCapacity { get; set; } = 1024;
}
