using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.Portable.Shaders;

namespace Lumyte.Graphics.Portable.Passes;

internal sealed class PortableOutputPass(PortablePassServices services) : IPortableRenderPass<OutputPassRequest, TexturePassResult>
{
    private readonly Dictionary<(GpuFormat, OutputEncoding, OutputAlphaMode), Pipeline> pipelines = [];
    public ValueTask BuildAsync(PortablePassBuildContext context, OutputPassRequest request,
        TexturePassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PortablePassTexture source = context.ImportTexture(request.Source), target = context.ImportTexture(request.Target);
        var key = (target.Description.Format, request.Encoding, request.AlphaMode);
        if (!pipelines.TryGetValue(key, out Pipeline? pipeline))
        { pipeline = CreatePipeline(key.Format, key.Encoding, key.AlphaMode); pipelines.Add(key, pipeline); }
        PortablePassView sourceView = context.CreateView("Output input", source);
        PortablePassView targetView = context.CreateView("Output attachment", target);
        PortablePassBindings bindings = context.CreateBindings("Output input", pipeline.Program, 0, new TextureInputs(sourceView));
        context.AddPass("Output", (target, targetView, bindings, pipeline.Handle), static (record, state) =>
        {
            record.Commands.BeginRendering([new(record.GetTextureView(state.targetView), GpuAttachmentLoadOperation.Clear)]);
            record.Commands.SetPipeline(state.Handle);
            record.Commands.SetBindings(0, record.GetBindings(state.bindings));
            uint width = state.target.Description.Width, height = state.target.Description.Height;
            record.Commands.SetViewportAndScissor(new(0, 0, width, height), new(0, 0, width, height));
            record.Commands.Draw(3);
            record.Commands.EndRendering();
        }).Read(source, PortablePassUsage.SampledRead).Write(target, PortablePassUsage.ColorAttachment);
        return ValueTask.CompletedTask;
    }

    private Pipeline CreatePipeline(GpuFormat format, OutputEncoding encoding, OutputAlphaMode alphaMode)
    {
        bool automaticSrgb = format is GpuFormat.Rgba8UnormSrgb or GpuFormat.Bgra8UnormSrgb;
        string source = $$"""
            @group(0) @binding(0) var sourceImage: texture_2d<f32>;
            fn encodeSrgb(value: vec3f) -> vec3f {
                let x = max(value, vec3f(0));
                return select(1.055 * pow(x, vec3f(1.0 / 2.4)) - 0.055, x * 12.92, x <= vec3f(0.0031308));
            }
            fn decodeSrgb(value: vec3f) -> vec3f {
                let x = max(value, vec3f(0));
                return select(pow((x + 0.055) / 1.055, vec3f(2.4)), x / 12.92, x <= vec3f(0.04045));
            }
            @vertex fn vertex(@builtin(vertex_index) id: u32) -> @builtin(position) vec4f {
                let vertices = array<vec2f, 3>(vec2f(-1, -1), vec2f(3, -1), vec2f(-1, 3));
                return vec4f(vertices[id], 0, 1);
            }
            @fragment fn fragment(@builtin(position) position: vec4f) -> @location(0) vec4f {
                let input = textureLoad(sourceImage, vec2i(position.xy), 0);
                var rgb = vec3f(0);
                if (input.a > 0) { rgb = input.rgb / input.a; }
                {{(encoding == OutputEncoding.Srgb ? "rgb = encodeSrgb(rgb);" : "")}}
                let alpha = {{(alphaMode == OutputAlphaMode.Opaque ? "1.0" : "input.a")}};
                rgb *= alpha;
                {{(automaticSrgb ? "rgb = decodeSrgb(rgb);" : "")}}
                return vec4f(rgb, alpha);
            }
            """;
        var package = new PortableShaderPackage(1, source,
            [new(GpuShaderStage.Vertex, "vertex"), new(GpuShaderStage.Pixel, "fragment")], PortableShaderFeatures.None,
            [new([new(0, GpuShaderStage.Pixel, new GpuTextureBindingLayout(GpuTextureSampleType.UnfilterableFloat))])], null, [],
            new([]), $"Lumyte.Output.v1.{format}.{encoding}.{alphaMode}");
        PortableShaderProgram program = services.ShaderLoader.Load(package);
        try { return new(program, services.Backend.CreateRasterPipeline(new([new(format)]), program.Description)); }
        catch { program.Dispose(); throw; }
    }
    public ValueTask DisposeAsync()
    {
        foreach (Pipeline pipeline in pipelines.Values)
        { services.Backend.DestroyRasterPipeline(pipeline.Handle); pipeline.Program.Dispose(); }
        pipelines.Clear(); return ValueTask.CompletedTask;
    }
    private sealed record Pipeline(PortableShaderProgram Program, GpuRasterPipelineHandle Handle);
    private sealed class TextureInputs(PortablePassView view) : IPortablePassBindingInputs
    { public void Write(PortablePassBindingWriter writer) => writer.Texture(0, view); }
}
