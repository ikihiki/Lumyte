using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.Native.Passes;

public sealed class NativeTextureCopyPass : INativeRenderPass<TextureCopyPassRequest, TexturePassResult>
{
    public ValueTask BuildAsync(NativePassBuildContext context, TextureCopyPassRequest request, TexturePassResult result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativePassTexture source = context.ImportTexture(request.Source), target = context.ImportTexture(request.Target);
        NativeGpuTextureDescription description = source.Description;
        NativeGpuTextureAspect[] aspects = description.Format switch
        {
            GpuFormat.D32Float => [NativeGpuTextureAspect.Depth],
            GpuFormat.Depth24PlusStencil8 => [NativeGpuTextureAspect.Depth, NativeGpuTextureAspect.Stencil],
            _ => [NativeGpuTextureAspect.Color],
        };
        for (uint mip = 0; mip < description.MipCount; mip++)
        {
            foreach (NativeGpuTextureAspect aspect in aspects)
            {
                uint width = Math.Max(1, description.Width >> (int)mip), height = Math.Max(1, description.Height >> (int)mip);
                uint depth = Math.Max(1, description.Depth >> (int)mip);
                uint bytes = aspect == NativeGpuTextureAspect.Stencil ? 1u : description.Format switch
                { GpuFormat.R8Unorm => 1, GpuFormat.Rg8Unorm => 2, _ => 4 };
                ulong rowPitch = checked((width * bytes + 255ul) / 256 * 256), imagePitch = checked(rowPitch * height);
                NativePassBuffer scratch = context.CreateBuffer("texture-copy", new(checked(imagePitch * Math.Max(depth, description.LayerCount)), Alignment: 512));
                NativeGpuTextureCopyFootprint footprint = new(mip, aspect, 0, description.LayerCount, default,
                    new(width, height, depth), rowPitch, imagePitch);
                context.AddPass("copy-texture", (Source: source, Target: target, Scratch: scratch, Footprint: footprint), static (record, state) =>
                {
                    NativeGpuRange range = record.GetBufferRange(state.Scratch);
                    record.Commands.CopyTextureToMemory(record.GetTexture(state.Source), range, state.Footprint);
                    record.Commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Copy, GpuAccess.CopyRead);
                    record.Commands.CopyMemoryToTexture(range, record.GetTexture(state.Target), state.Footprint);
                }).Read(source, new(GpuStage.Copy, GpuAccess.CopyRead))
                    .Write(target, new(GpuStage.Copy, GpuAccess.CopyWrite))
                    .ReadWrite(scratch, new(GpuStage.Copy, GpuAccess.CopyRead | GpuAccess.CopyWrite));
            }
        }
        return ValueTask.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
