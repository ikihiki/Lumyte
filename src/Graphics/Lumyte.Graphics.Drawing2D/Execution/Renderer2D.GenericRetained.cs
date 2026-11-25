using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Preserves retained frame retirement while preparing fresh generic writable scratch.</summary>
public sealed partial class Renderer2D
{
    private async ValueTask<PreparedDrawingFrame2D> PrepareGenericRetainedAsync(IDrawingSceneSource2D source, RenderTarget2D target, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(target.View);
        PipelineBinding2D binding = _pipelineBinding!;
        if (binding.RequiresReadback)
        {
            throw new NotSupportedException("Retained synchronous recording cannot contain readback intervals.");
        }

        ValidateView(target.View);
        var info = new DrawingTargetInfo2D { Description = Describe(target.View.Texture), PixelScale = target.PixelScale, LoadOp = AttachmentLoadOp.Load };
        ValidateTarget(info with { LoadOp = target.LoadOp, ClearColor = target.ClearColor });
        token.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _preparing, 1, 0) != 0)
        {
            throw new InvalidOperationException("Concurrent preparation using one renderer is not supported.");
        }

        RetainedDrawingCache2D.FrameLease? frame = null;
        IPreparedCommandBatch2D? unpublished = null;
        IDisposable? composition = null;
        IDisposable? serial = null;
        IDisposable? imageResources = null;
        try
        {
            serial = await binding.AcquirePreparationAsync(token).ConfigureAwait(false);

            // Admit transform/clip capture before generic request materialization.
            long captureBytes = ResidentSceneBudget2D.ValidateCapture(source, _retained.MaxWorkingBytes, token);
            imageResources = ImageResourceRegistry2D.Retain(source.Entries.Select(entry => entry.Content));
            _retained.BindStorageOwner(_resources);
            frame = _retained.AcquireFrame();
            Vector2 translation = default;
            if (_retained.ResidentInstancesEnabled && source.Entries.Count != 0)
            {
                DrawingSceneEntry2D first = source.Entries[0];
                Vector2 candidate = new(first.Transform.M31, first.Transform.M32);
                if (source.Entries.All(entry => CanTranslateResident(entry, info) && entry.Transform.M31 == candidate.X && entry.Transform.M32 == candidate.Y))
                {
                    translation = candidate;
                }
            }

            var commands = new List<DrawingCommand2D>();
            foreach (DrawingSceneEntry2D entry in source.Entries)
            {
                token.ThrowIfCancellationRequested();
                DrawList2D transformed = TransformPages(entry.Content.RetainedPages.Pages, entry.Transform * Matrix3x2.CreateTranslation(-translation), entry.Clips);
                commands.AddRange(transformed.Commands);
            }

            binding.ValidateCommands(commands);
            var list = new DrawList2D(commands);
            var arguments = new DrawingArgumentPlan2D(_device) { ArgumentTableMode = ArgumentTableMode };
            using IDisposable argumentOwner = _resources.CleanupOwner(arguments);
            var context = new RenderPreparationContext2D(_device, info, _resources, arguments)
            {
                MaxWorkingBytes = _retained.MaxWorkingBytes,
                OtherWorkingBytes = checked(captureBytes + arguments.EstimatedBytes),
                EnforceWorkingBudget = true,
                RetainedTranslation = translation,
                SourceBinding = new RenderSourceBinding2D(_device, target.View, binding.RecordContext),
            };
            composition = _resources.RetainComposition();
            if (commands.Count != 0)
            {
                unpublished = await binding.PrepareAsync(context, list.Commands, token).ConfigureAwait(false);
                var key = new RetainedDrawingCache2D.Key([list], Matrix3x2.Identity, [], info.Description.Width, info.Description.Height, info.Description.Format, info.PixelScale);
                var run = new RetainedBatchRun2D([unpublished], false, composition);
                frame.Assets.Add(new RetainedDrawingCache2D.Asset(key, run));
                unpublished = null;
                composition = null;
                _retained.CountCompilation();
            }

            if (commands.Count == 0)
            {
                composition?.Dispose();
                composition = null;
            }

            await arguments.SealAsync(token).ConfigureAwait(false);
            argumentOwner.Dispose();
            token.ThrowIfCancellationRequested();
            _ = source.Revision;
            _retained.AdmitFrame(frame);
            RetainedDrawingCache2D.FrameLease accepted = frame;
            var prepared = new PreparedDrawingFrame2D(_device, target, frame.Assets, completed => _retained.ReleaseFrame(accepted, completed), cache: _retained, imageResources: imageResources);
            imageResources = null;
            return prepared;
        }
        catch (Exception primary)
        {
            if (unpublished is not null || composition is not null)
            {
                _retained.QueueCleanup(new RetainedBatchRun2D(unpublished is null ? [] : [unpublished], false, composition));
                composition = null;
            }

            if (frame is not null)
            {
                try
                {
                    _retained.ReleaseFrame(frame, false);
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(primary, cleanup);
                }
            }

            throw;
        }
        finally
        {
            composition?.Dispose();
            imageResources?.Dispose();
            serial?.Dispose();
            Volatile.Write(ref _preparing, 0);
        }
    }
}
