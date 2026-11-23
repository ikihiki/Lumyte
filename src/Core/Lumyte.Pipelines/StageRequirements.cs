namespace Lumyte.Pipelines;

/// <summary>Captures finite host memory and explicitly declared extension requirements.</summary>
public sealed record StageRequirements
{
    private long? _outputMaxElements;

    /// <summary>Initializes a new instance of the <see cref="StageRequirements"/> class.</summary>
    /// <param name="maxHostBytes">The nonnegative host allocation ceiling.</param>
    /// <param name="extensions">The immutable extension requirements.</param>
    public StageRequirements(long maxHostBytes, IReadOnlyList<IStageExtensionRequirement> extensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxHostBytes);
        ArgumentNullException.ThrowIfNull(extensions);
        if (extensions.Any(extension => extension is null))
        {
            throw new ArgumentException("Requirements cannot contain null.", nameof(extensions));
        }

        MaxHostBytes = maxHostBytes;
        Extensions = Array.AsReadOnly(extensions.ToArray());
    }

    /// <summary>Gets the per-invocation host ceiling.</summary>
    public long MaxHostBytes { get; }

    /// <summary>Gets the captured extension requirements.</summary>
    public IReadOnlyList<IStageExtensionRequirement> Extensions { get; }

    /// <summary>Gets the optional finite canonical output element ceiling used for downstream build descriptions.</summary>
    public long? OutputMaxElements
    {
        get => _outputMaxElements;
        init
        {
            if (value is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _outputMaxElements = value;
        }
    }

    internal Type? HostInputSchema { get; init; }

    internal long? MaxHostInputElements { get; init; }
}
