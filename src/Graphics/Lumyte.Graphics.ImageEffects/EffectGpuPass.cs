using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

internal static class EffectGpuPass
{
    private static readonly BarrierScope _sample = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
    private static readonly BarrierScope _write = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);

    internal static EffectImage Append(EffectPreparationContext owner, RenderGraphBuilder graph, EffectImage input, EffectImage backdrop, EffectPixelRect outputBounds, Vector4 settings, ColorMatrix4x5? matrix = null, int offsetX = 0, int offsetY = 0, Vector4 extra = default, EffectColorSemantic? outputSemantic = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(graph);
        owner.EnsureAlive();
        ImageEffectRenderGraphExtensions.ValidateImage(input);
        ImageEffectRenderGraphExtensions.ValidateImage(backdrop);
        if (input.ColorSemantic != backdrop.ColorSemantic)
        {
            throw new ArgumentException("Effect inputs must have the same color semantic.", nameof(backdrop));
        }

        var descriptor = new TextureDesc
        {
            Width = checked((uint)outputBounds.Width),
            Height = checked((uint)outputBounds.Height),
            Format = TextureFormat.Rgba16Float,
            Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment | TextureUsage.CopySource,
        };
        RenderGraphTexture output = graph.CreateTexture(descriptor, "image-effect-output");
        IGraphicDevice device = owner.Device;
        ImageEffectShaders shaders = owner.Shaders ?? ImageEffectShaders.Embedded;
        IGraphicsShader vertex = shaders.FullscreenShader ?? owner.Add(device.CreateShader(shaders.Fullscreen));
        IGraphicsShader fragment = shaders.EffectsShader ?? owner.Add(device.CreateShader(shaders.Effects));
        IGraphicsPipeline pipeline = shaders.Pipeline ?? owner.Add(device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment, Label = "image-effect" }));
        RenderGraphShaderResources resources = graph.CreateShaderResources(fragment.Artifact)
            .ReadTexture("Input", input.Texture)
            .ReadTexture("Backdrop", backdrop.Texture)
            .Sampler("Sampler", shaders.Sampler ?? graph.GetSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, LodMaxClamp = 0 }))
            .Value("InputBounds", RelativeBounds(input.Bounds.Offset(offsetX, offsetY), outputBounds))
            .Value("BackdropBounds", RelativeBounds(backdrop.Bounds, outputBounds))
            .Value("OutputBounds", new Vector4(0, 0, outputBounds.Width, outputBounds.Height))
            .Value("Settings", settings)
            .Value("Row0", matrix is null ? extra : Row(matrix, 0))
            .Value("Row1", Row(matrix, 1))
            .Value("Row2", Row(matrix, 2))
            .Value("Row3", Row(matrix, 3))
            .Value("Constant", Constants(matrix));
        int recorded = 0;
        RenderGraphPass pass = graph.AddPass("image-effect", context =>
        {
            owner.EnsureAlive();
            if (Interlocked.Exchange(ref recorded, 1) != 0)
            {
                throw new InvalidOperationException("A prepared effect pass can be recorded only once.");
            }

            ValidateAllocation(context.GetTexture(input.Texture), input.Bounds);
            ValidateAllocation(context.GetTexture(backdrop.Texture), backdrop.Bounds);
            using RenderGraphRenderEncoder render = context.BeginRenderPass(new()
            {
                ColorAttachments = [new() { Texture = output, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }],
            });
            render.SetPipeline(pipeline);
            render.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
            render.SetViewport(new(0, 0, descriptor.Width, descriptor.Height, 0, 1));
            render.SetScissor(new(0, 0, descriptor.Width, descriptor.Height));
            render.SetBlendConstant(new(0, 0, 0, 0));
            render.SetStencilReference(0);
            render.Draw(resources, 3);
            render.End();
        }).UseTexture(input.Texture, TextureState.Sampled, _sample);
        if (backdrop.Texture != input.Texture)
        {
            pass.UseTexture(backdrop.Texture, TextureState.Sampled, _sample);
        }

        pass.UseTexture(output, TextureState.ColorAttachment, _write);
        pass.UseShaderResources(resources, output);
        return new(output, outputBounds, outputSemantic ?? input.ColorSemantic);
    }

    // Subtract integer origins before conversion so neighboring pixels remain distinct beyond 2^24.
    private static Vector4 RelativeBounds(EffectPixelRect bounds, EffectPixelRect origin) => new((long)bounds.X - origin.X, (long)bounds.Y - origin.Y, bounds.Width, bounds.Height);

    private static Vector4 Row(ColorMatrix4x5? matrix, int row) => matrix is null ? Vector4.Zero : new(matrix.Coefficients[row * 5], matrix.Coefficients[(row * 5) + 1], matrix.Coefficients[(row * 5) + 2], matrix.Coefficients[(row * 5) + 3]);

    private static Vector4 Constants(ColorMatrix4x5? matrix) => matrix is null ? Vector4.Zero : new(matrix.Coefficients[4], matrix.Coefficients[9], matrix.Coefficients[14], matrix.Coefficients[19]);

    private static void ValidateAllocation(IGraphicsTexture texture, EffectPixelRect bounds)
    {
        if (texture.Format != TextureFormat.Rgba16Float || texture.MipLevels != 1 || texture.ArrayLayers != 1 || texture.Width != bounds.Width || texture.Height != bounds.Height || (texture.Usage & TextureUsage.Sampled) == 0)
        {
            throw new ArgumentException("Effect input must be a sampled, single-mip/layer Rgba16Float allocation matching its bounds.", nameof(texture));
        }
    }
}
