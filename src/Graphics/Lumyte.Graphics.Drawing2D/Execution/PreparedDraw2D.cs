using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Owns direct drawing batches; does not contain a render graph or compiled plan.</summary>
public sealed class PreparedDraw2D : IDisposable
{
    private readonly RenderTarget2D _target;
    private readonly IReadOnlyList<IPreparedCommandBatch2D> _batches;
    private readonly DrawingProfileContext? _profileContext;
    private IDisposable? _imageResources;
    private bool _disposed;
    private bool _recorded;
    private List<IPreparedCommandBatch2D>? _unreleased;
    private IDisposable? _compositionUse;
    private bool _batchesDisposed;

    internal PreparedDraw2D(RenderTarget2D target, IReadOnlyList<IPreparedCommandBatch2D> batches, DrawingProfileContext? profileContext = null, IDisposable? compositionUse = null, IDisposable? imageResources = null)
    {
        _imageResources = imageResources;
        _target = target with { };
        _batches = batches;
        _profileContext = profileContext ?? default(DrawingProfileContext);
        _compositionUse = compositionUse;
    }

    internal bool ClearsTarget => _target.LoadOp == AttachmentLoadOp.Clear;

    /// <summary>Records once into a command buffer, outside an active pass, without submitting.</summary>
    /// <param name="commands">The borrowed graphics command buffer.</param>
    public void Record(IGraphicsCommandBuffer commands)
    {
        using var profile = DrawingProfileScope.Start(10, _profileContext, root: true);
        RecordCore(commands, profile);
    }

    /// <summary>Disposes fixed batch resources after GPU completion and command disposal.</summary>
    public void Dispose()
    {
        using var profile = DrawingProfileScope.Start(11, _profileContext, root: true);
        try
        {
            if (_disposed && _unreleased is null && _compositionUse is null && _imageResources is null)
            {
                profile?.Skip();
                return;
            }

            _disposed = true;
            IReadOnlyList<IPreparedCommandBatch2D> remaining = _batchesDisposed ? [] : _unreleased ?? _batches;
            List<IPreparedCommandBatch2D>? failed = null;
            List<Exception>? errors = null;
            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                try
                {
                    remaining[i].Dispose();
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                    (failed ??= []).Add(remaining[i]);
                }
            }

            failed?.Reverse();
            _unreleased = failed;
            if (errors is not null)
            {
                throw new AggregateException("Failed to release drawing resources.", errors);
            }

            _batchesDisposed = true;
            _compositionUse?.Dispose();
            _compositionUse = null;
            _imageResources?.Dispose();
            _imageResources = null;
            profile?.Success();
        }
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
    }

    internal void RetireImmutableUploads(IGraphicsSubmission submission)
    {
        foreach (IPreparedImmutableUploads2D batch in _batches.OfType<IPreparedImmutableUploads2D>())
        {
            batch.RetireImmutableUploads(submission);
        }
    }

    internal void DeclareGraphResources(IDrawingResourceDeclarations2D declarations, IGraphicsTexture texture)
    {
        if (!ReferenceEquals(texture, _target.View.Texture))
        {
            throw new ArgumentException("The graph target must be the prepared frame's borrowed allocation.", nameof(texture));
        }

        foreach (IPreparedCommandBatch2D batch in _batches)
        {
            batch.DeclareResources(declarations);
        }
    }

    internal void RecordCore(IGraphicsCommandBuffer commands, DrawingProfileScope? profile)
    {
        long resident = 0;
        try
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
                SRgba<float> color = _target.ClearColor;
                LRgba<double> linear = color.ToLinear<double>().PremultiplyLinear<double>();
                IRenderEncoder pass = commands.BeginRenderPass(new()
                {
                    ColorAttachments = [new() { View = _target.View, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(linear.R, linear.G, linear.B, linear.A) }],
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

                if (_batches[i] is RetainedDrawingCache2D.Asset
                    { Resident: true })
                {
                    resident++;
                }

                _batches[i].Record(commands, _target.View);
            }

            profile?.Success();
        }
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
        finally
        {
            profile?.Counter(14, resident);
        }
    }
}
