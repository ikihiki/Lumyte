using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>Prepares typed drawing commands and image effects for a single-queue render graph.</summary>
/// <param name="list">The reusable layered recording.</param>
/// <param name="target">The graph-local destination.</param>
/// <param name="bindings">The external resource bindings.</param>
/// <param name="renderers">The borrowed fixed command renderer table.</param>
/// <param name="resources">Optional borrowed drawing resources; omission uses lazy embedded defaults.</param>
/// <param name="resourceOwner">An optional owner transferred on successful construction and shared with prepared passes.</param>
/// <param name="validateResources">An optional runtime validation used before preparation and recording.</param>
public sealed class RenderGraphRenderer2D(DrawingCommandRenderers2D renderers, RenderGraphDrawList2D list, RenderTargetBinding2D target, DrawingGraphBindings2D bindings, DrawingResources2D? resources = null, IDisposable? resourceOwner = null, Action? validateResources = null) : IDisposable
{
    private static readonly BarrierScope _attachmentScope = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
    private static readonly BarrierScope _imageScope = new(PipelineStage.VertexShader | PipelineStage.FragmentShader | PipelineStage.ComputeShader, ResourceAccess.ShaderRead);
    private readonly DrawingCommandRenderers2D _renderers = renderers ?? throw new ArgumentNullException(nameof(renderers));
    private readonly DrawList2D _list = (list ?? throw new ArgumentNullException(nameof(list))).Commands;
    private readonly RenderTargetBinding2D _target = target ?? throw new ArgumentNullException(nameof(target));
    private readonly DrawingGraphBindings2D _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
    private readonly DrawingResourceState2D _resources = new(bindings.Graph.Device, resources, resourceOwner, validateResources);

    private bool _disposed;
    private int _preparing;

    /// <summary>Releases this host without disposing borrowed strategies or waiting for GPU work.</summary>
    public void Dispose()
    {
        _disposed = true;
        _resources.Dispose();
    }

    /// <summary>Appends drawing passes to a shared graph and returns their fixed resource owner.</summary>
    /// <remarks>On failure the caller discards the graph. Keep the returned owner through GPU completion and graph disposal.</remarks>
    /// <param name="graph">The receiving render graph.</param>
    /// <param name="cancellationToken">The preparation cancellation token.</param>
    /// <returns>The owned resources referenced by the appended callbacks.</returns>
    internal async ValueTask<PreparedDrawingPasses2D> AppendAsync(RenderGraphBuilder graph, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(graph);
        DrawList2D list = _list;
        RenderTargetBinding2D target = _target;
        DrawingGraphBindings2D bindings = _bindings;
        if (graph.Device is null)
        {
            throw new InvalidOperationException("Layered drawing requires a RenderGraph with a graphics device.");
        }

        ValidateTarget(target);
        if (!ReferenceEquals(graph, bindings.Graph))
        {
            throw new ArgumentException("Image bindings belong to a different render graph.", nameof(bindings));
        }

        cancellationToken.ThrowIfCancellationRequested();
        DrawingGraphBindings2D snapshot = bindings.Snapshot();
        foreach (IGraphicsTexture image in GetImageAllocations(list.Commands))
        {
            _ = snapshot.GetTexture(image);
        }

        if (Interlocked.CompareExchange(ref _preparing, 1, 0) != 0)
        {
            throw new InvalidOperationException("Concurrent preparation using one RenderGraphRenderer2D is not supported.");
        }

        var owner = new PreparedDrawingPasses2D();
        try
        {
            graph.AddPass("drawing2d.begin", _ => owner.BeginRecord()).KeepAlive();
            AppendClear(graph, target);
            await AppendCommandsAsync(graph, list, 0, list.Commands.Count, target with { LoadOp = AttachmentLoadOp.Load }, snapshot, owner, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return owner;
        }
        catch
        {
            owner.Dispose();
            throw;
        }
        finally
        {
            Volatile.Write(ref _preparing, 0);
        }
    }

    private static void ValidateTarget(RenderTargetBinding2D target)
    {
        ArgumentNullException.ThrowIfNull(target.Texture);
        ArgumentNullException.ThrowIfNull(target.Description);
        TextureDesc desc = target.Description;
        if (desc.Width == 0 || desc.Height == 0 || desc.Width > int.MaxValue || desc.Height > int.MaxValue
            || desc.MipLevels != 1 || desc.ArrayLayers != 1 || target.Range != new TextureSubresourceRange(0, 1, 0, 1)
            || (desc.Usage & TextureUsage.RenderAttachment) == 0 || desc.Format is not (TextureFormat.Rgba8Srgb or TextureFormat.Rgba16Float))
        {
            throw new ArgumentException("The target must be a positive, single-mip, single-layer Rgba8Srgb or Rgba16Float attachment.", nameof(target));
        }

        if (!float.IsFinite(target.PixelScale) || target.PixelScale <= 0 || !IsInteger(target.PixelOrigin.X) || !IsInteger(target.PixelOrigin.Y)
            || !Enum.IsDefined(target.LoadOp) || !ValidColor(target.ClearColor))
        {
            throw new ArgumentException("The target scale, origin, load operation, and clear color must be valid.", nameof(target));
        }

        _ = checked((int)target.PixelOrigin.X + (int)desc.Width);
        _ = checked((int)target.PixelOrigin.Y + (int)desc.Height);
    }

    private static bool IsInteger(float value) => float.IsFinite(value) && value == MathF.Truncate(value) && value >= int.MinValue && (double)value <= int.MaxValue;

    private static bool ValidColor(Color2D color) => ValidChannel(color.R) && ValidChannel(color.G) && ValidChannel(color.B) && ValidChannel(color.A);

    private static bool ValidChannel(float value) => float.IsFinite(value) && value >= 0 && value <= 1;

    private static double Linear(float value) => value <= 0.04045f ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static void AppendClear(RenderGraphBuilder graph, RenderTargetBinding2D target)
    {
        if (target.LoadOp != AttachmentLoadOp.Clear)
        {
            return;
        }

        Color2D color = target.ClearColor;
        var clear = new ClearColor(Linear(color.R) * color.A, Linear(color.G) * color.A, Linear(color.B) * color.A, color.A);
        graph.AddPass("drawing2d.clear", context =>
        {
            IRenderEncoder pass = context.Commands.BeginRenderPass(new RenderPassDesc
            {
                ColorAttachments =
                [
                    new RenderColorAttachmentDesc
                    {
                        View = context.CreateTextureView(target.Texture),
                        LoadOp = AttachmentLoadOp.Clear,
                        StoreOp = AttachmentStoreOp.Store,
                        ClearValue = clear,
                    },
                ],
            });
            pass.End();
        }).UseTexture(target.Texture, TextureState.ColorAttachment, new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
    }

    private static ReadOnlyMemory<T> TakeBatch<T>(DrawList2D list, ref int index, int end)
        where T : DrawingCommand2D
    {
        IReadOnlyList<DrawingCommand2D> commands = list.Commands;
        ClipStack2D clip = commands[index].State.Clip;
        int begin = index;
        while (index < end && commands[index] is T command && ReferenceEquals(command.State.Clip, clip))
        {
            index++;
        }

        return list.GetCommands<T>(begin, index - begin);
    }

    private static int FindLayerEnd(IReadOnlyList<DrawingCommand2D> commands, int begin, int end)
    {
        int depth = 1;
        for (int i = begin + 1; i < end; i++)
        {
            if (commands[i] is BeginLayerCommand2D)
            {
                depth++;
            }
            else if (commands[i] is EndLayerCommand2D && --depth == 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException("The drawing snapshot contains an unclosed layer.");
    }

    private static EffectPixelRect Bounds(RenderTargetBinding2D target) => new((int)target.PixelOrigin.X, (int)target.PixelOrigin.Y, checked((int)target.Description.Width), checked((int)target.Description.Height));

    private static EffectPixelRect PixelBounds(Rect2D rect, Matrix3x2 transform, float pixelScale)
    {
        Vector2 a = Vector2.Transform(new Vector2(rect.X, rect.Y), transform) * pixelScale;
        Vector2 b = Vector2.Transform(new Vector2(rect.X + rect.Width, rect.Y), transform) * pixelScale;
        Vector2 c = Vector2.Transform(new Vector2(rect.X, rect.Y + rect.Height), transform) * pixelScale;
        Vector2 d = Vector2.Transform(new Vector2(rect.X + rect.Width, rect.Y + rect.Height), transform) * pixelScale;
        var minimum = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        var maximum = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        int x = checked((int)Math.Floor(minimum.X));
        int y = checked((int)Math.Floor(minimum.Y));
        int right = checked((int)Math.Ceiling(maximum.X));
        int bottom = checked((int)Math.Ceiling(maximum.Y));
        return new EffectPixelRect(x, y, checked(right - x), checked(bottom - y));
    }

    private static DrawingState2D WithClip(DrawingState2D state, Rect2D? clip)
    {
        if (clip is not Rect2D rect)
        {
            return state;
        }

        var path = new PathBuilder2D();
        path.MoveTo(new Vector2(rect.X, rect.Y));
        path.LineTo(new Vector2(rect.X + rect.Width, rect.Y));
        path.LineTo(new Vector2(rect.X + rect.Width, rect.Y + rect.Height));
        path.LineTo(new Vector2(rect.X, rect.Y + rect.Height));
        path.Close();
        return state with { Clip = state.Clip.Add(new ClipElement2D(path.Build(), state.Transform, FillRule2D.NonZero)) };
    }

    private static RenderTargetBinding2D CreateIntermediate(RenderGraphBuilder graph, EffectPixelRect bounds, float pixelScale, string name)
    {
        var description = new TextureDesc
        {
            Width = checked((uint)bounds.Width),
            Height = checked((uint)bounds.Height),
            Format = TextureFormat.Rgba16Float,
            Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment,
        };
        return new RenderTargetBinding2D
        {
            Texture = graph.CreateTexture(description, name),
            Description = description,
            PixelOrigin = new Vector2(bounds.X, bounds.Y),
            PixelScale = pixelScale,
            LoadOp = AttachmentLoadOp.Clear,
        };
    }

    private static GraphImageSource2D ImageSource(EffectImage image) => new(
        image.Texture,
        new TextureDesc
        {
            Width = checked((uint)image.Bounds.Width),
            Height = checked((uint)image.Bounds.Height),
            Format = TextureFormat.Rgba16Float,
            Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment,
        },
        image.Bounds);

    private static IEnumerable<IGraphicsTexture> GetImageAllocations(IReadOnlyList<DrawingCommand2D> commands)
    {
        var images = new HashSet<IGraphicsTexture>(ReferenceEqualityComparer.Instance);
        var nodes = new HashSet<PaintNode2D>(ReferenceEqualityComparer.Instance);
        foreach (DrawingCommand2D command in commands)
        {
            PaintGraph2D? paint = command switch
            {
                FillRectCommand2D value => value.Paint,
                FillRoundedRectCommand2D value => value.Paint,
                FillCircleCommand2D value => value.Paint,
                DrawLineCommand2D value => value.Paint,
                FillPathCommand2D value => value.Paint,
                StrokePathCommand2D value => value.Paint,
                DrawCoverageMaskCommand2D value => value.Paint,
                DrawDistanceFieldCommand2D value => value.Paint,
                DrawMeshCommand2D value => value.Paint,
                DrawPaintGraphCommand2D value => value.Graph,
                _ => null,
            };
            IGraphicsTexture? image = command switch
            {
                DrawImageCommand2D value => value.Image.View.Texture,
                DrawCoverageMaskCommand2D value => value.Mask.View.Texture,
                DrawDistanceFieldCommand2D value => value.Field.View.Texture,
                _ => null,
            };
            if (image is not null)
            {
                images.Add(image);
            }

            if (paint is not null)
            {
                AddPaintImages(paint.Root, nodes, images);
            }
        }

        return images;
    }

    private static void AddPaintImages(PaintNode2D root, HashSet<PaintNode2D> visited, HashSet<IGraphicsTexture> images)
    {
        if (!visited.Add(root))
        {
            return;
        }

        switch (root)
        {
            case ImagePaintNode2D image:
                images.Add(image.Pattern.View.Texture);
                break;
            case OpacityPaintNode2D opacity:
                AddPaintImages(opacity.Child, visited, images);
                break;
            case ClipPaintNode2D clip:
                AddPaintImages(clip.Child, visited, images);
                break;
            case TransformPaintNode2D transform:
                AddPaintImages(transform.Child, visited, images);
                break;
            case LayersPaintNode2D layers:
                foreach (PaintNode2D child in layers.Children)
                {
                    AddPaintImages(child, visited, images);
                }

                break;
            case CompositePaintNode2D composite:
                AddPaintImages(composite.Source, visited, images);
                AddPaintImages(composite.Backdrop, visited, images);
                break;
        }
    }

    private static EffectImage ApplyEffects(RenderGraphBuilder graph, EffectImage source, Func<ImageEffectSubgraph, EffectImage>? callback, PreparedDrawingPasses2D owner)
    {
        if (callback is null)
        {
            return source;
        }

        var scope = new ImageEffectSubgraph(graph, source);
        owner.Own(scope);
        EffectImage output = callback(scope);
        if (output.Texture is null || output.Bounds.IsEmpty || output.ColorSemantic != source.ColorSemantic)
        {
            throw new InvalidOperationException("The effect callback must return a finite image with the source color semantic.");
        }

        return output;
    }

    private static void AppendBatch(RenderGraphBuilder graph, RenderTargetBinding2D target, DrawingGraphBindings2D bindings, IPreparedCommandBatch2D batch)
    {
        RenderGraphPass pass = graph.AddPass("drawing2d.batch", context => batch.Record(context.Commands, context.CreateTextureView(target.Texture)))
            .UseTexture(target.Texture, TextureState.ColorAttachment, _attachmentScope);
        batch.DeclareResources(new DrawingResourceDeclarations2D(pass, target, bindings));
    }

    private async ValueTask AppendCommandsAsync(RenderGraphBuilder graph, DrawList2D list, int begin, int end, RenderTargetBinding2D target, DrawingGraphBindings2D bindings, PreparedDrawingPasses2D owner, CancellationToken cancellationToken)
    {
        IReadOnlyList<DrawingCommand2D> commands = list.Commands;
        var context = new GraphPreparationContext2D(graph.Device!, graph, bindings, target, _resources);
        int index = begin;
        while (index < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (commands[index] is BeginLayerCommand2D layer)
            {
                int layerEnd = FindLayerEnd(commands, index, end);
                await AppendLayerAsync(graph, list, index + 1, layerEnd, layer, context, owner, cancellationToken).ConfigureAwait(false);
                index = layerEnd + 1;
                continue;
            }

            if (commands[index] is DrawBackdropEffectCommand2D backdrop)
            {
                await AppendBackdropAsync(backdrop, context, owner, cancellationToken).ConfigureAwait(false);
                index++;
                continue;
            }

            IPreparedCommandBatch2D batch = commands[index] switch
            {
                FillRectCommand2D => await _renderers.FillRect.PrepareAsync(new RenderPreparationContext2D(context.Device, context.Target, context.ResourceState), TakeBatch<FillRectCommand2D>(list, ref index, end), cancellationToken).ConfigureAwait(false),
                FillRoundedRectCommand2D => await _renderers.FillRoundedRect.PrepareAsync(new RenderPreparationContext2D(context.Device, context.Target, context.ResourceState), TakeBatch<FillRoundedRectCommand2D>(list, ref index, end), cancellationToken).ConfigureAwait(false),
                FillCircleCommand2D => await _renderers.FillCircle.PrepareAsync(new RenderPreparationContext2D(context.Device, context.Target, context.ResourceState), TakeBatch<FillCircleCommand2D>(list, ref index, end), cancellationToken).ConfigureAwait(false),
                DrawLineCommand2D => await _renderers.DrawLine.PrepareAsync(new RenderPreparationContext2D(context.Device, context.Target, context.ResourceState), TakeBatch<DrawLineCommand2D>(list, ref index, end), cancellationToken).ConfigureAwait(false),
                FillPathCommand2D => await _renderers.FillPath.PrepareAsync(new RenderPreparationContext2D(context.Device, context.Target, context.ResourceState), TakeBatch<FillPathCommand2D>(list, ref index, end), cancellationToken).ConfigureAwait(false),
                StrokePathCommand2D => await _renderers.StrokePath.PrepareAsync(new RenderPreparationContext2D(context.Device, context.Target, context.ResourceState), TakeBatch<StrokePathCommand2D>(list, ref index, end), cancellationToken).ConfigureAwait(false),
                DrawImageCommand2D => await _renderers.DrawImage.PrepareAsync(new RenderPreparationContext2D(context.Device, context.Target, context.ResourceState), TakeBatch<DrawImageCommand2D>(list, ref index, end), cancellationToken).ConfigureAwait(false),
                DrawCoverageMaskCommand2D => await _renderers.DrawCoverageMask.PrepareAsync(new RenderPreparationContext2D(context.Device, context.Target, context.ResourceState), TakeBatch<DrawCoverageMaskCommand2D>(list, ref index, end), cancellationToken).ConfigureAwait(false),
                DrawDistanceFieldCommand2D => await _renderers.DrawDistanceField.PrepareAsync(new RenderPreparationContext2D(context.Device, context.Target, context.ResourceState), TakeBatch<DrawDistanceFieldCommand2D>(list, ref index, end), cancellationToken).ConfigureAwait(false),
                DrawMeshCommand2D => await _renderers.DrawMesh.PrepareAsync(new RenderPreparationContext2D(context.Device, context.Target, context.ResourceState), TakeBatch<DrawMeshCommand2D>(list, ref index, end), cancellationToken).ConfigureAwait(false),
                DrawPaintGraphCommand2D => await _renderers.DrawPaintGraph.PrepareAsync(new RenderPreparationContext2D(context.Device, context.Target, context.ResourceState), TakeBatch<DrawPaintGraphCommand2D>(list, ref index, end), cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("The drawing snapshot contains an unsupported structural command."),
            };
            ArgumentNullException.ThrowIfNull(batch);
            owner.Own(batch);
            cancellationToken.ThrowIfCancellationRequested();
            AppendBatch(graph, target, bindings, batch);
        }
    }

    private async ValueTask AppendLayerAsync(RenderGraphBuilder graph, DrawList2D list, int begin, int end, BeginLayerCommand2D layer, GraphPreparationContext2D parent, PreparedDrawingPasses2D owner, CancellationToken cancellationToken)
    {
        EffectPixelRect bounds = PixelBounds(layer.Options.ContentBounds, layer.State.Transform, parent.Target.PixelScale);
        if (layer.Options.ContentBounds.Width == 0 || layer.Options.ContentBounds.Height == 0 || bounds.Width == 0 || bounds.Height == 0)
        {
            return;
        }

        RenderTargetBinding2D target = CreateIntermediate(graph, bounds, parent.Target.PixelScale, "drawing2d.layer");
        AppendClear(graph, target);
        await AppendCommandsAsync(graph, list, begin, end, target with { LoadOp = AttachmentLoadOp.Load }, parent.Bindings, owner, cancellationToken).ConfigureAwait(false);
        EffectImage output = ApplyEffects(graph, new EffectImage(target.Texture, bounds, EffectColorSemantic.DisplayLinear), layer.Options.Effects, owner);
        if (layer.Options.Opacity != 1)
        {
            output = graph.AppendOpacity(output, layer.Options.Opacity);
        }

        IPreparedGraphCommandBatch2D composite = await DrawingGraphCompositor.PrepareAsync(parent, ImageSource(output), WithClip(layer.State, layer.Options.OutputClip), ct: cancellationToken).ConfigureAwait(false);
        owner.Own(composite);
        composite.AppendPasses(graph, parent.Target);
    }

    private async ValueTask AppendBackdropAsync(DrawBackdropEffectCommand2D command, GraphPreparationContext2D context, PreparedDrawingPasses2D owner, CancellationToken cancellationToken)
    {
        if ((context.Target.Description.Usage & TextureUsage.Sampled) == 0)
        {
            throw new ArgumentException("Backdrop effects require a target with Sampled and RenderAttachment usage.");
        }

        Rect2D output = command.Options.OutputBounds;
        EffectPixelRect outputBounds = PixelBounds(output, command.State.Transform, context.Target.PixelScale);
        if (output.Width == 0 || output.Height == 0 || outputBounds.Width == 0 || outputBounds.Height == 0)
        {
            return;
        }

        // An arbitrary callback has no inverse ROI contract. Snapshot the complete finite background once.
        EffectPixelRect captureBounds = Bounds(context.Target);
        RenderTargetBinding2D capture = CreateIntermediate(context.Graph, captureBounds, context.Target.PixelScale, "drawing2d.backdrop.snapshot");
        AppendClear(context.Graph, capture);
        var captureContext = new GraphPreparationContext2D(context.Device, context.Graph, context.Bindings, capture with { LoadOp = AttachmentLoadOp.Load }, context.ResourceState);
        var targetSource = new GraphImageSource2D(context.Target.Texture, context.Target.Description, Bounds(context.Target));
        var emptyState = new DrawingState2D(Matrix3x2.Identity, ClipStack2D.Empty);
        IPreparedGraphCommandBatch2D snapshot = await DrawingGraphCompositor.PrepareAsync(captureContext, targetSource, emptyState, replace: true, ct: cancellationToken).ConfigureAwait(false);
        owner.Own(snapshot);
        snapshot.AppendPasses(context.Graph, captureContext.Target);
        EffectImage effected = ApplyEffects(context.Graph, new EffectImage(capture.Texture, captureBounds, EffectColorSemantic.DisplayLinear), command.Options.Effects, owner);
        var original = new GraphImageSource2D(capture.Texture, capture.Description, captureBounds);
        IPreparedGraphCommandBatch2D replacement = await DrawingGraphCompositor.PrepareAsync(context, ImageSource(effected), WithClip(command.State, output), original: original, replace: true, ct: cancellationToken).ConfigureAwait(false);
        owner.Own(replacement);
        replacement.AppendPasses(context.Graph, context.Target);
    }
}
