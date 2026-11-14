using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanComputeEncoder(VulkanCommandBuffer owner) : IComputeEncoder
{
    private VulkanComputePipeline? _pipeline;

    private Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? _argumentValidation;
    private ShaderRootArguments? _arguments;
    private object? _argumentProgram;
    private bool _settingArguments;
    private VulkanArgumentTable? _table;

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
        owner.Owner.Validation?.Use(owner, selected);
        _table = selected;
    }

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
    {
        ThrowIfSettingArguments();
        owner.ValidatePass(this);
        _argumentValidation = prepare;
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
            ShaderRootArguments root = VulkanShaderTransfer.CreateRoot(owner.Owner, in value, payload, diagnostic: owner.Owner.Validation == null ? null : DiagnosticRoot(T.RootParameter), prepare: _argumentValidation);
            SelectRoot(root, payload, pipeline);
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
            ShaderRootArguments root = VulkanShaderTransfer.CreateUnmanaged(owner.Owner, in value, rootParameter, shaderTypeName, payload, diagnostic: DiagnosticRoot(rootParameter), prepare: _argumentValidation);
            SelectRoot(root, payload, pipeline);
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
        owner.Owner.Validation?.Use(owner, program);
        _pipeline = program;
    }

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        ThrowIfSettingArguments();
        using GraphicsValidation.RecordingTransaction? validationRecording = owner.Owner.Validation?.BeginRecording(owner);
        owner.ValidatePass(this);
        uint limit = owner.Owner.Caps.MaxComputeWorkgroupsPerDimension;
        if (groupCountX == 0 || groupCountY == 0 || groupCountZ == 0 || groupCountX > limit || groupCountY > limit || groupCountZ > limit)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCountX));
        }

        PrepareDispatch();
        owner.Owner.Api.CmdDispatch(owner.Native, groupCountX, groupCountY, groupCountZ);
        validationRecording?.Commit();
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
    {
        ThrowIfSettingArguments();
        using GraphicsValidation.RecordingTransaction? validationRecording = owner.Owner.Validation?.BeginRecording(owner);
        owner.ValidatePass(this);
        if (arguments.Count != 1 || arguments.OffsetInBytes % 4 != 0)
        {
            throw new ArgumentException("Indirect execution requires one four-byte-aligned command record.");
        }

        VulkanBuffer<DispatchIndirectArguments> buffer = owner.Buffer(arguments, BufferUsage.Indirect);
        PrepareDispatch();
        owner.Owner.Validation?.Access(owner, buffer, new(PipelineStage.DrawIndirect, ResourceAccess.IndirectRead), arguments.OffsetInBytes, arguments.SizeInBytes);
        owner.Owner.Api.CmdDispatchIndirect(owner.Native, buffer.Native, arguments.OffsetInBytes);
        validationRecording?.Commit();
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

    private ShaderDataLayout? DiagnosticRoot(string rootParameter) => owner.Owner.Validation == null ? null : ShaderDataLayout.Root(_pipeline!.Data, rootParameter);

    private void SelectRoot(ShaderRootArguments root, ReadOnlySpan<byte> payload, VulkanComputePipeline pipeline)
    {
        // User codecs and admission hooks can dispose the command owner or pipeline.
        // Revalidate the captured selection before issuing commands or publishing metadata.
        owner.ValidatePass(this);
        pipeline.ValidateAlive();
        if (!ReferenceEquals(_pipeline, pipeline))
        {
            throw new InvalidOperationException("The selected program changed while setting arguments.");
        }

        VulkanPushConstants.Emit(owner, pipeline.ArgumentPipelineLayout, VulkanPushConstants.ComputeStages, payload);
        _arguments = root;
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

        ShaderBindingDependencies? bindingDependencies = null;
        if (_pipeline.RequiresArguments && _arguments != null)
        {
            _table?.ThrowIfDisposed();
            var dependencies = ShaderBindingDependencies.Collect(_arguments, _table, owner.ReadShaderData);
            if (owner.Owner.Validation is { } validation)
            {
                EncoderValidation.ShaderAccesses(validation, owner, dependencies.References, PipelineStage.ComputeShader);
            }

            bindingDependencies = dependencies;
        }

        _pipeline.ValidateAlive();

        owner.Owner.Api.CmdBindPipeline(owner.Native, V.PipelineBindPoint.Compute, _pipeline.Resolve(bindingDependencies));
        if (bindingDependencies != null)
        {
            var binding = new VulkanShaderBinding(owner.Owner, bindingDependencies, _pipeline.Data, _pipeline.ArgumentLayout);
            owner.KeepBinding(binding);
            V.DescriptorSet set = binding.Native;
            owner.Owner.Api.CmdBindDescriptorSets(owner.Native, V.PipelineBindPoint.Compute, _pipeline.ArgumentPipelineLayout, 0, 1, &set, 0, null);
        }
    }
}
