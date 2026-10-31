using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe class Direct3D12ComputeEncoder(Direct3D12CommandBuffer owner) : IComputeEncoder
{
    private Direct3D12ComputePipeline? _pipeline;

    private ShaderValueSnapshot? _arguments;
    private object? _argumentProgram;
    private Direct3D12ArgumentTable? _table;

    public void SetArgumentTable(IArgumentTable table)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not Direct3D12ArgumentTable selected || !selected.BelongsTo(owner.Owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(table));
        }

        selected.ThrowIfDisposed();
        _table = selected;
    }

    public void SetArguments<T>(in T value)
        where T : struct, IShaderArguments
    {
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a program before setting its arguments.");
        }

        _pipeline.ValidateAlive();
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        _arguments = snapshot;
        _argumentProgram = _pipeline;
    }

    public void SetPipeline(IGraphicsComputePipeline pipeline)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not Direct3D12ComputePipeline program || !ReferenceEquals(program.Owner, owner.Owner))
        {
            throw new ArgumentException("Compute program belongs to another device.");
        }

        program.ValidateAlive();
        _pipeline = program;
    }

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        owner.ValidatePass(this);
        uint limit = owner.Owner.Caps.MaxComputeWorkgroupsPerDimension;
        if (groupCountX == 0 || groupCountY == 0 || groupCountZ == 0 || groupCountX > limit || groupCountY > limit || groupCountZ > limit)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCountX));
        }

        PrepareDispatch();
        NativeMethods.Dispatch(owner.Native, groupCountX, groupCountY, groupCountZ);
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
    {
        owner.ValidatePass(this);
        if (arguments.Count != 1 || arguments.OffsetInBytes % 4 != 0)
        {
            throw new ArgumentException("Indirect execution requires one four-byte-aligned command record.");
        }

        Direct3D12Buffer<DispatchIndirectArguments> buffer = owner.Buffer(arguments, BufferUsage.Indirect);
        PrepareDispatch();
        owner.Indirect(buffer.Native, arguments.OffsetInBytes, 2);
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
            bindingSnapshot = snapshot;
        }

        _pipeline.ValidateAlive();

        if (_pipeline.RequiresArguments && bindingSnapshot == null)
        {
            throw new InvalidOperationException("Set shader arguments before dispatch.");
        }

        owner.Owner.Check(NativeMethods.Pipeline(owner.Native, _pipeline.Native, 1));
        if (bindingSnapshot != null)
        {
            var binding = new Direct3D12ShaderBinding(owner.Owner, bindingSnapshot, _pipeline.Data);
            owner.KeepBinding(binding);
            NativeMethods.Binding(owner.Native, binding.Native, 1);
        }
    }
}
