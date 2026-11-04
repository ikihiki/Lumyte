using System.Security.Cryptography;
using Lumyte.Graphics.Abstractions;
using Lumyte.UI.Application;

namespace Lumyte.Graphics.Presentation.Benchmarks;

// Same RGBA intermediate -> sampled fullscreen triangle route as UiHostPresenter.
internal sealed class UiCompositePass : IDisposable
{
    private readonly RetirementScope _ownership;
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsSampler _sampler;

    internal UiCompositePass(IGraphicDevice device, RetirementScope parent)
    {
        _ownership = new RetirementScope { Parent = parent };
        try
        {
            var vertexArtifact = ShaderArtifact.LoadEmbedded(typeof(UiApplication).Assembly, "Lumyte.UI.Application.present-vertex.lshader");
            var fragmentArtifact = ShaderArtifact.LoadEmbedded(typeof(UiApplication).Assembly, "Lumyte.UI.Application.present-fragment.lshader");
            ShaderProvenance = new
            {
                target = device.Caps.ShaderTarget.ToString(),
                vertexSha256 = Convert.ToHexString(SHA256.HashData(vertexArtifact.GetTarget(device.Caps.ShaderTarget).Code)),
                fragmentSha256 = Convert.ToHexString(SHA256.HashData(fragmentArtifact.GetTarget(device.Caps.ShaderTarget).Code)),
                scope = "actual loaded product present shader target-code bytes before runtime binding specialization; source/container/binary provenance recorded separately",
            };
            IGraphicsShader vertex = _ownership.Own(device.CreateShader(vertexArtifact));
            IGraphicsShader fragment = _ownership.Own(device.CreateShader(fragmentArtifact));
            _pipeline = _ownership.Own(device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment }));
            _sampler = _ownership.Own(device.CreateSampler(new()));
        }
        catch
        {
            _ownership.Dispose();
            throw;
        }
    }

    internal object ShaderProvenance { get; }

    public void Dispose() => _ownership.Dispose();

    internal UiCompositeTarget CreateTarget(IGraphicDevice device, RetirementScope parent) => new(device, _sampler, parent);

    internal void Record(IGraphicsCommandBuffer commands, UiCompositeTarget source, IGraphicsTexture destination, IGraphicsTextureView display, bool present)
    {
        if (destination.Format != TextureFormat.Bgra8Srgb)
        {
            throw new NotSupportedException("Exact BGRA8-sRGB composite destination required.");
        }

        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        var sample = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
        commands.Barrier(new TextureBarrierDesc
        {
            Texture = source.Texture,
            Range = new(0, 1, 0, 1),
            BeforeState = TextureState.ColorAttachment,
            AfterState = TextureState.Sampled,
            Before = color,
            After = sample,
        });
        commands.Barrier(new TextureBarrierDesc
        {
            Texture = destination,
            Range = new(0, 1, 0, 1),
            BeforeState = TextureState.Undefined,
            AfterState = TextureState.ColorAttachment,
            Before = default,
            After = color,
        });
        IRenderEncoder render = commands.BeginRenderPass(new()
        {
            ColorAttachments = [new() { View = display, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }],
        });
        try
        {
            render.SetPipeline(_pipeline);
            render.SetArgumentTable(source.Table);
            render.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
            render.SetViewport(new(0, 0, source.Texture.Width, source.Texture.Height, 0, 1));
            render.SetScissor(new(0, 0, source.Texture.Width, source.Texture.Height));
            render.SetBlendConstant(new(0, 0, 0, 0));
            render.SetStencilReference(0);
            UiPresentArguments arguments = source.Arguments;
            render.SetArguments(in arguments);
            render.Draw(3);
        }
        finally
        {
            render.End();
        }

        if (present)
        {
            commands.Barrier(new TextureBarrierDesc
            {
                Texture = destination,
                Range = new(0, 1, 0, 1),
                BeforeState = TextureState.ColorAttachment,
                AfterState = TextureState.Present,
                Before = color,
                After = default,
            });
        }
    }
}
