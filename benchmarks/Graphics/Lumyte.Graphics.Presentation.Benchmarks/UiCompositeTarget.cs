using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Presentation.Benchmarks;

// One persistent intermediate and immutable binding table per bounded slot.
internal sealed class UiCompositeTarget : IDisposable
{
    private readonly RetirementScope _ownership;
    private bool _initialized;

    internal UiCompositeTarget(IGraphicDevice device, IGraphicsSampler sampler, RetirementScope parent)
    {
        _ownership = new RetirementScope { Parent = parent };
        try
        {
            Texture = _ownership.Own(device.CreateTexture(new()
            {
                Width = ClearFixture.Width,
                Height = ClearFixture.Height,
                Format = TextureFormat.Rgba8Srgb,
                Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled,
            }));
            View = _ownership.Own(Texture.CreateView());
            Table = _ownership.Own(device.CreateArgumentTable(new() { TextureCapacity = 1, SamplerCapacity = 1 }));
            Arguments = new(Table.WriteTexture(0, View), Table.WriteSampler(0, sampler), new(Texture.Width, Texture.Height), 0);
        }
        catch
        {
            _ownership.Dispose();
            throw;
        }
    }

    internal IGraphicsTexture Texture { get; }

    internal IGraphicsTextureView View { get; }

    internal IArgumentTable Table { get; }

    internal UiPresentArguments Arguments { get; }

    public void Dispose() => _ownership.Dispose();

    internal void Begin(IGraphicsCommandBuffer commands)
    {
        commands.Barrier(new TextureBarrierDesc
        {
            Texture = Texture,
            Range = new(0, 1, 0, 1),
            BeforeState = _initialized ? TextureState.Sampled : TextureState.Undefined,
            AfterState = TextureState.ColorAttachment,
            Before = _initialized ? new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) : default,
            After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite),
        });
        _initialized = true;
    }

    internal void BetweenLayers(IGraphicsCommandBuffer commands)
    {
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc
        {
            Texture = Texture,
            Range = new(0, 1, 0, 1),
            BeforeState = TextureState.ColorAttachment,
            AfterState = TextureState.ColorAttachment,
            Before = color,
            After = color,
        });
    }
}
