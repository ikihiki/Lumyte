using Lumyte.Graphics.Abstractions;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanComputeEncoder(VulkanCommandBuffer owner) : IComputeEncoder
{
    private VulkanComputePipeline? _pipeline;

    private VulkanArgumentTable? _argumentTable;
    private ShaderValueSnapshot? _arguments;
    private object? _argumentProgram;

    public void SetArgumentTable(IArgumentTable table)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not VulkanArgumentTable concrete || !concrete.BelongsTo(owner.Owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(table));
        }

        concrete.ThrowIfDisposed();
        _argumentTable = concrete;
    }

    public void SetArguments<T>(in T value)
        where T : struct
    {
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a program before setting its arguments.");
        }

        _pipeline.ValidateAlive();
        ShaderValueSnapshot snapshot = ShaderCodec<T>.Capture(in value);
        ShaderDataLayout.Root(_pipeline.Data, snapshot.RootParameter).Validate(snapshot);
        foreach (ShaderValue member in snapshot.Values)
        {
            if (member.IsReference)
            {
                if (member.Reference is not IShaderReference reference || reference.Table is not VulkanArgumentTable referenceTable || !referenceTable.BelongsTo(owner.Owner))
                {
                    throw new ArgumentException("Root arguments contain a missing or foreign backend reference.", nameof(value));
                }

                reference.Validate();
            }
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
        _pipeline = program;
    }

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a compute program before dispatch.");
        }

        if (_arguments != null && !ReferenceEquals(_argumentProgram, _pipeline))
        {
            throw new InvalidOperationException("Set arguments again after switching to a different program.");
        }

        if (_arguments == null && ShaderDataLayout.HasRoot(_pipeline.Data))
        {
            throw new InvalidOperationException("Set the program's root arguments before execution.");
        }

        ShaderBindingSnapshot? bindingSnapshot = null;
        if (_arguments != null)
        {
            if (_argumentTable == null && _arguments.Values.Any(v => v.IsReference))
            {
                throw new InvalidOperationException("Select an argument table before using shader arguments.");
            }

            _argumentTable?.ThrowIfDisposed();
            var snapshot = ShaderBindingSnapshot.Capture((object?)_argumentTable ?? this, _arguments);
            bindingSnapshot = snapshot;
        }

        _pipeline.ValidateAlive();
        uint limit = owner.Owner.Caps.MaxComputeWorkgroupsPerDimension;
        if (groupCountX == 0 || groupCountY == 0 || groupCountZ == 0 || groupCountX > limit || groupCountY > limit || groupCountZ > limit)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCountX));
        }

        owner.Owner.Api.CmdBindPipeline(owner.Native, V.PipelineBindPoint.Compute, _pipeline.Resolve(bindingSnapshot));
        if (bindingSnapshot != null)
        {
            var binding = new VulkanShaderBinding(owner.Owner, bindingSnapshot, _pipeline.Data, _pipeline.ArgumentLayout);
            owner.KeepBinding(binding);
            V.DescriptorSet set = binding.Native;
            owner.Owner.Api.CmdBindDescriptorSets(owner.Native, V.PipelineBindPoint.Compute, _pipeline.ArgumentPipelineLayout, 0, 1, &set, 0, null);
        }

        owner.Owner.Api.CmdDispatch(owner.Native, groupCountX, groupCountY, groupCountZ);
        if (bindingSnapshot != null)
        {
            owner.TrackProgram(bindingSnapshot.Validate);
        }

        owner.TrackProgram(_pipeline.ValidateAlive);
    }

    public void End() => owner.EndCompute(this);
}
