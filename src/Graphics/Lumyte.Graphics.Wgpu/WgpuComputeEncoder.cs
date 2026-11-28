using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuComputeEncoder(WgpuCommandBuffer owner, WGPUComputePassEncoderImpl* handle) : IComputeEncoder
{
    private WgpuComputePipeline? _pipeline;

    private WgpuRootStorage? _arguments;
    private object? _argumentProgram;
    private Type? _argumentsType;
    private bool _settingArguments;
    private WgpuCommandBuffer.PreparedEntry? _preparedArguments;
    private WgpuArgumentTable? _table;
    private WgpuArgumentTable? _currentRootHint;

    public void SetArgumentTable(IArgumentTable table)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not WgpuArgumentTable selected || !selected.BelongsTo(owner.Owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(table));
        }

        selected.ThrowIfDisposed();
        _table = selected;
    }

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        if (prepare != null)
        {
            throw new NotSupportedException("Use ValidationDevice for argument validation.");
        }
    }

    public void SetArguments<T>(in T value)
        where T : struct, IShaderArguments
    {
        ThrowIfSettingArguments();
        ValidateRootSelection();
        WgpuComputePipeline program = _pipeline!;
        if (value.NativePreparationIdentity is { } identity)
        {
            WgpuCommandBuffer.PreparedEntry prepared = owner.FindPrepared(identity, typeof(T), program, _table, null, [], null, null);
            _argumentsType = typeof(T);
            _preparedArguments = prepared;
            _arguments = prepared.Root;
            _argumentProgram = program;
            _currentRootHint = prepared.Root.ImplicitTable;
            return;
        }

        _settingArguments = true;
        try
        {
            WgpuRootStorage root = WgpuShaderTransfer.CreateRoot(owner.Owner, in value);
            SelectRoot(root, program);
        }
        finally
        {
            _settingArguments = false;
        }
    }

    public void SetArguments<T>(in T value, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
    {
        ThrowIfSettingArguments();
        ValidateRootSelection();
        WgpuComputePipeline program = _pipeline!;
        _settingArguments = true;
        try
        {
            WgpuRootStorage root = WgpuShaderTransfer.CreateUnmanaged(owner.Owner, in value, rootParameter, shaderTypeName);
            SelectRoot(root, program);
        }
        finally
        {
            _settingArguments = false;
        }
    }

    public void SetPipeline(IGraphicsComputePipeline pipeline)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not WgpuComputePipeline program || !ReferenceEquals(program.Owner, owner.Owner))
        {
            throw new ArgumentException("Compute program belongs to another device.");
        }

        program.ValidateAlive();
        _pipeline = program;
    }

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        uint limit = owner.Owner.Caps.MaxComputeWorkgroupsPerDimension;
        if (groupCountX == 0 || groupCountY == 0 || groupCountZ == 0 || groupCountX > limit || groupCountY > limit || groupCountZ > limit)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCountX));
        }

        PrepareDispatch();
        WGPU.wgpuComputePassEncoderDispatchWorkgroups(handle, groupCountX, groupCountY, groupCountZ);
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        if (arguments.Count != 1 || arguments.OffsetInBytes % 4 != 0)
        {
            throw new ArgumentException("Indirect execution requires one four-byte-aligned command record.");
        }

        WgpuBuffer<DispatchIndirectArguments> buffer = owner.Buffer(arguments, BufferUsage.Indirect);
        PrepareDispatch();
        WGPU.wgpuComputePassEncoderDispatchWorkgroupsIndirect(handle, buffer.Native.Handle, arguments.OffsetInBytes);
    }

    public void End()
    {
        ThrowIfSettingArguments();
        owner.EndCompute(this, handle);
    }

    internal void ReleaseNative()
    {
        if (handle != null)
        {
            WGPU.wgpuComputePassEncoderRelease(handle);
            handle = null;
        }
    }

    private void ThrowIfSettingArguments()
    {
        if (_settingArguments)
        {
            throw new InvalidOperationException("Root argument preparation is already active.");
        }
    }

    private void ValidateRootSelection()
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a program before setting its arguments.");
        }

        _pipeline.ValidateAlive();
    }

    private void SelectRoot(WgpuRootStorage root, WgpuComputePipeline program)
    {
        try
        {
            owner.ValidatePass(this);
            program.ValidateAlive();
            if (!ReferenceEquals(_pipeline, program))
            {
                throw new InvalidOperationException("The selected program changed while preparing root arguments.");
            }

            if (_table != null && root.ImplicitTable != null && !ReferenceEquals(_table, root.ImplicitTable))
            {
                throw new ArgumentException("Selected table differs from root references.");
            }

            owner.KeepBinding(root);
        }
        catch
        {
            try
            {
                root.Dispose();
            }
            catch
            {
                // Preserve the original transfer or selection failure.
            }

            throw;
        }

        _preparedArguments = null;
        _currentRootHint = root.ImplicitTable;
        _arguments = root;
        _argumentProgram = program;
    }

    private void PrepareDispatch()
    {
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a compute program before dispatch.");
        }

        if (_pipeline.RequiresArguments && _arguments != null && !ReferenceEquals(_argumentProgram, _pipeline))
        {
            throw new InvalidOperationException("Set arguments again after switching to a different program.");
        }

        if (_pipeline.RequiresArguments && _arguments == null)
        {
            throw new InvalidOperationException("Set arguments before drawing or dispatching.");
        }

        if (_table != null && _currentRootHint != null && !ReferenceEquals(_table, _currentRootHint))
        {
            throw new ArgumentException("Selected table differs from root references.");
        }

        if (_preparedArguments is { } prepared)
        {
            _pipeline.ValidateAlive();
            if (!prepared.Matches(_argumentsType!, _pipeline, _table, null, [], null, null))
            {
                throw new InvalidOperationException("Selected execution state differs from native preparation.");
            }

            prepared.Validate();
            WGPU.wgpuComputePassEncoderSetPipeline(handle, prepared.Compute);
            if (prepared.Binding != null)
            {
                WGPU.wgpuComputePassEncoderSetBindGroup(handle, 0, prepared.Binding.Native, 0, null);
            }

            return;
        }

        WgpuArgumentTable? selectedTable = _table ?? _currentRootHint;
        selectedTable?.ThrowIfDisposed();
        IReadOnlyCollection<IShaderReference> registrations = selectedTable?.SnapshotRegistrations() ?? Array.Empty<IShaderReference>();
        ShaderBindingData? bindingData = null;
        if (_pipeline.RequiresArguments || selectedTable != null)
        {
            bindingData = new(registrations, _pipeline.Data, owner.Owner.Caps);
        }

        _pipeline.ValidateAlive();

        WGPUComputePipelineImpl* pipeline = _pipeline.Resolve(bindingData);
        WGPU.wgpuComputePassEncoderSetPipeline(handle, pipeline);
        if (bindingData != null)
        {
            WGPUBindGroupLayoutImpl* layout = WGPU.wgpuComputePipelineGetBindGroupLayout(pipeline, 0);
            try
            {
                var binding = new WgpuShaderBinding(owner.Owner, bindingData, _pipeline.RequiresArguments ? _arguments : null, layout, selectedTable, registrations);
                owner.KeepBinding(binding);
                WGPU.wgpuComputePassEncoderSetBindGroup(handle, 0, binding.Native, 0, null);
            }
            finally
            {
                WGPU.wgpuBindGroupLayoutRelease(layout);
            }
        }
    }
}
