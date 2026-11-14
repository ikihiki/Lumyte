using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Records graph shader operations within one callback-scoped compute pass.</summary>
public sealed class RenderGraphComputeEncoder : IDisposable
{
    private readonly RenderGraphContext _context;
    private readonly IComputeEncoder _native;
    private readonly Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?> _prepareArguments;
    private IGraphicsComputePipeline? _pipeline;
    private IReadOnlyList<ShaderResources>? _arguments;
    private bool _hasArguments;
    private object? _argumentPipeline;
    private bool _ended;
    private IReadOnlyList<ShaderResources>? _pendingResources;
    private bool _settingArguments;

    internal RenderGraphComputeEncoder(RenderGraphContext context, IComputeEncoder native)
    {
        _context = context;
        _native = native;
        _prepareArguments = PrepareArguments;
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

    /// <summary>Captures ordinary application arguments once and infers their predeclared resource group.</summary>
    /// <typeparam name="T">The existing application argument type and its ordinary shader codec.</typeparam>
    /// <param name="arguments">The application values, copied through their codec.</param>
    public void SetArguments<T>(in T arguments)
        where T : IShaderArguments
    {
        EnsureActive();
        if (_pipeline is null)
        {
            throw new InvalidOperationException("Select a pipeline before setting its arguments.");
        }

        _settingArguments = true;
        try
        {
            _native.SetArgumentValidation(_prepareArguments);
            _native.SetArguments(in arguments);
            _arguments = _pendingResources;
            _hasArguments = true;
            _argumentPipeline = _pipeline;
        }
        finally
        {
            try
            {
                _native.SetArgumentValidation(null);
            }
            finally
            {
                _pendingResources = null;
                _settingArguments = false;
            }
        }
    }

    /// <summary>Snapshots an ordinary unmanaged root with the existing graph declaration rules.</summary>
    /// <typeparam name="T">The unmanaged argument type.</typeparam>
    /// <param name="arguments">The C# value to copy without conversion.</param>
    /// <param name="rootParameter">The logical root parameter name.</param>
    /// <param name="shaderTypeName">The shader structure name, or the C# type name.</param>
    public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
    {
        EnsureActive();
        if (_pipeline is null)
        {
            throw new InvalidOperationException("Select a pipeline before setting its arguments.");
        }

        _settingArguments = true;
        try
        {
            _native.SetArgumentValidation(_prepareArguments);
            _native.SetArguments(in arguments, rootParameter, shaderTypeName);
            _arguments = _pendingResources;
            _hasArguments = true;
            _argumentPipeline = _pipeline;
        }
        finally
        {
            try
            {
                _native.SetArgumentValidation(null);
            }
            finally
            {
                _pendingResources = null;
                _settingArguments = false;
            }
        }
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
            _native.SetArgumentValidation(null);
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

    private Action<IReadOnlyCollection<IShaderReference>>? PrepareArguments(IShaderReferenceMetadata references)
    {
        (IReadOnlyList<ShaderResources>? resources, Action<IReadOnlyCollection<IShaderReference>>? admit) =
            _context.SelectArguments(references, [], graphics: false);
        if (resources is not null)
        {
            _context.BindResources(resources, _native);
        }

        _pendingResources = resources;
        return admit;
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

        if (!_hasArguments)
        {
            throw new InvalidOperationException("Set arguments before recording shader operations.");
        }

        if (!ReferenceEquals(_argumentPipeline, _pipeline))
        {
            throw new InvalidOperationException("Set arguments again after switching to a different program.");
        }

        if (_arguments is { } resources)
        {
            _context.BindResources(resources, _native);
        }
    }

    private void EnsureActive()
    {
        if (_settingArguments)
        {
            throw new InvalidOperationException("Argument selection cannot be reentered.");
        }

        _context.EnsureActive();
        if (_ended)
        {
            throw new InvalidOperationException("The graph encoder has already ended.");
        }
    }
}
