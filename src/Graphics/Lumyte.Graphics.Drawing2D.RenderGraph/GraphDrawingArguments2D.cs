using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

internal sealed class GraphDrawingArguments2D : IRenderGraphPreparation
{
    private readonly List<DrawingArgumentPlan2D> _plans = [];
    private readonly HashSet<ArgumentTableReservation> _fallbackScopes = [];
    private readonly RenderGraphBuilder _graph;
    private IGraphicsSampler? _sampler;
    private RenderGraphTexture? _fallback;
    private bool _disposed;

    private GraphDrawingArguments2D(RenderGraphBuilder graph)
    {
        _graph = graph;
        Arguments = graph.GetArguments();
    }

    internal RenderGraphArguments Arguments { get; }

    internal RenderGraphTexture Fallback
    {
        get
        {
            if (_fallback is not null)
            {
                return _fallback;
            }

            RenderGraphBuilder graph = _graph;
            _fallback = graph.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled }, "drawing2d.shared-fallback");
            graph.AddPass("drawing2d.initialize-shared-fallback", context =>
            {
                IRenderEncoder clear = context.Commands.BeginRenderPass(new() { ColorAttachments = [new() { View = context.CreateTextureView(_fallback), LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
                clear.End();
            }).UseTexture(_fallback, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), discard: true);
            return _fallback;
        }
    }

    public void Validate() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Prepare(RenderGraphPreparationContext context)
    {
        foreach (DrawingArgumentPlan2D plan in _plans)
        {
            plan.FinalizeMapped();
        }
    }

    public void Dispose()
    {
        while (_plans.Count != 0)
        {
            _plans[^1].Dispose();
            _plans.RemoveAt(_plans.Count - 1);
        }

        _disposed = true;
    }

    internal static GraphDrawingArguments2D Get(RenderGraphBuilder graph) => graph.GetOrAddExtension(owner =>
    {
        var state = new GraphDrawingArguments2D(owner);
        owner.Retain(state);
        return state;
    });

    internal bool RequiresFallback(IEnumerable<ArgumentTableReservation> scopes) => scopes.Any(_fallbackScopes.Contains);

    internal void Own(DrawingArgumentPlan2D plan)
    {
        Validate();
        _plans.Add(plan);
    }

    internal IGraphicsSampler Sampler() => DefaultSampler();

    internal ArgumentRegistration<IGraphicsTextureView> RegisterFallback(ArgumentTableReservation scope)
    {
        _fallbackScopes.Add(scope);
        return Arguments.RegisterTexture(scope, Fallback);
    }

    private IGraphicsSampler DefaultSampler() => _sampler ??= Arguments.GetSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, LodMaxClamp = 0 });
}
