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
        List<CopyRegion> regions = [];
        for (uint mip = 0; mip < description.MipCount; mip++)
        {
            foreach (NativeGpuTextureAspect aspect in aspects)
            {
                uint width = Math.Max(1, description.Width >> (int)mip), height = Math.Max(1, description.Height >> (int)mip);
                uint depth = Math.Max(1, description.Depth >> (int)mip);
                uint bytes = aspect == NativeGpuTextureAspect.Stencil ? 1u : description.Format switch
                { GpuFormat.R8Unorm => 1, GpuFormat.Rg8Unorm => 2, GpuFormat.Rgba16Float => 8, _ => 4 };
                ulong rowPitch = checked((width * bytes + 255ul) / 256 * 256), imagePitch = checked(rowPitch * height);
                NativePassBuffer scratch = context.CreateBuffer($"texture-copy/{mip}/{aspect}", new(checked(imagePitch * Math.Max(depth, description.LayerCount)), Alignment: 512));
                NativeGpuTextureCopyFootprint footprint = new(mip, aspect, 0, description.LayerCount, default,
                    new(width, height, depth), rowPitch, imagePitch);
                regions.Add(new(scratch, footprint));
            }
        }
        CopyRegion[] snapshot = regions.ToArray();
        var read = context.AddPass("copy-to-memory", (Source: source, Regions: snapshot), static (record, state) =>
        {
            foreach (var region in state.Regions)
            { record.Commands.CopyTextureToMemory(record.GetTexture(state.Source), record.GetBufferRange(region.Buffer), region.Footprint); }
        }).Read(source, new(GpuStage.Copy, GpuAccess.CopyRead));
        var write = context.AddPass("copy-to-texture", (Target: target, Regions: snapshot), static (record, state) =>
        {
            foreach (var region in state.Regions)
            { record.Commands.CopyMemoryToTexture(record.GetBufferRange(region.Buffer), record.GetTexture(state.Target), region.Footprint); }
        }).Write(target, new(GpuStage.Copy, GpuAccess.CopyWrite));
        foreach (var region in snapshot)
        {
            read.Write(region.Buffer, new(GpuStage.Copy, GpuAccess.CopyWrite));
            write.Read(region.Buffer, new(GpuStage.Copy, GpuAccess.CopyRead));
        }
        return ValueTask.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    private readonly record struct CopyRegion(NativePassBuffer Buffer, NativeGpuTextureCopyFootprint Footprint);
}
