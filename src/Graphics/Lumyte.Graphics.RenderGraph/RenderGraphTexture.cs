using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Identifies an imported texture without owning its allocation or views.</summary>
public sealed class RenderGraphTexture : RenderGraphResource
{
    internal RenderGraphTexture(RenderGraph owner, IGraphicsTexture texture, TextureState state, BarrierScope scope)
        : base(owner, texture, state, scope)
    {
    }

    internal IGraphicsTexture Texture => (IGraphicsTexture)Allocation;

    internal override void Validate(TextureState state, BarrierScope scope)
    {
        bool depth = Texture.Format is TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8;
        (TextureUsage usage, ResourceAccess access) = state switch
        {
            TextureState.Undefined => ((TextureUsage)0, ResourceAccess.None),
            TextureState.CopySource => (TextureUsage.CopySource, ResourceAccess.CopyRead),
            TextureState.CopyDestination => (TextureUsage.CopyDestination, ResourceAccess.CopyWrite),
            TextureState.Sampled => (TextureUsage.Sampled, ResourceAccess.ShaderRead),
            TextureState.ColorAttachment when !depth => (TextureUsage.RenderAttachment, ResourceAccess.ColorRead | ResourceAccess.ColorWrite),
            TextureState.DepthStencilAttachment when depth => (TextureUsage.RenderAttachment, ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite),
            _ => throw new ArgumentException("Invalid texture state or format."),
        };
        if ((Texture.Usage & usage) != usage || (scope.Access & ~access) != 0)
        {
            throw new ArgumentException("Texture usage or access conflicts with its state.");
        }
    }

    internal override void Barrier(IGraphicsCommandBuffer commands, TextureState beforeState, TextureState afterState, BarrierScope before, BarrierScope after)
        => commands.Barrier(new TextureBarrierDesc
        {
            Texture = Texture,
            Range = new TextureSubresourceRange(0, Texture.MipLevels, 0, Texture.ArrayLayers),
            BeforeState = beforeState,
            AfterState = afterState,
            Before = before,
            After = after,
        });
}
