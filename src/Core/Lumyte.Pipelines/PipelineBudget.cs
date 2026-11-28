namespace Lumyte.Pipelines;

/// <summary>Explicit finite compilation and per-run admission limits.</summary>
public sealed record PipelineBudget
{
    /// <summary>Initializes a new instance of the <see cref="PipelineBudget"/> class.</summary>
    /// <param name="maxNodes">The positive declaration node limit.</param>
    /// <param name="maxHostBytes">The nonnegative peak host byte limit.</param>
    /// <param name="maxInFlight">The positive retained run limit.</param>
    public PipelineBudget(int maxNodes, long maxHostBytes, int maxInFlight)
        : this(maxNodes, maxHostBytes, maxInFlight, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PipelineBudget"/> class with an application input extent ceiling.</summary>
    /// <param name="maxNodes">The positive declaration node limit.</param>
    /// <param name="maxHostBytes">The nonnegative peak host byte limit.</param>
    /// <param name="maxInFlight">The positive retained run limit.</param>
    /// <param name="maxInputElements">The optional nonnegative application input element ceiling, required for schema-free immutable input metadata.</param>
    public PipelineBudget(int maxNodes, long maxHostBytes, int maxInFlight, long? maxInputElements)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNodes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxHostBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInFlight);
        if (maxInputElements is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInputElements));
        }

        MaxInputElements = maxInputElements;
        MaxNodes = maxNodes;
        MaxHostBytes = maxHostBytes;
        MaxInFlight = maxInFlight;
    }

    /// <summary>Gets the optional schema-free application input element ceiling, checked before stage invocation.</summary>
    public long? MaxInputElements { get; }

    /// <summary>Gets the maximum number of nodes.</summary>
    public int MaxNodes { get; }

    /// <summary>Gets the maximum retained host bytes per run.</summary>
    public long MaxHostBytes { get; }

    /// <summary>Gets the maximum number of retained runs.</summary>
    public int MaxInFlight { get; }
}
