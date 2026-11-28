using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares plan buffer.</summary>
/// <typeparam name = "T">The typed shader or resource contract.</typeparam>
public sealed class PlanBuffer<T> : PlanResource, IBufferVersionFactory
    where T : unmanaged
{
    internal PlanBuffer(GraphicsPlanBuilder owner, ResourceIdentity identity, int version, PlanRange range, IReadOnlyList<PlanRange> initialized)
        : base(owner, identity, version, range, initialized)
    {
    }

    /// <inheritdoc/>
    PlanResource IBufferVersionFactory.Next(int version, IReadOnlyList<PlanRange> valid) => new PlanBuffer<T>(Owner, Identity, version, Range, valid);

    internal override PreparedPlanBarrier PrepareBarrier(PreparedResources resources, PlanRange range, BarrierScope before, BarrierScope after, TextureState beforeState, TextureState afterState) => new PreparedBufferBarrier<T>(new() { Buffer = resources.Buffer(this, range), Before = before, After = after });

    internal override ResourceAccess PrecedingPhysicalWrites(PreparedResources resources)
    {
        var buffer = (IGraphicsBuffer<T>)resources.Allocations[Identity];
        ResourceAccess access = ResourceAccess.None;
        if ((buffer.Usage & BufferUsage.CopyDestination) != 0)
        {
            access |= ResourceAccess.CopyWrite;
        }

        if ((buffer.Usage & BufferUsage.ShaderWrite) != 0)
        {
            access |= ResourceAccess.ShaderWrite;
        }

        if (buffer.Memory == MemoryPreference.Upload)
        {
            access |= ResourceAccess.HostWrite;
        }

        return access;
    }

    internal override PlanResource NextVersion(int version, IReadOnlyList<PlanRange> initialized, bool preserve)
        => new PlanBuffer<T>(Owner, Identity, version, Range, initialized);
}
