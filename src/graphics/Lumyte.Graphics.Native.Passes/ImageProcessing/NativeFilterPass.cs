using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.Native.Shaders;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Native.Passes.ImageProcessing.FilterGenerated;
using DxRoot = Lumyte.Graphics.Native.Passes.ImageProcessing.FilterGenerated.DirectX12.Root;
using VkRoot = Lumyte.Graphics.Native.Passes.ImageProcessing.FilterGenerated.Vulkan.Root;

namespace Lumyte.Graphics.Native.Passes;

internal sealed class NativeFilterPass(NativePassServices services) :
    INativeRenderPass<BlitPassRequest, TexturePassResult>, INativeRenderPass<BlurPassRequest, BlurPassResult>,
    INativeRenderPass<CompositePassRequest, TexturePassResult>, INativeRenderPass<ToneMapPassRequest, ToneMapPassResult>
{
    private readonly Dictionary<(GpuFormat, bool), NativeGpuRasterPipelineHandle> pipelines = [];
    public ValueTask BuildAsync(NativePassBuildContext context, BlitPassRequest request, TexturePassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Draw(context, "blit", context.ImportTexture(request.Source), context.ImportTexture(request.Target),
            context.GetInput(request.Filter) == ImageSampling.Nearest ? 0u : 1u, 0);
        return ValueTask.CompletedTask;
    }
    public ValueTask BuildAsync(NativePassBuildContext context, BlurPassRequest request, BlurPassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = context.ImportTexture(request.Source);
        var target = context.ImportTexture(result.Color);
        var intermediate = context.CreateTexture("blur intermediate", target.Description);
        uint radius = (uint)context.GetInput(request.Radius);
        Draw(context, "horizontal", source, intermediate, 2, radius);
        Draw(context, "vertical", intermediate, target, 3, radius);
        return ValueTask.CompletedTask;
    }
    public ValueTask BuildAsync(NativePassBuildContext context, ToneMapPassRequest request, ToneMapPassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Draw(context, "tone map", context.ImportTexture(request.Source), context.ImportTexture(result.Color), 4,
            BitConverter.SingleToUInt32Bits(context.GetInput(request.ExposureStops)));
        return ValueTask.CompletedTask;
    }
    public ValueTask BuildAsync(NativePassBuildContext context, CompositePassRequest request, TexturePassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = context.ImportTexture(request.Target);
        var attachment = Attachment(context, "composite target", target);
        var sources = request.Layers.Select(layer => context.ImportTexture(layer.Source)).ToArray();
        var layers = sources.Select((source, i) => new Layer(Sampled(context, $"layer {i}", source),
            BitConverter.SingleToUInt32Bits(context.GetInput(request.Layers[i].Opacity)))).ToArray();
        var clear = context.GetInput(request.ClearColor);
        var state = new CompositeState(attachment, layers, Pipeline(target.Description.Format, true),
            request.Content, new(clear.X * clear.W, clear.Y * clear.W, clear.Z * clear.W, clear.W), target.Description.Width, target.Description.Height);
        var pass = context.AddPass("composite", state, (record, s) =>
        {
            record.Commands.BeginRendering([new(record.GetRenderView(s.Target), s.Content == TargetContent.Clear ? NativeGpuLoadOp.Clear : NativeGpuLoadOp.Load,
                ClearColor: new(s.Clear.X, s.Clear.Y, s.Clear.Z, s.Clear.W))]);
            record.Commands.SetPipeline(s.Pipeline);
            foreach (var layer in s.Layers)
            { DrawRoot(record, layer.View, 5, layer.Opacity, s.Width, s.Height); }
            record.Commands.EndRendering();
        });
        foreach (var source in sources.Distinct())
        { pass.Read(source, new(GpuStage.PixelShader, GpuAccess.ShaderRead)); }
        if (request.Content == TargetContent.Preserve)
        { pass.ReadWrite(target, new(GpuStage.ColorOutput, GpuAccess.ColorRead | GpuAccess.ColorWrite)); }
        else
        { pass.Write(target, new(GpuStage.ColorOutput, GpuAccess.ColorWrite)); }
        return ValueTask.CompletedTask;
    }
    private void Draw(NativePassBuildContext context, string name, NativePassTexture source, NativePassTexture target, uint mode, uint parameter)
    {
        var state = new DrawState(Sampled(context, name + " source", source), Attachment(context, name + " target", target),
            Pipeline(target.Description.Format, false), mode, parameter, target.Description.Width, target.Description.Height);
        context.AddPass(name, state, (record, s) =>
        {
            record.Commands.BeginRendering([new(record.GetRenderView(s.Target), NativeGpuLoadOp.Discard)]);
            record.Commands.SetPipeline(s.Pipeline);
            DrawRoot(record, s.Source, s.Mode, s.Parameter, s.Width, s.Height);
            record.Commands.EndRendering();
        }).Read(source, new(GpuStage.PixelShader, GpuAccess.ShaderRead)).Write(target, new(GpuStage.ColorOutput, GpuAccess.ColorWrite));
    }
    private void DrawRoot(NativePassRecordContext record, NativePassView source, uint mode, uint parameter, uint width, uint height)
    {
        if (services.Backend.ShaderCodeFormat == GpuShaderCodeFormat.Dxil)
        {
            DxRoot root = new() { textureIndex = record.GetShaderIndex(source), mode = mode, parameter = parameter, width = width, height = height };
            record.Commands.Draw(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref root, 1)), 3);
        }
        else
        {
            VkRoot root = new() { textureIndex = record.GetShaderIndex(source), mode = mode, parameter = parameter, width = width, height = height };
            record.Commands.Draw(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref root, 1)), 3);
        }
    }
    private static NativePassView Sampled(NativePassBuildContext context, string name, NativePassTexture texture) => context.CreateView(name, texture,
        new(NativeGpuTextureViewDimension.TwoD, texture.Description.Format, NativeGpuTextureAspect.Color, 0, 1, 0, 1));
    private static NativePassView Attachment(NativePassBuildContext context, string name, NativePassTexture texture) => context.CreateView(name, texture,
        new(NativeGpuTextureViewDimension.TwoD, texture.Description.Format, NativeGpuTextureAspect.Color, 0, 1, 0, 1, GpuTextureViewPurpose.Attachment));
    private NativeGpuRasterPipelineHandle Pipeline(GpuFormat format, bool blend)
    {
        if (pipelines.TryGetValue((format, blend), out var pipeline))
        { return pipeline; }
        using NativeShaderProgram program = services.ShaderLoader.Load(ShaderPackage.Create(), services.Backend.ShaderCodeFormat == GpuShaderCodeFormat.Dxil ? DxRoot.AbiHash : VkRoot.AbiHash);
        pipeline = services.Backend.CreateRasterPipeline(new()
        {
            ColorTargets = [new(format, Blend: new(Enabled: blend,
            DestinationColorFactor: NativeGpuBlendFactor.OneMinusSourceAlpha, DestinationAlphaFactor: NativeGpuBlendFactor.OneMinusSourceAlpha))]
        }, program.Code);
        try
        { pipelines.Add((format, blend), pipeline); }
        catch { services.Backend.DestroyRasterPipeline(pipeline); throw; }
        return pipeline;
    }
    public ValueTask DisposeAsync()
    {
        foreach (var pipeline in pipelines.Values)
        { services.Backend.DestroyRasterPipeline(pipeline); }
        pipelines.Clear();
        return ValueTask.CompletedTask;
    }
    private sealed record DrawState(NativePassView Source, NativePassView Target, NativeGpuRasterPipelineHandle Pipeline, uint Mode, uint Parameter, uint Width, uint Height);
    private sealed record Layer(NativePassView View, uint Opacity);
    private sealed record CompositeState(NativePassView Target, Layer[] Layers, NativeGpuRasterPipelineHandle Pipeline, TargetContent Content, Vector4 Clear, uint Width, uint Height);
}
