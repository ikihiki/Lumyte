using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Identifies a whole imported or temporary texture without owning its allocation.</summary>
public sealed class RenderGraphTexture : RenderGraphResource
{
    private readonly TextureDesc? _desc;

    internal RenderGraphTexture(RenderGraph owner, IGraphicsTexture texture, TextureState state, BarrierScope scope, string name)
        : base(owner, texture, state, scope, name)
    {
    }

    internal RenderGraphTexture(RenderGraph owner, TextureDesc desc, string name)
        : base(owner, null, TextureState.Undefined, default, name)
    {
        _desc = desc with { };
    }

    internal override string Kind => "Texture";

    internal override bool InitiallyInitialized => InitialState != TextureState.Undefined;

    internal override IDisposable Create(IGraphicDevice device) => device.CreateTexture(_desc!);

    internal override void Validate(TextureState state, BarrierScope scope)
    {
        var texture = Allocation as IGraphicsTexture;
        TextureFormat format = texture?.Format ?? _desc!.Format;
        TextureUsage permittedUsage = texture?.Usage ?? _desc!.Usage;
        bool depth = format is TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8;
        (TextureUsage usage, ResourceAccess access) = state switch
        {
            TextureState.Undefined => ((TextureUsage)0, ResourceAccess.None),
            TextureState.CopySource => (TextureUsage.CopySource, ResourceAccess.CopyRead),
            TextureState.CopyDestination => (TextureUsage.CopyDestination, ResourceAccess.CopyWrite),
            TextureState.Sampled => (TextureUsage.Sampled, ResourceAccess.ShaderRead),
            TextureState.ColorAttachment when !depth => (TextureUsage.RenderAttachment, ResourceAccess.ColorRead | ResourceAccess.ColorWrite),
            TextureState.DepthStencilAttachment when depth => (TextureUsage.RenderAttachment, ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite),
            TextureState.Present when !IsTransient => ((TextureUsage)0, ResourceAccess.None),
            _ => throw new ArgumentException("Invalid texture state or format."),
        };
        if ((permittedUsage & usage) != usage || (scope.Access & ~access) != 0)
        {
            throw new ArgumentException("Texture usage or access conflicts with its state.");
        }
    }

    internal override void Barrier(IGraphicsCommandBuffer commands, object allocation, TextureState beforeState, TextureState afterState, BarrierScope before, BarrierScope after)
        => commands.Barrier(new TextureBarrierDesc
        {
            Texture = (IGraphicsTexture)allocation,
            Range = new TextureSubresourceRange(0, ((IGraphicsTexture)allocation).MipLevels, 0, ((IGraphicsTexture)allocation).ArrayLayers),
            BeforeState = beforeState,
            AfterState = afterState,
            Before = before,
            After = after,
        });
}
