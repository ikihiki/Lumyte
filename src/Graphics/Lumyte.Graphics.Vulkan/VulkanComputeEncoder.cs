using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanComputeEncoder(VulkanCommandBuffer owner) : IComputeEncoder
{
    private VulkanComputePipeline? _pipeline;

    private ShaderRootArguments? _arguments;
    private object? _argumentProgram;
    private bool _settingArguments;
    private V.Pipeline _boundPipeline;
    private V.PipelineLayout _boundDescriptorLayout;
    private VulkanShaderBinding? _boundDescriptors;
    private object? _boundProgram;
    private int _pipelineBindCount;
    private int _descriptorBindCount;
    private VulkanArgumentTable? _table;
    private VulkanArgumentTable? _argumentTableHint;

    public void SetArgumentTable(IArgumentTable table)
    {
        // Graph admission legitimately autobinds its table here; it does not change root bytes.
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not VulkanArgumentTable selected || !selected.BelongsTo(owner.Owner))
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
            throw new NotSupportedException("Use a ValidationDevice for argument validation.");
        }
    }

    public void SetArguments<T>(in T value)
        where T : struct, IShaderArguments
    {
        ValidateRootSelection();
        VulkanComputePipeline pipeline = _pipeline!;
        _settingArguments = true;
        try
        {
            int size = VulkanPushConstants.ValidateStackSize(owner.Owner, T.TransferSize);
            Span<byte> payload = stackalloc byte[size];
            payload.Clear();
            (ShaderRootArguments? root, VulkanArgumentTable? tableHint) = VulkanShaderTransfer.CreateRoot(owner.Owner, in value, payload);
            SelectRoot(root, payload, pipeline, tableHint);
        }
        finally
        {
            _settingArguments = false;
        }
    }

    public void SetArguments<T>(in T value, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
    {
        ValidateRootSelection();
        VulkanComputePipeline pipeline = _pipeline!;
        _settingArguments = true;
        try
        {
            int size = VulkanPushConstants.ValidateStackSize(owner.Owner, Unsafe.SizeOf<T>());
            Span<byte> payload = stackalloc byte[size];
            payload.Clear();
            ShaderRootArguments root = VulkanShaderTransfer.CreateUnmanaged(owner.Owner, in value, rootParameter, shaderTypeName, payload);
            SelectRoot(root, payload, pipeline, null);
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
        if (pipeline is not VulkanComputePipeline program || !ReferenceEquals(program.Owner, owner.Owner))
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
        owner.Owner.Api.CmdDispatch(owner.Native, groupCountX, groupCountY, groupCountZ);
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        if (arguments.Count != 1 || arguments.OffsetInBytes % 4 != 0)
        {
            throw new ArgumentException("Indirect execution requires one four-byte-aligned command record.");
        }

        VulkanBuffer<DispatchIndirectArguments> buffer = owner.Buffer(arguments, BufferUsage.Indirect);
        PrepareDispatch();
        owner.Owner.Api.CmdDispatchIndirect(owner.Native, buffer.Native, arguments.OffsetInBytes);
    }

    public void End()
    {
        ThrowIfSettingArguments();
        owner.EndCompute(this);
    }

    private void ThrowIfSettingArguments()
    {
        if (_settingArguments)
        {
            throw new InvalidOperationException("Encoder mutation is not allowed while setting arguments.");
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

    private void SelectRoot(ShaderRootArguments root, ReadOnlySpan<byte> payload, VulkanComputePipeline pipeline, VulkanArgumentTable? tableHint)
    {
        // User codecs and admission hooks can dispose the command owner or pipeline.
        // Revalidate the captured selection before issuing commands or publishing metadata.
        owner.ValidatePass(this);
        pipeline.ValidateAlive();
        if (!ReferenceEquals(_pipeline, pipeline))
        {
            throw new InvalidOperationException("The selected program changed while setting arguments.");
        }

        if (_table != null && tableHint != null && !ReferenceEquals(_table, tableHint))
        {
            throw new ArgumentException("Shader resources belong to a different explicit argument table.");
        }

        tableHint?.ThrowIfDisposed();
        _table?.ThrowIfDisposed();
        VulkanPushConstants.Emit(owner, pipeline.ArgumentPipelineLayout, VulkanPushConstants.ComputeStages, payload);
        _arguments = root;
        _argumentTableHint = tableHint;
        _argumentProgram = pipeline;
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

        VulkanArgumentTable? table = _table ?? _argumentTableHint;
        if (_table != null && _argumentTableHint != null && !ReferenceEquals(_table, _argumentTableHint))
        {
            throw new ArgumentException("Shader resources belong to a different explicit argument table.");
        }

        table?.ThrowIfDisposed();
        IShaderReference[]? registrations = table?.SnapshotRegistrations();

        _pipeline.ValidateAlive();

        V.Pipeline pipeline = _pipeline.Resolve(table?.LayoutKey);
        VulkanShaderBinding? binding = null;
        if (registrations != null)
        {
            binding = table!.AcquireBinding(registrations, _pipeline.ArgumentLayout);
            owner.KeepBinding(binding, registrations);
        }

        // Publish only commands actually emitted. Program identity deliberately makes
        // layout compatibility conservative even when native layouts are equivalent.
        if (_boundPipeline.Handle != pipeline.Handle || !ReferenceEquals(_boundProgram, _pipeline))
        {
            owner.Owner.Api.CmdBindPipeline(owner.Native, V.PipelineBindPoint.Compute, pipeline);
            _boundPipeline = pipeline;
            _pipelineBindCount++;
        }

        if (binding != null && (!ReferenceEquals(_boundDescriptors, binding) ||
            _boundDescriptorLayout.Handle != _pipeline.ArgumentPipelineLayout.Handle ||
            !ReferenceEquals(_boundProgram, _pipeline)))
        {
            V.DescriptorSet set = binding.Native;
            owner.Owner.Api.CmdBindDescriptorSets(owner.Native, V.PipelineBindPoint.Compute, _pipeline.ArgumentPipelineLayout, 0, 1, &set, 0, null);
            _boundDescriptors = binding;
            _boundDescriptorLayout = _pipeline.ArgumentPipelineLayout;
            _descriptorBindCount++;
        }

        _boundProgram = _pipeline;
    }
}
