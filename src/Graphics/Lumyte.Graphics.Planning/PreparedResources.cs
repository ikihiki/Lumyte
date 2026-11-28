using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed partial class PreparedResources
{
    internal List<PreparedPlanBarrier> FinalBarriers { get; } = [];

    internal List<PhysicalBindingUse> PhysicalBindings { get; } = [];

    internal int OperationCount { get; set; }

    internal Dictionary<ResourceIdentity, object> Allocations { get; } = [];

    internal Dictionary<PlanTexture, IGraphicsTextureView> Views { get; } = [];

    internal BufferSlice<T> Buffer<T>(PlanBuffer<T> buffer, PlanRange range)
        where T : unmanaged => new((IGraphicsBuffer<T>)Allocations[buffer.Identity], range.Offset, range.Count);

    internal IGraphicsTextureView Texture(PlanTexture texture) => Views[texture];

    internal ShaderDataSlice<T> ShaderData<T>(PlanShaderData<T> buffer, PlanRange range)
        where T : struct, IShaderArguments
        => new((IGraphicsShaderDataBuffer<T>)Allocations[buffer.Identity], range.Offset, range.Count);
}
