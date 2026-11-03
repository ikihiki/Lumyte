using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Prepares immutable retained drawing assets and completion-safe frames.</summary>
public sealed partial class Renderer2D
{
    /// <summary>Gets cumulative issued cache work and current idle memory bounds; these are not driver-wide counters.</summary>
    public DrawingRetentionStatistics2D RetentionStatistics => _retained.Statistics;

    /// <summary>Prepares retained work while borrowing a live scene guard only until this call completes.</summary>
    /// <param name="source">The caller-owned guard, kept alive across the await.</param>
    /// <param name="target">The caller-owned frame target.</param>
    /// <param name="cancellationToken">Cancels preparation without publishing residency.</param>
    /// <returns>A one-shot completion-safe frame.</returns>
    public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source, RenderTarget2D target, CancellationToken cancellationToken = default)
        => PrepareRetainedAsync(source, target, cancellationToken);

    /// <summary>Prepares retained work from an explicit immutable snapshot.</summary>
    /// <param name="source">The caller-owned snapshot, kept alive across the await.</param>
    /// <param name="target">The caller-owned frame target.</param>
    /// <param name="cancellationToken">Cancels preparation without publishing residency.</param>
    /// <returns>A one-shot completion-safe frame.</returns>
    public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source, RenderTarget2D target, CancellationToken cancellationToken = default)
        => PrepareRetainedAsync(source, target, cancellationToken);

    private static DrawList2D TransformPages(IReadOnlyList<DrawList2D> pages, Matrix3x2 transform, IReadOnlyList<DrawingSceneClip2D> parentClips)
    {
        var clips = new Dictionary<ClipStack2D, ClipStack2D>(ReferenceEqualityComparer.Instance);
        var result = new List<DrawingCommand2D>();
        foreach (DrawList2D page in pages)
        {
            foreach (DrawingCommand2D command in page.Commands)
            {
                if (!clips.TryGetValue(command.State.Clip, out ClipStack2D? clip))
                {
                    var elements = parentClips.Select(c => new ClipElement2D(c.Path, c.Transform, c.Rule)).ToList();
                    elements.AddRange(command.State.Clip.Elements.Select(c => c with { Transform = c.Transform * transform }));
                    clip = new(elements);
                    clips.Add(command.State.Clip, clip);
                }

                Matrix3x2 combined = command.State.Transform * transform;
                ModelValidation2D.Matrix(combined, nameof(transform));
                result.Add(command with { State = new(combined, clip) });
            }
        }

        return new(result);
    }

    private async ValueTask<PreparedDrawingFrame2D> PrepareRetainedAsync(IDrawingSceneSource2D source, RenderTarget2D target, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(target.View);
        ValidateView(target.View);
        var info = new DrawingTargetInfo2D { Description = Describe(target.View.Texture), PixelScale = target.PixelScale, LoadOp = AttachmentLoadOp.Load };
        ValidateTarget(info with { LoadOp = target.LoadOp, ClearColor = target.ClearColor });
        cancellationToken.ThrowIfCancellationRequested();
        if (_device.Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("Retained preparation requires a ready device.");
        }

        if (Interlocked.CompareExchange(ref _preparing, 1, 0) != 0)
        {
            throw new InvalidOperationException("Concurrent preparation using one Renderer2D is not supported.");
        }

        RetainedDrawingCache2D.FrameLease? frame = null;
        try
        {
            frame = _retained.AcquireFrame();
            List<RetainedDrawingCache2D.Asset> assets = frame.Assets;
            var context = new RenderPreparationContext2D(_device, info, _resources);

            // Capture only immutable page/property values. The source and its mutable lease
            // are never captured by the returned frame or by cached GPU assets.
            foreach (DrawingSceneEntry2D entry in source.Entries)
            {
                for (int first = 0; first < entry.Content.RetainedPages.Pages.Count; first += 16)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DrawList2D[] pages = entry.Content.RetainedPages.Pages.Skip(first).Take(16).ToArray();
                    var key = new RetainedDrawingCache2D.Key(pages, entry.Transform, entry.Clips.ToArray(), info.Description.Width, info.Description.Height, info.Description.Format, info.PixelScale);
                    RetainedDrawingCache2D.Asset? cached = _retained.Take(key);
                    if (cached is not null)
                    {
                        assets.Add(cached);
                        continue;
                    }

                    DrawList2D transformed = TransformPages(pages, entry.Transform, entry.Clips);
                    var batches = new List<IPreparedCommandBatch2D>();
                    try
                    {
                        int index = 0;
                        while (index < transformed.Commands.Count)
                        {
                            batches.Add(await PrepareRetainedBatchAsync(context, transformed, index, cancellationToken).ConfigureAwait(false));

                            // Match the default contiguous type/clip batching contract.
                            DrawingCommand2D command = transformed.Commands[index++];
                            while (index < transformed.Commands.Count && transformed.Commands[index].GetType() == command.GetType() && ReferenceEquals(transformed.Commands[index].State.Clip, command.State.Clip))
                            {
                                index++;
                            }
                        }

                        _retained.CountCompilation();
                        assets.Add(new(key, new RetainedBatchRun2D(batches, transformed.Commands.All(IsBuiltinRenderer))));
                    }
                    catch
                    {
                        if (batches.Count != 0)
                        {
                            _retained.QueueCleanup(new RetainedBatchRun2D(batches, false));
                        }

                        throw;
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Re-read the guard before returning: disposed sources cannot publish a frame.
            _ = source.Revision;
            RetainedDrawingCache2D cache = _retained;
            RetainedDrawingCache2D.FrameLease ownedFrame = frame;
            return new(_device, target, assets, completed => cache.ReleaseFrame(ownedFrame, completed));
        }
        catch (Exception preparationFailure)
        {
            if (frame is not null)
            {
                try
                {
                    _retained.ReleaseFrame(frame, false);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("Retained preparation and cleanup both failed; pending resources remain cache-owned.", preparationFailure, cleanupFailure);
                }
            }

            throw;
        }
        finally
        {
            Volatile.Write(ref _preparing, 0);
        }
    }

    private async ValueTask<IPreparedCommandBatch2D> PrepareRetainedBatchAsync(RenderPreparationContext2D context, DrawList2D list, int index, CancellationToken cancellationToken)
        => list.Commands[index] switch
        {
            FillRectCommand2D => await _renderers.FillRect.PrepareAsync(context, TakeBatch<FillRectCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
            FillRoundedRectCommand2D => await _renderers.FillRoundedRect.PrepareAsync(context, TakeBatch<FillRoundedRectCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
            FillCircleCommand2D => await _renderers.FillCircle.PrepareAsync(context, TakeBatch<FillCircleCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
            DrawLineCommand2D => await _renderers.DrawLine.PrepareAsync(context, TakeBatch<DrawLineCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
            FillPathCommand2D => await _renderers.FillPath.PrepareAsync(context, TakeBatch<FillPathCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
            StrokePathCommand2D => await _renderers.StrokePath.PrepareAsync(context, TakeBatch<StrokePathCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
            DrawImageCommand2D => await _renderers.DrawImage.PrepareAsync(context, TakeBatch<DrawImageCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
            DrawCoverageMaskCommand2D => await _renderers.DrawCoverageMask.PrepareAsync(context, TakeBatch<DrawCoverageMaskCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
            DrawDistanceFieldCommand2D => await _renderers.DrawDistanceField.PrepareAsync(context, TakeBatch<DrawDistanceFieldCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
            DrawMeshCommand2D => await _renderers.DrawMesh.PrepareAsync(context, TakeBatch<DrawMeshCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
            DrawPaintGraphCommand2D => await _renderers.DrawPaintGraph.PrepareAsync(context, TakeBatch<DrawPaintGraphCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException("Layer and effect commands require RenderGraphCanvas2D."),
        };

    private bool IsBuiltinRenderer(DrawingCommand2D command) => command switch
    {
        FillRectCommand2D => _renderers.FillRect is DefaultFillRectRenderer2D,
        FillRoundedRectCommand2D => _renderers.FillRoundedRect is DefaultFillRoundedRectRenderer2D,
        FillCircleCommand2D => _renderers.FillCircle is DefaultFillCircleRenderer2D,
        DrawLineCommand2D => _renderers.DrawLine is DefaultDrawLineRenderer2D,
        FillPathCommand2D => _renderers.FillPath is DefaultFillPathRenderer2D,
        StrokePathCommand2D => _renderers.StrokePath is DefaultStrokePathRenderer2D,
        DrawImageCommand2D => _renderers.DrawImage is DefaultDrawImageRenderer2D,
        DrawCoverageMaskCommand2D => _renderers.DrawCoverageMask is DefaultDrawCoverageMaskRenderer2D,
        DrawDistanceFieldCommand2D => _renderers.DrawDistanceField is DefaultDrawDistanceFieldRenderer2D,
        DrawMeshCommand2D => _renderers.DrawMesh is DefaultDrawMeshRenderer2D,
        DrawPaintGraphCommand2D => _renderers.DrawPaintGraph is DefaultDrawPaintGraphRenderer2D,
        _ => false,
    };
}
