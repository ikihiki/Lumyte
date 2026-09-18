using System.Runtime.InteropServices;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.Portable.Resources;
using Lumyte.Graphics.Portable.Shaders;

namespace Lumyte.Graphics.Portable.Passes;

internal sealed class PortableModelEnvironment(PortablePassServices services) : IDisposable
{
    private readonly Dictionary<(GpuImageUploadData? Image, uint Mode), PortablePassContentGeneration<GpuTextureRef>> cache = [];
    private PortableShaderProgram? program;
    private GpuRasterPipelineHandle? pipeline;
    private int sequence;

    internal PortablePassTexture[] Prepare(PortablePassBuildContext context, PortablePassTexture source, GpuImageUploadData image)
        => [Get(context, source, image, 0, 32, 16), Get(context, source, image, 1, 128, 512), Get(context, source, null, 2, 128, 128)];

    private PortablePassTexture Get(PortablePassBuildContext context, PortablePassTexture source, GpuImageUploadData? image, uint mode, uint width, uint height)
    {
        var key = (image, mode);
        cache.TryGetValue(key, out var generation);
        if (context.TryUseContent(generation, out GpuTextureRef texture)) { return context.ImportTexture(texture); }
        generation?.Dispose();
        string name = $"model environment {sequence++}";
        using var scope = services.Resources.CreateScope();
        texture = scope.CreateTexture(new(GpuTextureDimension.Texture2D, width, height, 1, 1, 1, 1,
            GpuFormat.Rgba16Float, GpuTextureUsage.Sampled | GpuTextureUsage.ColorAttachment));
        var target = context.ImportTexture(texture);
        var attachment = context.CreateView(name + " target", target);
        var sourceView = context.CreateView(name + " source", source, new(BaseMip: 0, MipCount: 1));
        var selected = Pipeline();
        var bindings = context.CreateBindings(name + " bindings", program!, 0, new Input(sourceView));
        var state = (attachment, bindings, Pipeline: selected, Root: new Root(mode, width, height));
        var writer = context.AddPass(name, state, static (record, s) =>
        {
            record.Commands.BeginRendering([new(record.GetTextureView(s.attachment), GpuAttachmentLoadOperation.Clear)]);
            record.Commands.SetPipeline(s.Pipeline);
            record.Commands.SetBindings(0, record.GetBindings(s.bindings));
            var root = s.Root; record.Commands.SetRootData(in root);
            record.Commands.Draw(3); record.Commands.EndRendering();
        }).Read(source, PortablePassUsage.SampledRead).Write(target, PortablePassUsage.ColorAttachment);
        generation = context.RegisterContent(texture, services.Resources.Pin(texture), [writer]);
        cache[key] = generation;
        _ = context.TryUseContent(generation, out texture);
        while (cache.Count > 17)
        {
            var evicted = cache.Keys.First(k => k.Mode != 2 && k != key);
            cache[evicted].Dispose(); cache.Remove(evicted);
        }
        return target;
    }
    private GpuRasterPipelineHandle Pipeline()
    {
        if (pipeline is { } value) { return value; }
        using var stream = typeof(PortableModelEnvironment).Assembly.GetManifestResourceStream("Lumyte.Graphics.Portable.Passes.Models.Environment.wgsl")!;
        using var reader = new StreamReader(stream);
        program ??= services.ShaderLoader.Load(new(1, reader.ReadToEnd(), [new(GpuShaderStage.Vertex, "vertex"), new(GpuShaderStage.Pixel, "fragment")],
            PortableShaderFeatures.ImmediateAddressSpace,
            [new([new(0, GpuShaderStage.Pixel, new GpuTextureBindingLayout(GpuTextureSampleType.UnfilterableFloat))])],
            new("Root", 12, 4, [new("mode", "u32", 0, 4, 4), new("width", "u32", 4, 4, 4), new("height", "u32", 8, 4, 4)]), [], new([]), "Lumyte.Portable.Model.Environment.v1"));
        pipeline = services.Backend.CreateRasterPipeline(new([new(GpuFormat.Rgba16Float)]), program.Description);
        return pipeline;
    }
    public void Dispose()
    {
        foreach (var item in cache.Values) { item.Dispose(); }
        cache.Clear();
        if (pipeline is { } value) { services.Backend.DestroyRasterPipeline(value); }
        program?.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Root(uint Mode, uint Width, uint Height);
    private sealed class Input(PortablePassView source) : IPortablePassBindingInputs
    { public void Write(PortablePassBindingWriter writer) => writer.Texture(0, source); }
}
