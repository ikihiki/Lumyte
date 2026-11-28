using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares plan texture.</summary>
public sealed class PlanTexture : PlanResource
{
    internal PlanTexture(GraphicsPlanBuilder owner, ResourceIdentity identity, int version, PlanRange range, IReadOnlyList<PlanRange> initialized)
        : base(owner, identity, version, range, initialized)
    {
    }

    internal TextureViewDesc? ViewDescription { get; init; }

    internal TextureViewDesc NativeViewDescription => ViewDescription ?? new()
    {
        Dimension = Range.Count > Identity.MipLevels ? TextureViewDimension.D2Array : TextureViewDimension.D2,
        BaseMipLevel = checked((uint)(Range.Offset % Identity.MipLevels)),
        MipLevelCount = checked((uint)Math.Min(Range.Count, Identity.MipLevels)),
        BaseArrayLayer = checked((uint)(Range.Offset / Identity.MipLevels)),
        ArrayLayerCount = checked((uint)Math.Max(1ul, Range.Count / Identity.MipLevels)),
    };

    internal override PreparedPlanBarrier PrepareBarrier(PreparedResources resources, PlanRange range, BarrierScope before, BarrierScope after, TextureState beforeState, TextureState afterState)
    {
        uint mips = Identity.MipLevels;
        if (range.Count != 1)
        {
            throw new InvalidOperationException("Texture barriers are prepared per subresource.");
        }

        return new PreparedTextureBarrier(new() { Texture = (IGraphicsTexture)resources.Allocations[Identity], Range = new((uint)(range.Offset % mips), 1, (uint)(range.Offset / mips), 1), Before = before, After = after, BeforeState = beforeState, AfterState = afterState });
    }

    internal override PlanResource NextVersion(int version, IReadOnlyList<PlanRange> initialized, bool preserve)
        => new PlanTexture(Owner, Identity, version, Range, initialized) { ViewDescription = ViewDescription };
}
