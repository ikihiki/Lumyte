using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Records graph shader operations within one callback-scoped compute pass.</summary>
public sealed class RenderGraphComputeEncoder : IDisposable
{
    private readonly RenderGraphContext _context;
    private readonly IComputeEncoder _native;
    private IGraphicsComputePipeline? _pipeline;
    private bool _ended;

    internal RenderGraphComputeEncoder(RenderGraphContext context, IComputeEncoder native)
    {
        _context = context;
        _native = native;
    }

    /// <summary>Selects the compute pipeline.</summary>
    /// <param name="pipeline">The pipeline value.</param>
    public void SetPipeline(IGraphicsComputePipeline pipeline)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(pipeline);
        _native.SetPipeline(pipeline);
        _pipeline = pipeline;
    }

    /// <summary>Validates logical resources, snapshots current values and records Dispatch.</summary>
    /// <param name="resources">The shader inputs declared for this graph pass.</param>
    /// <param name="groupCountX">The groupCountX value.</param>
    /// <param name="groupCountY">The groupCountY value.</param>
    /// <param name="groupCountZ">The groupCountZ value.</param>
    public void Dispatch(RenderGraphShaderResources resources, uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        Validate(resources);
        resources.Core.Dispatch(_native, resources.Snapshot(), groupCountX, groupCountY, groupCountZ);
    }

    /// <summary>Validates logical resources, snapshots current values and records DispatchIndirect.</summary>
    /// <param name="resources">The shader inputs declared for this graph pass.</param>
    /// <param name="arguments">The arguments value.</param>
    public void DispatchIndirect(RenderGraphShaderResources resources, BufferSlice<DispatchIndirectArguments> arguments)
    {
        Validate(resources);
        resources.Core.DispatchIndirect(_native, resources.Snapshot(), arguments);
    }

    /// <summary>Ends this pass once without submitting or releasing GPU resources.</summary>
    public void End()
    {
        EnsureActive();
        _ended = true;
        try
        {
            _native.End();
        }
        finally
        {
            _context.EncoderEnded(this);
        }
    }

    /// <summary>Ends this pass if it has not already ended.</summary>
    public void Dispose()
    {
        if (!_ended)
        {
            try
            {
                End();
            }
            catch (Exception cleanupError)
            {
                _context.DeferEncoderCleanup(cleanupError);
            }
        }
    }

    private void Validate(RenderGraphShaderResources resources)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(resources);
        IGraphicsComputePipeline pipeline = _pipeline ?? throw new InvalidOperationException("Select a pipeline before recording shader operations.");
        resources.ValidateCompute(_context, pipeline);
    }

    private void EnsureActive()
    {
        _context.EnsureActive();
        if (_ended)
        {
            throw new InvalidOperationException("The graph encoder has already ended.");
        }
    }
}
