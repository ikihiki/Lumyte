using Lumyte.Graphics.Abstractions;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanComputeEncoder(VulkanCommandBuffer owner) : IComputeEncoder
{
    private VulkanComputePipeline? _pipeline;

    private ShaderValueSnapshot? _arguments;
    private object? _argumentProgram;
    private VulkanArgumentTable? _table;

    public void SetArgumentTable(IArgumentTable table)
    {
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

    public void SetArguments<T>(in T value)
        where T : IShaderArguments
    {
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a program before setting its arguments.");
        }

        _pipeline.ValidateAlive();
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        if (owner.Owner.Validation != null)
        {
            ShaderDataLayout.Root(_pipeline.Data, snapshot.RootParameter).ValidateArguments(snapshot, validateReferences: true);
        }

        _arguments = snapshot;
        _argumentProgram = _pipeline;
    }

    public void SetArguments(ShaderValueSnapshot arguments)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(arguments);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a program before setting its arguments.");
        }

        _pipeline.ValidateAlive();
        ShaderValueSnapshot snapshot = arguments.Copy();
        if (owner.Owner.Validation != null)
        {
            ShaderDataLayout.Root(_pipeline.Data, snapshot.RootParameter).ValidateArguments(snapshot, validateReferences: true);
        }

        _arguments = snapshot;
        _argumentProgram = _pipeline;
    }

    public void SetPipeline(IGraphicsComputePipeline pipeline)
    {
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

    public void End() => owner.EndCompute(this);

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

        ShaderBindingSnapshot? bindingSnapshot = null;
        if (_pipeline.RequiresArguments && _arguments != null)
        {
            _table?.ThrowIfDisposed();
            var snapshot = ShaderBindingSnapshot.Capture(_arguments, _table, owner.ReadShaderData);
            if (owner.Owner.Validation is { } validation)
            {
                EncoderValidation.ShaderAccesses(validation, owner, snapshot.References, PipelineStage.ComputeShader);
            }

            bindingSnapshot = snapshot;
        }

        _pipeline.ValidateAlive();

        owner.Owner.Api.CmdBindPipeline(owner.Native, V.PipelineBindPoint.Compute, _pipeline.Resolve(bindingSnapshot));
        if (bindingSnapshot != null)
        {
            var binding = new VulkanShaderBinding(owner.Owner, bindingSnapshot, _pipeline.Data, _pipeline.ArgumentLayout);
            owner.KeepBinding(binding);
            V.DescriptorSet set = binding.Native;
            owner.Owner.Api.CmdBindDescriptorSets(owner.Native, V.PipelineBindPoint.Compute, _pipeline.ArgumentPipelineLayout, 0, 1, &set, 0, null);
        }
    }
}
