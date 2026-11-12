using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

internal sealed class RenderGraphSampler : RenderGraphResource
{
    private readonly SamplerDesc? _desc;

    internal RenderGraphSampler(RenderGraph owner, IGraphicsSampler sampler)
        : base(owner, sampler, TextureState.Undefined, default, "sampler")
    {
        Key = sampler;
    }

    internal RenderGraphSampler(RenderGraph owner, SamplerDesc desc)
        : base(owner, null, TextureState.Undefined, default, "sampler")
    {
        Key = desc;
        _desc = desc;
    }

    internal object Key { get; }

    internal override string Kind => "Sampler";

    internal override bool InitiallyInitialized => true;

    internal override IDisposable Create(IGraphicDevice device) => device.CreateSampler(_desc!);

    internal override void Validate(TextureState state, BarrierScope scope)
    {
        if (scope.Access != ResourceAccess.ShaderRead)
        {
            throw new ArgumentException("Samplers are immutable shader inputs.");
        }
    }

    internal override void Barrier(IGraphicsCommandBuffer commands, object allocation, TextureState beforeState, TextureState afterState, BarrierScope before, BarrierScope after)
    {
    }
}
