using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.Portable.Shaders;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.Portable.Passes;

internal sealed class PortableFilterPass(PortablePassServices services) :
    IPortableRenderPass<BlitPassRequest, TexturePassResult>, IPortableRenderPass<BlurPassRequest, BlurPassResult>,
    IPortableRenderPass<CompositePassRequest, TexturePassResult>, IPortableRenderPass<ToneMapPassRequest, ToneMapPassResult>
{
    private readonly Dictionary<(GpuFormat, bool), GpuRasterPipelineHandle> pipelines = [];
    private PortableShaderProgram? program;
    public ValueTask BuildAsync(PortablePassBuildContext context, BlitPassRequest request, TexturePassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Draw(context, "blit", context.ImportTexture(request.Source), context.ImportTexture(request.Target),
            context.GetInput(request.Filter) == ImageSampling.Nearest ? 0u : 1u, 0);
        return ValueTask.CompletedTask;
    }
    public ValueTask BuildAsync(PortablePassBuildContext context, BlurPassRequest request, BlurPassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = context.ImportTexture(request.Source);
        var target = context.ImportTexture(result.Color);
        var intermediate = context.CreateTexture("blur intermediate", target.Description with { Usage = GpuTextureUsage.Sampled | GpuTextureUsage.ColorAttachment });
        uint radius = (uint)context.GetInput(request.Radius);
        Draw(context, "horizontal", source, intermediate, 2, radius);
        Draw(context, "vertical", intermediate, target, 3, radius);
        return ValueTask.CompletedTask;
    }
    public ValueTask BuildAsync(PortablePassBuildContext context, ToneMapPassRequest request, ToneMapPassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Draw(context, "tone map", context.ImportTexture(request.Source), context.ImportTexture(result.Color), 4,
            BitConverter.SingleToUInt32Bits(context.GetInput(request.ExposureStops)));
        return ValueTask.CompletedTask;
    }
    public ValueTask BuildAsync(PortablePassBuildContext context, CompositePassRequest request, TexturePassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = context.ImportTexture(request.Target);
        var pipeline = Pipeline(target.Description.Format, true);
        var attachment = context.CreateView("composite target", target);
        var sources = request.Layers.Select(layer => context.ImportTexture(layer.Source)).ToArray();
        var layers = sources.Select((source, i) => new Layer(Bind(context, $"layer {i}", source),
            BitConverter.SingleToUInt32Bits(context.GetInput(request.Layers[i].Opacity)))).ToArray();
        var clear = context.GetInput(request.ClearColor);
        var state = new CompositeState(attachment, layers, pipeline, request.Content,
            new(clear.X * clear.W, clear.Y * clear.W, clear.Z * clear.W, clear.W), target.Description.Width, target.Description.Height);
        var pass = context.AddPass("composite", state, static (record, s) =>
        {
            record.Commands.BeginRendering([new(record.GetTextureView(s.Target), s.Content == TargetContent.Clear ? GpuAttachmentLoadOperation.Clear : GpuAttachmentLoadOperation.Load,
                ClearColor: new(s.Clear.X, s.Clear.Y, s.Clear.Z, s.Clear.W))]);
            record.Commands.SetPipeline(s.Pipeline);
            record.Commands.SetViewportAndScissor(new(0, 0, s.Width, s.Height), new(0, 0, s.Width, s.Height));
            foreach (var layer in s.Layers)
            {
                record.Commands.SetBindings(0, record.GetBindings(layer.Bindings));
                Root root = new(5, layer.Opacity, s.Width, s.Height);
                record.Commands.SetRootData(in root);
                record.Commands.Draw(3);
            }
            record.Commands.EndRendering();
        });
        foreach (var source in sources.Distinct())
        { pass.Read(source, PortablePassUsage.SampledRead); }
        if (request.Content == TargetContent.Preserve)
        { pass.ReadWrite(target, PortablePassUsage.ColorAttachment); }
        else
        { pass.Write(target, PortablePassUsage.ColorAttachment); }
        return ValueTask.CompletedTask;
    }
    private void Draw(PortablePassBuildContext context, string name, PortablePassTexture source, PortablePassTexture target, uint mode, uint parameter)
    {
        var pipeline = Pipeline(target.Description.Format, false);
        var bindings = Bind(context, name + " source", source);
        var attachment = context.CreateView(name + " target", target);
        var state = new DrawState(attachment, bindings, pipeline, new(mode, parameter, target.Description.Width, target.Description.Height));
        context.AddPass(name, state, static (record, s) =>
        {
            record.Commands.BeginRendering([new(record.GetTextureView(s.Target), GpuAttachmentLoadOperation.Clear)]);
            record.Commands.SetPipeline(s.Pipeline);
            record.Commands.SetBindings(0, record.GetBindings(s.Bindings));
            record.Commands.SetViewportAndScissor(new(0, 0, s.Root.Width, s.Root.Height), new(0, 0, s.Root.Width, s.Root.Height));
            Root root = s.Root;
            record.Commands.SetRootData(in root);
            record.Commands.Draw(3);
            record.Commands.EndRendering();
        }).Read(source, PortablePassUsage.SampledRead).Write(target, PortablePassUsage.ColorAttachment);
    }
    private PortablePassBindings Bind(PortablePassBuildContext context, string name, PortablePassTexture texture) =>
        context.CreateBindings(name, program!, 0, new Inputs(context.CreateView(name, texture)));
    private GpuRasterPipelineHandle Pipeline(GpuFormat format, bool blend)
    {
        if (pipelines.TryGetValue((format, blend), out var pipeline))
        { return pipeline; }
        if (program is null)
        {
            using var stream = typeof(PortableFilterPass).Assembly.GetManifestResourceStream("Lumyte.Graphics.Portable.Passes.ImageProcessing.Filter.wgsl")!;
            using var reader = new StreamReader(stream);
            var package = new PortableShaderPackage(1, reader.ReadToEnd(), [new(GpuShaderStage.Vertex, "vertex"), new(GpuShaderStage.Pixel, "fragment")],
                PortableShaderFeatures.ImmediateAddressSpace,
                [new([new(0, GpuShaderStage.Pixel, new GpuTextureBindingLayout(GpuTextureSampleType.UnfilterableFloat))])],
                new("Root", 16, 4, [new("mode", "u32", 0, 4, 4), new("parameter", "u32", 4, 4, 4), new("width", "u32", 8, 4, 4), new("height", "u32", 12, 4, 4)]),
                [], new([]), "Lumyte.Portable.ImageFilter.v1");
            program = services.ShaderLoader.Load(package);
        }
        GpuBlendDescription? blending = blend ? new(DestinationColorFactor: GpuBlendFactor.OneMinusSourceAlpha, DestinationAlphaFactor: GpuBlendFactor.OneMinusSourceAlpha) : null;
        pipeline = services.Backend.CreateRasterPipeline(new([new(format, Blend: blending)]), program.Description);
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
        program?.Dispose();
        program = null;
        return ValueTask.CompletedTask;
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Root(uint Mode, uint Parameter, uint Width, uint Height);
    private sealed class Inputs(PortablePassView texture) : IPortablePassBindingInputs
    { public void Write(PortablePassBindingWriter writer) => writer.Texture(0, texture); }
    private sealed record DrawState(PortablePassView Target, PortablePassBindings Bindings, GpuRasterPipelineHandle Pipeline, Root Root);
    private sealed record Layer(PortablePassBindings Bindings, uint Opacity);
    private sealed record CompositeState(PortablePassView Target, Layer[] Layers, GpuRasterPipelineHandle Pipeline, TargetContent Content, Vector4 Clear, uint Width, uint Height);
}
