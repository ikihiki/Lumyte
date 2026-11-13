using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuComputeEncoder(WgpuCommandBuffer owner, WGPUComputePassEncoderImpl* handle) : IComputeEncoder, IShaderRootEncoder
{
    private WgpuComputePipeline? _pipeline;

    private ShaderRootArguments? _arguments;
    private object? _argumentProgram;
    private WgpuArgumentTable? _table;

    public void SetArgumentTable(IArgumentTable table)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not WgpuArgumentTable selected || !selected.BelongsTo(owner.Owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(table));
        }

        selected.ThrowIfDisposed();
        owner.Owner.Validation?.Use(owner, selected);
        _table = selected;
    }

    public void SetArguments<T>(in T value)
        where T : IShaderArguments => SetRootArguments(in value);

    public void SetArguments<T>(in T value, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged => SetRootArguments(in value, rootParameter, shaderTypeName);

    public void SetRootArguments<T>(in T value, IShaderArgumentScope? scope = null)
        where T : IShaderArguments
    {
        ValidateRootSelection();
        var root = ShaderRootArguments.Create(in value, CreateRootStorage, diagnostic: owner.Owner.Validation == null ? null : DiagnosticRoot(T.RootParameter), scope: scope, encode: EncodeRootReference, metadataFactory: WgpuReferenceMetadata.Create);
        SelectRoot(root);
    }

    public void SetRootArguments<T>(in T value, string rootParameter, string? shaderTypeName, IShaderArgumentScope? scope = null)
        where T : unmanaged
    {
        ValidateRootSelection();
        var root = ShaderRootArguments.CreateUnmanaged(in value, rootParameter, shaderTypeName, CreateRootStorage, diagnostic: DiagnosticRoot(rootParameter), scope: scope, encode: EncodeRootReference, metadataFactory: WgpuReferenceMetadata.Create);
        SelectRoot(root);
    }

    public void SetPipeline(IGraphicsComputePipeline pipeline)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not WgpuComputePipeline program || !ReferenceEquals(program.Owner, owner.Owner))
        {
            throw new ArgumentException("Compute program belongs to another device.");
        }

        program.ValidateAlive();
        owner.Owner.Validation?.Use(owner, program);
        _pipeline = program;
    }

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = owner.Owner.Validation?.BeginRecording(owner);
        owner.ValidatePass(this);
        uint limit = owner.Owner.Caps.MaxComputeWorkgroupsPerDimension;
        if (groupCountX == 0 || groupCountY == 0 || groupCountZ == 0 || groupCountX > limit || groupCountY > limit || groupCountZ > limit)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCountX));
        }

        PrepareDispatch();
        WGPU.wgpuComputePassEncoderDispatchWorkgroups(handle, groupCountX, groupCountY, groupCountZ);
        validationRecording?.Commit();
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = owner.Owner.Validation?.BeginRecording(owner);
        owner.ValidatePass(this);
        if (arguments.Count != 1 || arguments.OffsetInBytes % 4 != 0)
        {
            throw new ArgumentException("Indirect execution requires one four-byte-aligned command record.");
        }

        WgpuBuffer<DispatchIndirectArguments> buffer = owner.Buffer(arguments, BufferUsage.Indirect);
        PrepareDispatch();
        owner.Owner.Validation?.Access(owner, buffer, new(PipelineStage.DrawIndirect, ResourceAccess.IndirectRead), arguments.OffsetInBytes, arguments.SizeInBytes);
        WGPU.wgpuComputePassEncoderDispatchWorkgroupsIndirect(handle, buffer.Native.Handle, arguments.OffsetInBytes);
        validationRecording?.Commit();
    }

    public void End() => owner.EndCompute(this, handle);

    internal void ReleaseNative()
    {
        if (handle != null)
        {
            WGPU.wgpuComputePassEncoderRelease(handle);
            handle = null;
        }
    }

    private void ValidateRootSelection()
    {
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a program before setting its arguments.");
        }

        _pipeline.ValidateAlive();
    }

    private IShaderRootStorage CreateRootStorage(int size) => new WgpuRootStorage(owner.Owner, size);

    // Stable reference encoding can fail during Set; selection and command ownership change only after creation succeeds.
    private byte[] EncodeRootReference(ShaderValue value) => ShaderReferenceEncoding.Pack(value);

    private ShaderDataLayout? DiagnosticRoot(string rootParameter) => owner.Owner.Validation == null ? null : ShaderDataLayout.Root(_pipeline!.Data, rootParameter);

    private void SelectRoot(ShaderRootArguments root)
    {
        try
        {
            owner.KeepBinding(root);
        }
        catch
        {
            root.Dispose();
            throw;
        }

        _arguments = root;
        _argumentProgram = _pipeline;
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

        ShaderBindingData? bindingData = null;
        if (_pipeline.RequiresArguments && _arguments != null)
        {
            _table?.ThrowIfDisposed();
            var snapshot = ShaderBindingSnapshot.Capture(_arguments, _table, owner.ReadShaderData);
            if (owner.Owner.Validation is { } validation)
            {
                EncoderValidation.ShaderAccesses(validation, owner, snapshot.References, PipelineStage.ComputeShader);
            }

            bindingData = new(snapshot, _pipeline.Data, owner.Owner.Caps);
        }

        _pipeline.ValidateAlive();

        WGPUComputePipelineImpl* pipeline = _pipeline.Resolve(bindingData);
        WGPU.wgpuComputePassEncoderSetPipeline(handle, pipeline);
        if (bindingData != null)
        {
            WGPUBindGroupLayoutImpl* layout = WGPU.wgpuComputePipelineGetBindGroupLayout(pipeline, 0);
            try
            {
                var binding = new WgpuShaderBinding(owner.Owner, bindingData, layout);
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
