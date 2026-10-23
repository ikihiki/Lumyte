using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics;

/// <summary>Manages job execution and the graphics device environment selected for resource stages.</summary>
/// <param name="id">The manager-local runtime dependency identifier.</param>
/// <param name="devices">The borrowed stable device service owning physical epochs.</param>
/// <param name="jobs">The shared default runtime dispatching stage work.</param>
public sealed class GraphicsResourceRuntime(string id, IGraphicsDeviceService devices, IResourceRuntime jobs) : IResourceRuntime, IResourceRuntimeDependency
{
    /// <inheritdoc/>
    public event EventHandler? Changed
    {
        add => devices.StateChanged += value;
        remove => devices.StateChanged -= value;
    }

    /// <inheritdoc/>
    public string Id { get; } = id;

    /// <inheritdoc/>
    public object Identity => devices;

    /// <inheritdoc/>
    public ResourceRuntimeSnapshot Snapshot
    {
        get
        {
            GraphicsDeviceState state = devices.State;
            return new(state.Version, state.Status == GraphicsDeviceServiceStatus.Ready, state.InvalidatedThroughVersion);
        }
    }

    /// <inheritdoc/>
    public ValueTask<object> BuildAsync(IResourceStage stage, ResourceBuildContext context, CancellationToken cancellationToken)
        => jobs.BuildAsync(stage, context, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(IResourceStage stage, object resource) => jobs.ReleaseAsync(stage, resource);

    /// <summary>Acquires and records the current device epoch for this build.</summary>
    /// <param name="context">The build using this selected runtime.</param>
    /// <param name="lifetime">Whether the device is retained for construction or for the completed output.</param>
    /// <param name="cancellationToken">Cancels before acquisition.</param>
    /// <returns>A borrowed lease returned by the manager after the corresponding cleanup.</returns>
    public ValueTask<GraphicsDeviceLease> AcquireAsync(ResourceBuildContext context, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(context.Runtime, this))
        {
            throw new InvalidOperationException("Use the graphics runtime selected for this stage.");
        }

        return context.UseGraphicsAsync(devices, Id, lifetime, cancellationToken);
    }

    /// <summary>Waits for safe retirement of an original device epoch before its native children are released.</summary>
    /// <param name="epoch">The epoch retained when the resource was created.</param>
    /// <param name="cancellationToken">Cancels the retirement wait.</param>
    /// <returns>The retirement confirmation.</returns>
    public ValueTask WaitForRetirementAsync(ulong epoch, CancellationToken cancellationToken = default)
        => devices.WaitForRetirementAsync(epoch, cancellationToken);
}
