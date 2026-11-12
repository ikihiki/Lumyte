using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Records graph shader operations within one callback-scoped compute pass.</summary>
public sealed class RenderGraphComputeEncoder : IDisposable
{
    private readonly RenderGraphContext _context;
    private readonly IComputeEncoder _native;
    private IGraphicsComputePipeline? _pipeline;
    private RenderGraphShaderResources? _arguments;
    private object? _argumentPipeline;
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

    /// <summary>Captures logical numeric values once and selects arguments for the current pipeline.</summary>
    /// <param name="resources">The shader inputs declared for this graph pass.</param>
    public void SetArguments(RenderGraphShaderResources resources)
    {
        Validate(resources);
        ShaderValueSnapshot snapshot = resources.Snapshot();
        _native.SetArguments(snapshot);
        _arguments = resources;
        _argumentPipeline = _pipeline;
    }

    /// <summary>Records Dispatch with the previously selected arguments.</summary>
    /// <param name="groupCountX">The groupCountX value.</param>
    /// <param name="groupCountY">The groupCountY value.</param>
    /// <param name="groupCountZ">The groupCountZ value.</param>
    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        ValidateExecution();
        _native.Dispatch(groupCountX, groupCountY, groupCountZ);
    }

    /// <summary>Records DispatchIndirect with the previously selected arguments.</summary>
    /// <param name="arguments">The arguments value.</param>
    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
    {
        ValidateExecution();
        _native.DispatchIndirect(arguments);
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

    private void ValidateExecution()
    {
        EnsureActive();
        if (_pipeline is null)
        {
            throw new InvalidOperationException("Select a pipeline before recording shader operations.");
        }

        // Borrowed graphs without a device cannot create logical resources; native execution validates its own root contract.
        if (_context.DeviceTarget is not { } target)
        {
            return;
        }

        if (!ShaderDataLayout.HasRoot(_pipeline.Desc.ComputeShader.Artifact.GetTarget(target)))
        {
            return;
        }

        RenderGraphShaderResources resources = _arguments ?? throw new InvalidOperationException("Set arguments before recording shader operations.");
        if (!ReferenceEquals(_argumentPipeline, _pipeline))
        {
            throw new InvalidOperationException("Set arguments again after switching to a different program.");
        }

        Validate(resources);
        resources.Bind(_native);
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
