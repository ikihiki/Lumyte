using System.Runtime.InteropServices;
using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.Native.Shaders;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Native.Passes.ImageProcessing.Generated;
using DxRoot = Lumyte.Graphics.Native.Passes.ImageProcessing.Generated.DirectX12.Root;
using VkRoot = Lumyte.Graphics.Native.Passes.ImageProcessing.Generated.Vulkan.Root;

namespace Lumyte.Graphics.Native.Passes;

public sealed class NativeOutputPass(NativePassServices services) : INativeRenderPass<OutputPassRequest, TexturePassResult>
{
    private readonly Dictionary<(GpuFormat Format, uint Samples), NativeGpuRasterPipelineHandle> pipelines = [];
    public ValueTask BuildAsync(NativePassBuildContext context, OutputPassRequest request, TexturePassResult result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativePassTexture source = context.ImportTexture(request.Source), target = context.ImportTexture(request.Target);
        var key = (target.Description.Format, target.Description.SampleCount);
        if (!pipelines.TryGetValue(key, out NativeGpuRasterPipelineHandle? pipeline))
        {
            using NativeShaderProgram program = services.ShaderLoader.Load(ShaderPackage.Create(),
                services.Backend.ShaderCodeFormat == GpuShaderCodeFormat.Dxil ? DxRoot.AbiHash : VkRoot.AbiHash);
            pipeline = services.Backend.CreateRasterPipeline(new()
            { ColorTargets = [new(key.Format)], SampleCount = key.SampleCount }, program.Code);
            try { pipelines.Add(key, pipeline); }
            catch { services.Backend.DestroyRasterPipeline(pipeline); throw; }
        }
        NativePassView sampled = context.CreateView("output-source", source,
            new(NativeGpuTextureViewDimension.TwoD, source.Description.Format, NativeGpuTextureAspect.Color, 0, 1, 0, 1));
        NativePassView attachment = context.CreateView("output-target", target,
            new(NativeGpuTextureViewDimension.TwoD, target.Description.Format, NativeGpuTextureAspect.Color, 0, 1, 0, 1, GpuTextureViewPurpose.Attachment));
        bool hardwareSrgb = target.Description.Format is GpuFormat.Rgba8UnormSrgb or GpuFormat.Bgra8UnormSrgb;
        context.AddPass("output", new State(sampled, attachment, pipeline, request.Encoding, request.AlphaMode, hardwareSrgb,
            services.Backend.ShaderCodeFormat), static (record, state) =>
        {
            record.Commands.BeginRendering([new(record.GetRenderView(state.Target), NativeGpuLoadOp.Discard)]);
            record.Commands.SetPipeline(state.Pipeline);
            if (state.CodeFormat == GpuShaderCodeFormat.Dxil)
            {
                DxRoot root = new() { textureIndex = record.GetShaderIndex(state.Source), encodeSrgb = state.Encoding == OutputEncoding.Srgb ? 1u : 0,
                    premultiplied = state.AlphaMode == OutputAlphaMode.Premultiplied ? 1u : 0, hardwareSrgb = state.HardwareSrgb ? 1u : 0 };
                record.Commands.Draw(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref root, 1)), 3);
            }
            else
            {
                VkRoot root = new() { textureIndex = record.GetShaderIndex(state.Source), encodeSrgb = state.Encoding == OutputEncoding.Srgb ? 1u : 0,
                    premultiplied = state.AlphaMode == OutputAlphaMode.Premultiplied ? 1u : 0, hardwareSrgb = state.HardwareSrgb ? 1u : 0 };
                record.Commands.Draw(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref root, 1)), 3);
            }
            record.Commands.EndRendering();
        }).Read(source, new(GpuStage.PixelShader, GpuAccess.ShaderRead))
            .Write(target, new(GpuStage.ColorOutput, GpuAccess.ColorWrite));
        return ValueTask.CompletedTask;
    }
    public ValueTask DisposeAsync()
    {
        foreach (NativeGpuRasterPipelineHandle pipeline in pipelines.Values) { services.Backend.DestroyRasterPipeline(pipeline); }
        pipelines.Clear(); return ValueTask.CompletedTask;
    }
    private sealed record State(NativePassView Source, NativePassView Target, NativeGpuRasterPipelineHandle Pipeline,
        OutputEncoding Encoding, OutputAlphaMode AlphaMode, bool HardwareSrgb, GpuShaderCodeFormat CodeFormat);
}
