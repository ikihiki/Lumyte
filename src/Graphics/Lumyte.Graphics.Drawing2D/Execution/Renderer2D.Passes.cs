using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Provides descriptor-only graph preparation without a target view.</summary>
public sealed partial class Renderer2D : IDrawingPassRenderer2D
{
    /// <inheritdoc/>
    ValueTask<DrawingPassPlan2D> IDrawingPassRenderer2D.PreparePassesAsync(DrawList2D content, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken)
        => PreparePassesAsync(content, target, argumentPlan, cancellationToken);

    /// <inheritdoc/>
    ValueTask<DrawingPassPlan2D> IDrawingPassRenderer2D.PreparePassesAsync(DrawingSceneReadLease2D source, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken)
        => PrepareScenePassesAsync(source, target, argumentPlan, cancellationToken);

    /// <inheritdoc/>
    ValueTask<DrawingPassPlan2D> IDrawingPassRenderer2D.PreparePassesAsync(DrawingSnapshot2D source, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken)
        => PrepareScenePassesAsync(source, target, argumentPlan, cancellationToken);

    private async ValueTask<DrawingPassPlan2D> PrepareScenePassesAsync(IDrawingSceneSource2D source, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var commands = new List<DrawingCommand2D>();
        foreach (DrawingSceneEntry2D entry in source.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrawList2D transformed = TransformPages(entry.Content.RetainedPages.Pages.ToArray(), entry.Transform, entry.Clips);
            commands.AddRange(transformed.Commands);
        }

        _ = source.Revision;
        DrawingPassPlan2D plan = await PreparePassesAsync(new DrawList2D(commands), target, argumentPlan, cancellationToken).ConfigureAwait(false);
        try
        {
            _ = source.Revision;
            return plan;
        }
        catch
        {
            plan.Dispose();
            throw;
        }
    }

    private async ValueTask<DrawingPassPlan2D> PreparePassesAsync(DrawList2D content, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(argumentPlan);
        ValidateTarget(target);
        if (_computeShapeMembership)
        {
            DrawingWorkingBudget2D.Validate(content, target, _options.MaxWorkingBytes);
        }

        if (!ReferenceEquals(argumentPlan.Device, _device))
        {
            throw new ArgumentException("The argument plan belongs to another device.", nameof(argumentPlan));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _preparing, 1, 0) != 0)
        {
            throw new InvalidOperationException("Concurrent preparation using one Renderer2D is not supported.");
        }

        var result = new DrawingPassPlan2D(argumentPlan);
        try
        {
            result.Own(_resources.Retain());
            var context = new RenderPreparationContext2D(_device, target with { LoadOp = AttachmentLoadOp.Load }, _resources, argumentPlan)
            {
                ComputeShapeMembership = _computeShapeMembership,
                MaxWorkingBytes = _options.MaxWorkingBytes,
            };
            int index = 0;
            long workingBytes = 0;
            while (index < content.Commands.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int firstReservation = argumentPlan.Reservations.Count;
                IPreparedCommandBatch2D batch;
                if (IsBuiltinRenderer(content.Commands[index]) && context.RecordResources() is not null)
                {
                    batch = await PreparedRecordBatch2D.PrepareAsync(context, TakeBuiltinBatch(content, ref index, content.Commands.Count), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    batch = await PrepareRetainedBatchAsync(context, content, index, cancellationToken).ConfigureAwait(false);
                    DrawingCommand2D command = content.Commands[index++];
                    while (index < content.Commands.Count && content.Commands[index].GetType() == command.GetType() && ReferenceEquals(content.Commands[index].State.Clip, command.State.Clip))
                    {
                        index++;
                    }
                }

                result.Own(batch);
                if (batch is IRetainedCommandBatch2D memory)
                {
                    workingBytes = checked(workingBytes + memory.EstimatedBytes);
                    if (_computeShapeMembership && workingBytes > _options.MaxWorkingBytes)
                    {
                        throw new NotSupportedException("Standard graph preparation exceeds MaxWorkingBytes.");
                    }
                }

                if (batch is PreparedRecordBatch2D records)
                {
                    foreach (ShapeMembershipPassPage2D page in records.GraphPassPages)
                    {
                        result.Add(new("drawing2d.copy", (commands, _) => page.Copy(commands), page.DeclareCopy, [], UsesTarget: false, LocalResources: true));
                        if (page.HasCompute)
                        {
                            result.Add(new("drawing2d.shape-membership", (commands, _) => page.Compute(commands), page.DeclareCompute, [], UsesTarget: false, LocalResources: true));
                        }

                        result.Add(new("drawing2d.raster", page.Raster.Record, page.Raster.DeclareResources, page.RasterReservations, LocalResources: true));
                    }
                }
                else
                {
                    result.Add(new("drawing2d.raster", batch.Record, batch.DeclareResources, argumentPlan.Reservations.Skip(firstReservation).ToArray()));
                }
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
        finally
        {
            Volatile.Write(ref _preparing, 0);
        }
    }
}
