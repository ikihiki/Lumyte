using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.Native.Passes;

public sealed class NativeClearPass : INativeRenderPass<ClearPassRequest, TexturePassResult>
{
    public ValueTask BuildAsync(NativePassBuildContext context, ClearPassRequest request, TexturePassResult result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativePassTexture texture = context.ImportTexture(request.Target);
        TextureClearValue value = context.GetInput(request.Value);
        NativeGpuTextureDescription description = texture.Description;
        List<NativePassView> views = [];
        NativeGpuTextureAspect aspect = description.Format switch
        {
            GpuFormat.D32Float => NativeGpuTextureAspect.Depth,
            GpuFormat.Depth24PlusStencil8 => NativeGpuTextureAspect.Depth | NativeGpuTextureAspect.Stencil,
            _ => NativeGpuTextureAspect.Color,
        };
        for (uint mip = 0; mip < description.MipCount; mip++)
        {
            uint layers = description.Dimension == NativeGpuTextureDimension.ThreeD ? Math.Max(1, description.Depth >> (int)mip) : description.LayerCount;
            for (uint layer = 0; layer < layers; layer++)
            {
                NativeGpuTextureViewDimension dimension = description.Dimension == NativeGpuTextureDimension.OneD
                    ? NativeGpuTextureViewDimension.OneD : NativeGpuTextureViewDimension.TwoD;
                views.Add(context.CreateView($"clear-target/{mip}/{layer}", texture,
                    new(dimension, description.Format, aspect, mip, 1, layer, 1, GpuTextureViewPurpose.Attachment)));
            }
        }
        context.AddPass("clear", (Views: views.ToArray(), Value: value, Aspect: aspect), static (record, state) =>
        {
            foreach (NativePassView view in state.Views)
            {
                NativeGpuRenderViewHandle target = record.GetRenderView(view);
                if (state.Value.IsDepthStencil)
                {
                    bool stencil = (state.Aspect & NativeGpuTextureAspect.Stencil) != 0;
                    record.Commands.BeginRendering([], new(target, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store,
                        stencil ? NativeGpuLoadOp.Clear : null, stencil ? NativeGpuStoreOp.Store : null,
                        state.Value.Depth, checked((byte)state.Value.Stencil)));
                }
                else
                {
                    var color = state.Value.ColorValue;
                    record.Commands.BeginRendering([new(target, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store,
                        new(color.X * color.W, color.Y * color.W, color.Z * color.W, color.W))]);
                }
                record.Commands.EndRendering();
            }
        }).Write(texture, value.IsDepthStencil ? new(GpuStage.DepthStencil, GpuAccess.DepthStencilWrite) : new(GpuStage.ColorOutput, GpuAccess.ColorWrite));
        return ValueTask.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
