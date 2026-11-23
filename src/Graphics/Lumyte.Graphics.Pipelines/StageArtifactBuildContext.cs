using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Captures a build's borrowed device and exact generation.</summary>
public sealed class StageArtifactBuildContext
{
    private readonly Action<IDisposable> _retainFailedCleanup;

    internal StageArtifactBuildContext(IGraphicDevice device, GraphicsDeviceGeneration generation, Action<IDisposable> retainFailedCleanup)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(retainFailedCleanup);
        generation.ValidateDevice(device);
        _retainFailedCleanup = retainFailedCleanup;
        Device = device;
        Generation = generation;
    }

    /// <summary>Gets the borrowed device.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the fixed generation.</summary>
    public GraphicsDeviceGeneration Generation { get; }

    /// <summary>Cleans unpublished native owners, retaining failed cleanup in this build host for explicit disposal retry.</summary>
    /// <param name="owner">The acquisition-local owner, transferred to the build only if cleanup fails.</param>
    /// <param name="primaryFailure">The original acquisition exception, preserved with any cleanup failure.</param>
    public void CleanupAfterFailure(IDisposable owner, Exception primaryFailure)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(primaryFailure);
        try
        {
            owner.Dispose();
        }
        catch (Exception cleanup)
        {
            _retainFailedCleanup(owner);
            throw new AggregateException(primaryFailure, cleanup);
        }
    }
}
