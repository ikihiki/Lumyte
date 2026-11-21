using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Mathematics.Colors;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed partial class PipelineBinding2D
{
    internal async ValueTask<PreparedPipelineExecution2D> PrepareExecutionAsync(RenderPreparationContext2D context, IReadOnlyList<DrawingCommand2D> commands, RenderTarget2D target, CancellationToken token)
    {
        _routes.Validate(commands);
        context.ValidateResources();
        var owners = new PipelineOwnerGroup2D();
        try
        {
            owners.Add(context.RetainResources());
            IDisposable? targetUse = context.SourceBinding?.AcquireTargetUse();
            if (targetUse is not null)
            {
                owners.Add(targetUse);
            }

            PipelineSnapshotBudget2D.Use snapshot = _snapshots.Acquire();
            owners.Add(snapshot);
            PipelineResult<Device<TargetWritesV1>> result = await _pipeline.PrepareAsync(new RenderRequest2D(context, commands, snapshot.Reserve), token).ConfigureAwait(false);
            owners.Add(result);
            result.AddRecordValidation(context.ValidateResources);
            if (target.LoadOp == AttachmentLoadOp.Clear)
            {
                LRgba<double> linear = target.ClearColor.ToLinear<double>().PremultiplyLinear<double>();
                result.AddRecordPrelude(record =>
                {
                    IRenderEncoder pass = record.Commands.BeginRenderPass(new RenderPassDesc
                    {
                        ColorAttachments = [new RenderColorAttachmentDesc { View = target.View, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(linear.R, linear.G, linear.B, linear.A) }],
                    });
                    pass.End();
                    var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
                    record.Commands.Barrier(new TextureBarrierDesc { Texture = target.View.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
                });
            }

            GraphicsPipelineExecution execution = result.AcquireExecutionUnbound(_pipeline);
            owners.Add(execution);
            return new PreparedPipelineExecution2D(execution, owners);
        }
        catch (Exception error)
        {
            context.CleanupAfterFailure(owners, error);
            throw;
        }
    }
}
