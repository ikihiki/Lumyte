using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Preserves canonical device element types for explicit readback without reflection.</summary>
internal interface IDeviceStorage
{
    /// <summary>Validates native usage before the consuming processor can allocate outputs.</summary>
    /// <param name="device">The exact native device.</param>
    /// <param name="access">The complete admitted access union.</param>
    void Validate(Lumyte.Graphics.Abstractions.IGraphicDevice device, Lumyte.Graphics.Abstractions.ResourceAccess access);

    /// <summary>Prepares a native copy and waits for explicit execution-host mapping.</summary>
    /// <typeparam name="TSchema">The canonical output schema.</typeparam>
    /// <param name="context">The admitted transfer scope.</param>
    /// <param name="extent">The captured canonical extent.</param>
    /// <param name="cancellationToken">Cooperative CPU continuation cancellation.</param>
    /// <returns>The mapped and independently copied canonical host placement.</returns>
    ValueTask<ValueLease<Host<TSchema>>> DownloadAsync<TSchema>(GraphicsStagePreparationContext context, SchemaExtent extent, CancellationToken cancellationToken)
        where TSchema : ISchema;
}
