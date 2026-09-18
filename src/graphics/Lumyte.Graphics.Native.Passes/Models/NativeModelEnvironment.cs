using System.Runtime.InteropServices;
using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Native.Passes.Models.EnvironmentGenerated;
using DxRoot = Lumyte.Graphics.Native.Passes.Models.EnvironmentGenerated.DirectX12.Root;
using VkRoot = Lumyte.Graphics.Native.Passes.Models.EnvironmentGenerated.Vulkan.Root;

namespace Lumyte.Graphics.Native.Passes;

internal sealed class NativeModelEnvironment(NativePassServices services) : IDisposable
{
    private readonly Dictionary<(GpuImageUploadData? Image, uint Mode), NativePassContentGeneration<Image>> cache = [];
    private NativeGpuRasterPipelineHandle? pipeline;
    private int sequence;

    internal Image[] Prepare(NativePassBuildContext context, NativePassTexture source, GpuImageUploadData image)
        => [Get(context, source, image, 0, 32, 16), Get(context, source, image, 1, 128, 512), Get(context, source, null, 2, 128, 128)];

    private Image Get(NativePassBuildContext context, NativePassTexture source, GpuImageUploadData? image, uint mode, uint width, uint height)
    {
        var key = (image, mode);
        cache.TryGetValue(key, out var generation);
        if (context.TryUseContent(generation, out Image result)) { return result; }
        generation?.Dispose();
        string name = $"model environment {sequence++}";
        var scope = services.Resources.CreateScope();
        try
        {
            var description = new NativeGpuTextureDescription(NativeGpuTextureDimension.TwoD, width, height, 1, 1, 1, 1,
                GpuFormat.Rgba16Float, NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.ColorAttachment);
            var texture = scope.CreateTexture(description);
            var view = scope.GetView(texture, new GpuTextureViewDescription(NativeGpuTextureViewDimension.TwoD, GpuFormat.Rgba16Float, NativeGpuTextureAspect.Color, 0, 1, 0, 1));
            var target = context.ImportTexture(texture, description, discardContents: true);
            var attachment = context.CreateView(name + " target", target, new(NativeGpuTextureViewDimension.TwoD, GpuFormat.Rgba16Float,
                NativeGpuTextureAspect.Color, 0, 1, 0, 1, GpuTextureViewPurpose.Attachment));
            var sourceView = context.CreateView(name + " source", source, new(NativeGpuTextureViewDimension.TwoD, GpuFormat.Rgba16Float, NativeGpuTextureAspect.Color, 0, 1, 0, 1));
            var state = (attachment, sourceView, Pipeline: Pipeline(), mode, width, height);
            var writer = context.AddPass(name, state, (record, s) =>
            {
                record.Commands.BeginRendering([new(record.GetRenderView(s.attachment), NativeGpuLoadOp.Clear)]);
                record.Commands.SetPipeline(s.Pipeline);
                uint descriptor = record.GetShaderIndex(s.sourceView);
                if (services.Backend.ShaderCodeFormat == GpuShaderCodeFormat.Dxil)
                {
                    DxRoot root = new() { textureIndex = descriptor, mode = s.mode, width = s.width, height = s.height };
                    record.Commands.Draw(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref root, 1)), 3);
                }
                else
                {
                    VkRoot root = new() { textureIndex = descriptor, mode = s.mode, width = s.width, height = s.height };
                    record.Commands.Draw(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref root, 1)), 3);
                }
                record.Commands.EndRendering();
            }).Read(source, new(GpuStage.PixelShader, GpuAccess.ShaderRead)).Write(target, new(GpuStage.ColorOutput, GpuAccess.ColorWrite));
            result = new(texture, view, description);
            generation = context.RegisterContent(result, scope, writer);
            cache[key] = generation;
            _ = context.TryUseContent(generation, out result);
            while (cache.Count > 17)
            {
                var evicted = cache.Keys.First(k => k.Mode != 2 && k != key);
                cache[evicted].Dispose(); cache.Remove(evicted);
            }
            return result;
        }
        catch { scope.Dispose(); throw; }
    }
    private NativeGpuRasterPipelineHandle Pipeline()
    {
        if (pipeline is { } value) { return value; }
        using var program = services.ShaderLoader.Load(ShaderPackage.Create(), services.Backend.ShaderCodeFormat == GpuShaderCodeFormat.Dxil ? DxRoot.AbiHash : VkRoot.AbiHash);
        pipeline = services.Backend.CreateRasterPipeline(new() { ColorTargets = [new(GpuFormat.Rgba16Float)] }, program.Code);
        return pipeline;
    }
    public void Dispose()
    {
        foreach (var item in cache.Values) { item.Dispose(); }
        cache.Clear();
        if (pipeline is { } value) { services.Backend.DestroyRasterPipeline(value); }
    }
    internal sealed record Image(GpuTextureRef Texture, GpuViewRef View, NativeGpuTextureDescription Description);
}
