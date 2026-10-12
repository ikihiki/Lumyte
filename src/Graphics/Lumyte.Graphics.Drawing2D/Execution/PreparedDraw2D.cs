using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Owns direct drawing batches; does not contain a render graph or compiled plan.</summary>
public sealed class PreparedDraw2D : IDisposable
{
    private readonly RenderTarget2D _target;
    private readonly IReadOnlyList<IPreparedCommandBatch2D> _batches;
    private bool _disposed;
    private bool _recorded;

    internal PreparedDraw2D(RenderTarget2D target, IReadOnlyList<IPreparedCommandBatch2D> batches)
    {
        _target = target with { };
        _batches = batches;
    }

    /// <summary>Records once into a command buffer, outside an active pass, without submitting.</summary>
    /// <param name="commands">The borrowed graphics command buffer.</param>
    public void Record(IGraphicsCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recorded)
        {
            throw new InvalidOperationException("Prepared drawing can be recorded only once.");
        }

        _recorded = true;
        if (_target.LoadOp == AttachmentLoadOp.Clear)
        {
            Color2D color = _target.ClearColor;
            IRenderEncoder pass = commands.BeginRenderPass(new()
            {
                ColorAttachments = [new() { View = _target.View, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(Linear(color.R) * color.A, Linear(color.G) * color.A, Linear(color.B) * color.A, color.A) }],
            });
            pass.End();
            var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            commands.Barrier(new TextureBarrierDesc { Texture = _target.View.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
        }

        for (int i = 0; i < _batches.Count; i++)
        {
            if (i > 0)
            {
                var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
                commands.Barrier(new TextureBarrierDesc { Texture = _target.View.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
            }

            _batches[i].Record(commands, _target.View);
        }
    }

    /// <summary>Disposes fixed batch resources after GPU completion and command disposal.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Exception>? errors = null;
        for (int i = _batches.Count - 1; i >= 0; i--)
        {
            try
            {
                _batches[i].Dispose();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("Failed to release drawing resources.", errors);
        }
    }

    private static double Linear(float value) => value <= 0.04045f ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
}
