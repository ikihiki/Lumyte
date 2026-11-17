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

    // A deliberately exact subset: the inverse is identity with integer translation.
    // Flattening/stroke expansion stays in local coordinates at unchanged linear scale;
    // transformed paint, clip masks and DPR-dependent geometry are not rebased.
    internal static bool CanTranslateResident(DrawingSceneEntry2D entry, DrawingTargetInfo2D target, IReadOnlyList<DrawList2D>? pages = null)
    {
        Matrix3x2 t = entry.Transform;
        if (target.PixelScale != 1 || target.PixelOrigin != Vector2.Zero || entry.Clips.Count != 0
            || t.M11 != 1 || t.M12 != 0 || t.M21 != 0 || t.M22 != 1
            || MathF.Abs(t.M31) > 8192 || MathF.Abs(t.M32) > 8192
            || t.M31 != MathF.Truncate(t.M31) || t.M32 != MathF.Truncate(t.M32)
            || target.Description.Width > 8192 || target.Description.Height > 8192)
        {
            return false;
        }

        foreach (DrawList2D page in pages ?? entry.Content.RetainedPages.Pages)
        {
            foreach (DrawingCommand2D command in page.Commands)
            {
                if (command.State.Transform != Matrix3x2.Identity || command.State.Clip.Elements.Count != 0)
                {
                    return false;
                }

                PaintGraph2D? paint = command switch
                {
                    FillRectCommand2D value => value.Paint,
                    FillRoundedRectCommand2D value => value.Paint,
                    FillCircleCommand2D value => value.Paint,
                    FillPathCommand2D value => value.Paint,
                    StrokePathCommand2D value => value.Paint,
                    DrawLineCommand2D value => value.Paint,
                    _ => null,
                };

                // Local geometry and subdivision depend on the linear transform,
                // not integer translation. Preserve the complete canonical quad.
                if (paint?.Root is not SolidPaintNode2D || DrawingBounds2D.LocalBounds(command) is not { } bounds
                    || bounds.X < 1 || bounds.Y < 1
                    || bounds.X + bounds.Width > target.Description.Width - 1
                    || bounds.Y + bounds.Height > target.Description.Height - 1)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static DrawList2D TransformPages(IReadOnlyList<DrawList2D> pages, Matrix3x2 transform, IReadOnlyList<DrawingSceneClip2D> parentClips, int firstCommand = 0, int commandCount = int.MaxValue)
    {
        var clips = new Dictionary<ClipStack2D, ClipStack2D>(ReferenceEqualityComparer.Instance);
        var result = new List<DrawingCommand2D>();
        int order = 0;
        foreach (DrawList2D page in pages)
        {
            foreach (DrawingCommand2D command in page.Commands)
            {
                if (order++ < firstCommand)
                {
                    continue;
                }

                if (result.Count == commandCount)
                {
                    return new(result);
                }

                if (!clips.TryGetValue(command.State.Clip, out ClipStack2D? clip))
                {
                    var elements = parentClips.Select(c => new ClipElement2D(c.Path, c.Transform, c.Rule)).ToList();
                    elements.AddRange(command.State.Clip.Elements.Select(c => c with { Transform = c.Transform * transform }));
                    clip = new(elements);
                    clips.Add(command.State.Clip, clip);
                }

                if (transform == Matrix3x2.Identity && parentClips.Count == 0)
                {
                    result.Add(command);
                    continue;
                }

                Matrix3x2 combined = command.State.Transform * transform;
                ModelValidation2D.Matrix(combined, nameof(transform));
                result.Add(command with { State = new(combined, clip) });
            }
        }

        return new(result);
    }

    private static void SetInstance(RetainedDrawingCache2D.Asset asset, DrawingSceneEntry2D entry, Vector2 translation)
    {
        asset.Node = entry.Node;
        asset.ContentRevision = entry.ContentRevision;
        asset.PropertyRevision = entry.PropertyRevision;
        asset.Translation = translation;
    }

    private async ValueTask<PreparedDrawingFrame2D> PrepareRetainedAsync(IDrawingSceneSource2D source, RenderTarget2D target, CancellationToken cancellationToken)
    {
        using var profile = DrawingProfileScope.Start(2, root: true);
        long groups = 0;
        long reused = 0;
        long compiled = 0;
        try
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
                long captureBytes = _retained.ResidentInstancesEnabled
                    ? ResidentSceneBudget2D.ValidateCapture(source, _retained.MaxWorkingBytes, cancellationToken) : 0;
                _retained.BindStorageOwner(_resources);
                frame = _retained.AcquireFrame();
                RetainedDrawingCache2D.AssetList assets = frame.Assets;
                var plan = new DrawingArgumentPlan2D(_device) { ArgumentTableMode = ArgumentTableMode };
                using IDisposable planOwner = _resources.CleanupOwner(plan);
                var context = new RenderPreparationContext2D(_device, info, _resources, plan) { EnforceWorkingBudget = _retained.ResidentInstancesEnabled, MaxWorkingBytes = _retained.MaxWorkingBytes };

                // Capture only immutable page/property values. The source and its mutable lease
                // are never captured by the returned frame or by cached GPU assets.
                foreach (DrawingSceneEntry2D entry in source.Entries)
                {
                    if (entry.Content.RetainedPages.Count != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Four canonical CPU pages form one independently owned invalidation chunk.
                        // This is not a 64-command draw split: compatible records still batch across
                        // page boundaries, and backend record expansion/capacity governs GPU draws.
                        int pagesPerChunk = _retained.ResidentInstancesEnabled ? 4 : int.MaxValue;
                        for (int pageIndex = 0; pageIndex < entry.Content.RetainedPages.PageCount;)
                        {
                            DrawList2D[] pages = entry.Content.RetainedPages.Pages.Skip(pageIndex).Take(pagesPerChunk).ToArray();
                            pageIndex += pages.Length;
                            int firstCommand = 0;
                            int commandCount = pages.Sum(page => page.Commands.Count);
                            Vector2 translation = default;
                            Matrix3x2 payloadTransform = entry.Transform;
                            if (_retained.ResidentInstancesEnabled && pages.All(page => page.Commands.All(IsImmutableTranslationRenderer)) && CanTranslateResident(entry, info, pages) && context.RecordResources() is not null)
                            {
                                translation = entry.Transform.Translation;
                                payloadTransform = Matrix3x2.Identity;
                            }

                            var key = new RetainedDrawingCache2D.Key(pages, payloadTransform, entry.Clips.ToArray(), info.Description.Width, info.Description.Height, info.Description.Format, info.PixelScale, firstCommand, commandCount);
                            groups++;
                            RetainedDrawingCache2D.Asset? cached = _retained.Take(key, frame);
                            if (cached is not null)
                            {
                                reused++;
                                SetInstance(cached, entry, translation);
                                try
                                {
                                    if (_retained.ResidentInstancesEnabled && checked(captureBytes + DrawingWorkingBudget2D.OwnedBytes(assets.Select(asset => asset.Batch).Append(cached.Batch))) > _retained.MaxWorkingBytes)
                                    {
                                        throw new NotSupportedException("Retained frame working set exceeds MaxWorkingBytes.");
                                    }

                                    assets.Add(cached);
                                }
                                catch
                                {
                                    _retained.QueueCleanup(cached.Batch);
                                    throw;
                                }

                                continue;
                            }

                            DrawList2D transformed;
                            using (var transform = DrawingProfileScope.Start(3))
                            {
                                try
                                {
                                    transformed = TransformPages(pages, payloadTransform, entry.Clips, firstCommand, commandCount);
                                    transform?.Success();
                                }
                                catch (Exception error)
                                {
                                    transform?.Failure(error);
                                    throw;
                                }
                            }

                            if (_retained.ResidentInstancesEnabled)
                            {
                                DrawingWorkingBudget2D.Validate(transformed, info, _retained.MaxWorkingBytes - captureBytes - DrawingWorkingBudget2D.OwnedBytes(assets.Select(asset => asset.Batch)));
                            }

                            var batches = new List<IPreparedCommandBatch2D>();
                            IPreparedCommandBatch2D? unpublishedPayload = null;
                            IDisposable? compositionUse = null;
                            try
                            {
                                compositionUse = _resources.RetainComposition();
                                int index = 0;
                                using (var build = DrawingProfileScope.Start(4))
                                {
                                    try
                                    {
                                        while (index < transformed.Commands.Count)
                                        {
                                            context.OtherWorkingBytes = checked(captureBytes + DrawingWorkingBudget2D.OwnedBytes(assets.Select(asset => asset.Batch).Concat(batches), context.ArgumentPlan));
                                            if (IsBuiltinRenderer(transformed.Commands[index]) && context.RecordResources() is not null)
                                            {
                                                batches.Add(await PreparedRecordBatch2D.PrepareAsync(context, TakeBuiltinBatch(transformed, ref index, transformed.Commands.Count), cancellationToken).ConfigureAwait(false));
                                                continue;
                                            }

                                            batches.Add(await PrepareRetainedBatchAsync(context, transformed, index, cancellationToken).ConfigureAwait(false));

                                            // Match the default contiguous type/clip batching contract.
                                            DrawingCommand2D command = transformed.Commands[index++];
                                            while (index < transformed.Commands.Count && transformed.Commands[index].GetType() == command.GetType() && ReferenceEquals(transformed.Commands[index].State.Clip, command.State.Clip))
                                            {
                                                index++;
                                            }
                                        }

                                        build?.Success();
                                    }
                                    catch (Exception error)
                                    {
                                        build?.Failure(error);
                                        throw;
                                    }
                                    finally
                                    {
                                        build?.Counter(5, batches.Count);
                                    }
                                }

                                _retained.CountCompilation();
                                compiled++;
                                var run = new RetainedBatchRun2D(batches, transformed.Commands.All(IsImmutableTranslationRenderer), compositionUse);
                                compositionUse = null;
                                unpublishedPayload = _retained.ResidentInstancesEnabled && run.Shareable ? new ResidentBatchLease2D(run) : run;
                                var asset = new RetainedDrawingCache2D.Asset(key, unpublishedPayload);
                                SetInstance(asset, entry, translation);
                                assets.Add(asset);
                            }
                            catch
                            {
                                if (unpublishedPayload is not null)
                                {
                                    _retained.QueueCleanup(unpublishedPayload);
                                }
                                else if (batches.Count != 0 || compositionUse is not null)
                                {
                                    _retained.QueueCleanup(new RetainedBatchRun2D(batches, false, compositionUse));
                                }

                                throw;
                            }
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                await plan.SealAsync(cancellationToken).ConfigureAwait(false);

                if (_retained.ResidentInstancesEnabled && checked(captureBytes + DrawingWorkingBudget2D.OwnedBytes(assets.Select(asset => asset.Batch))) > _retained.MaxWorkingBytes)
                {
                    throw new NotSupportedException("Retained frame working set exceeds MaxWorkingBytes.");
                }

                planOwner.Dispose();
                _retained.AdmitFrame(frame);

                // Re-read the guard before returning: disposed sources cannot publish a frame.
                _ = source.Revision;
                RetainedDrawingCache2D cache = _retained;
                RetainedDrawingCache2D.FrameLease ownedFrame = frame;
                profile?.Success();
                return new(_device, target, assets, completed => cache.ReleaseFrame(ownedFrame, completed), profile?.Context, cache);
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
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
        finally
        {
            profile?.Counter(2, groups);
            profile?.Counter(3, reused);
            profile?.Counter(4, compiled);
            if (profile is { CountersEnabled: true })
            {
                profile.Snapshot(_retained.Statistics);
            }
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

    private bool IsImmutableTranslationRenderer(DrawingCommand2D command)
    {
        if (IsBuiltinRenderer(command))
        {
            return true;
        }

        object? renderer = command switch
        {
            FillRectCommand2D => _renderers.FillRect,
            FillRoundedRectCommand2D => _renderers.FillRoundedRect,
            FillCircleCommand2D => _renderers.FillCircle,
            DrawLineCommand2D => _renderers.DrawLine,
            FillPathCommand2D => _renderers.FillPath,
            StrokePathCommand2D => _renderers.StrokePath,
            DrawImageCommand2D => _renderers.DrawImage,
            DrawCoverageMaskCommand2D => _renderers.DrawCoverageMask,
            DrawDistanceFieldCommand2D => _renderers.DrawDistanceField,
            DrawPaintGraphCommand2D => _renderers.DrawPaintGraph,
            _ => null,
        };

        // A mixed custom/legacy alternative is not proven to consume translation.
        // Restrict optimization to a closed set of our own Mesh record processors.
        return renderer is Pipelines.ICommandChoice2D choice && choice.CandidateProcessors.Count != 0
            && choice.CandidateProcessors.All(candidate => candidate is Pipelines.MeshCoverageProcessor2D);
    }

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
