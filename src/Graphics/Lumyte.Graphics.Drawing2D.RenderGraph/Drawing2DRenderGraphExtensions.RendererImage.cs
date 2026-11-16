using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>Adapts strategy-independent drawing to caller-owned render graphs.</summary>
public static partial class Drawing2DRenderGraphExtensions
{
    /// <summary>Appends drawing to a transparent DisplayLinear image in the caller's graph.</summary>
    /// <param name="graph">The building parent graph on the renderer's device.</param>
    /// <param name="renderer">The borrowed drawing backend.</param>
    /// <param name="content">The immutable drawing content, kept alive until preparation completes.</param>
    /// <param name="options">The physical bounds and logical pixel scale.</param>
    /// <param name="cancellationToken">Cancels preparation.</param>
    /// <returns>The graph-local linear premultiplied Rgba16Float image.</returns>
    /// <remarks>On preparation failure discard the graph. Its retained owners survive renderer disposal.</remarks>
    public static ValueTask<EffectImage> Append2D(this RenderGraphBuilder graph, IRenderer2D renderer, DrawList2D content, DrawingImageOptions2D options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        IDrawingPassRenderer2D backend = ValidateRendererImage(graph, renderer, options, cancellationToken);
        return AppendRendererImageAsync(graph, options, (target, arguments) => backend.PreparePassesAsync(content, target, arguments, cancellationToken), cancellationToken);
    }

    /// <summary>Appends retained scene content to a transparent DisplayLinear image.</summary>
    /// <param name="graph">The building parent graph on the renderer's device.</param>
    /// <param name="renderer">The borrowed drawing backend.</param>
    /// <param name="source">The caller-owned read lease, held until this call completes.</param>
    /// <param name="options">The physical bounds and logical pixel scale.</param>
    /// <param name="cancellationToken">Cancels preparation.</param>
    /// <returns>The graph-local linear premultiplied Rgba16Float image.</returns>
    public static ValueTask<EffectImage> Append2D(this RenderGraphBuilder graph, IRenderer2D renderer, DrawingSceneReadLease2D source, DrawingImageOptions2D options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        IDrawingPassRenderer2D backend = ValidateRendererImage(graph, renderer, options, cancellationToken);
        return AppendRendererImageAsync(graph, options, (target, arguments) => backend.PreparePassesAsync(source, target, arguments, cancellationToken), cancellationToken);
    }

    /// <summary>Appends immutable retained content to a transparent DisplayLinear image.</summary>
    /// <param name="graph">The building parent graph on the renderer's device.</param>
    /// <param name="renderer">The borrowed drawing backend.</param>
    /// <param name="source">The caller-owned snapshot, held until this call completes.</param>
    /// <param name="options">The physical bounds and logical pixel scale.</param>
    /// <param name="cancellationToken">Cancels preparation.</param>
    /// <returns>The graph-local linear premultiplied Rgba16Float image.</returns>
    public static ValueTask<EffectImage> Append2D(this RenderGraphBuilder graph, IRenderer2D renderer, DrawingSnapshot2D source, DrawingImageOptions2D options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        IDrawingPassRenderer2D backend = ValidateRendererImage(graph, renderer, options, cancellationToken);
        return AppendRendererImageAsync(graph, options, (target, arguments) => backend.PreparePassesAsync(source, target, arguments, cancellationToken), cancellationToken);
    }

    private static IDrawingPassRenderer2D ValidateRendererImage(RenderGraphBuilder graph, IRenderer2D renderer, DrawingImageOptions2D options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(options);
        if (renderer is not IDrawingPassRenderer2D backend)
        {
            throw new NotSupportedException("The renderer does not provide parent-graph drawing preparation.");
        }

        if (!graph.IsBuilding)
        {
            throw new InvalidOperationException("Drawing images require a building parent graph.");
        }

        if (graph.Device is null || !ReferenceEquals(graph.Device, renderer.Device))
        {
            throw new ArgumentException("The renderer and parent graph must use the same graphics device.", nameof(renderer));
        }

        EffectPixelRect bounds = options.Bounds;
        if (bounds.IsEmpty || !float.IsFinite(options.PixelScale) || options.PixelScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Image bounds must be positive and pixel scale finite and positive.");
        }

        _ = checked(bounds.X + bounds.Width);
        _ = checked(bounds.Y + bounds.Height);
        if ((uint)bounds.Width > graph.Device.Caps.MaxTextureDimension2D || (uint)bounds.Height > graph.Device.Caps.MaxTextureDimension2D)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Image bounds exceed the device texture dimension limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return backend;
    }

    private static async ValueTask<EffectImage> AppendRendererImageAsync(RenderGraphBuilder graph, DrawingImageOptions2D options, Func<DrawingTargetInfo2D, DrawingArgumentPlan2D, ValueTask<DrawingPassPlan2D>> prepare, CancellationToken cancellationToken)
    {
        var owner = new RendererImageLifetime2D();
        graph.Retain(owner);
        var shared = GraphDrawingArguments2D.Get(graph);
        var arguments = new DrawingArgumentPlan2D(graph.Device!, registrations: null, graphFallback: (scope, resolved) => shared.RegisterFallback(scope, resolved), samplerFactory: () => shared.Sampler(), resourceFactory: () => shared.CreateResources());
        owner.Own(arguments);
        var description = new TextureDesc
        {
            Width = checked((uint)options.Bounds.Width),
            Height = checked((uint)options.Bounds.Height),
            Format = TextureFormat.Rgba16Float,
            Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled,
        };
        var info = new DrawingTargetInfo2D
        {
            Description = description,
            PixelOrigin = new Vector2(options.Bounds.X, options.Bounds.Y),
            PixelScale = options.PixelScale,
            LoadOp = AttachmentLoadOp.Load,
        };
        DrawingPassPlan2D plan = await prepare(info, arguments).ConfigureAwait(false);
        owner.Own(plan);
        cancellationToken.ThrowIfCancellationRequested();
        RenderGraphTexture texture = graph.CreateTexture(description, "drawing2d.image");
        var target = new RenderTargetBinding2D { Texture = texture, Description = description, PixelOrigin = info.PixelOrigin, PixelScale = info.PixelScale };
        graph.AddPass("drawing2d.image.clear", context =>
        {
            IRenderEncoder clear = context.Commands.BeginRenderPass(new()
            {
                ColorAttachments = [new() { View = context.CreateTextureView(texture), LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new ClearColor(0, 0, 0, 0) }],
            });
            clear.End();
        }).UseTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), discard: true);
        RendererImageImports2D imports = graph.GetOrAddExtension(static parent => new RendererImageImports2D(parent));
        DrawingGraphBindings2D bindings = imports.Bindings;
        foreach (DrawingPass2D drawingPass in plan.Passes)
        {
            if (!drawingPass.LocalResources)
            {
                drawingPass.DeclareResources(imports);
            }

            RenderGraphPass pass = graph.AddPass(drawingPass.Name, context => drawingPass.Record(context.Commands, drawingPass.UsesTarget ? context.CreateTextureView(texture) : null!));
            if (drawingPass.UsesTarget)
            {
                pass.UseTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
            }

            var declarations = new DrawingResourceDeclarations2D(pass, target, bindings);
            drawingPass.DeclareResources(drawingPass.LocalResources ? new RendererImageLocalDeclarations2D(graph, pass, declarations, imports) : declarations);
            if (!drawingPass.LocalResources)
            {
                plan.ArgumentPlan.DeclarePreparedResources(drawingPass.Reservations, declarations.Prepared);
            }

            declarations.Apply();
            if (drawingPass.Reservations.Count != 0 && shared.RequiresFallback(drawingPass.Reservations))
            {
                pass.UseTexture(shared.Fallback, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
            }

            foreach (ShaderResources reservation in drawingPass.Reservations)
            {
                pass.AttachPreparedPayload(reservation);
            }
        }

        await plan.ArgumentPlan.PrepareMappingsAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new EffectImage(texture, options.Bounds, EffectColorSemantic.DisplayLinear);
    }

    private sealed class RendererImageLifetime2D : IRenderGraphLifetime
    {
        private readonly List<IDisposable> _owners = [];
        private bool _disposed;

        public void Validate() => ObjectDisposedException.ThrowIf(_disposed, this);

        public void Dispose()
        {
            _disposed = true;
            while (_owners.Count != 0)
            {
                _owners[^1].Dispose();
                _owners.RemoveAt(_owners.Count - 1);
            }
        }

        internal void Own(IDisposable resource)
        {
            Validate();
            _owners.Add(resource);
        }
    }
}
