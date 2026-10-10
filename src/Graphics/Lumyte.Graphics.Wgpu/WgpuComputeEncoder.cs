using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuComputeEncoder(WgpuCommandBuffer owner, WGPUComputePassEncoderImpl* handle) : IComputeEncoder
{
    private WgpuComputePipeline? _pipeline;

    private ShaderValueSnapshot? _arguments;
    private object? _argumentProgram;

    public void SetArgumentTable(IArgumentTable table)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(table);
        if (table is not WgpuArgumentTable concrete || !concrete.BelongsTo(owner.Owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(table));
        }

        concrete.ThrowIfDisposed();
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
        if (pipeline is not WgpuComputePipeline program || !ReferenceEquals(program.Owner, owner.Owner))
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
        WGPU.wgpuComputePassEncoderDispatchWorkgroups(handle, groupCountX, groupCountY, groupCountZ);
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
    {
        owner.ValidatePass(this);
        if (arguments.Count != 1 || arguments.OffsetInBytes % 4 != 0)
        {
            throw new ArgumentException("Indirect execution requires one four-byte-aligned command record.");
        }

        WgpuBuffer<DispatchIndirectArguments> buffer = owner.Buffer(arguments, BufferUsage.Indirect);
        PrepareDispatch();
        WGPU.wgpuComputePassEncoderDispatchWorkgroupsIndirect(handle, buffer.Native.Handle, arguments.OffsetInBytes);
    }

    public void End() => owner.EndCompute(this, handle);

    private void PrepareDispatch()
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

        ShaderBindingData? bindingData = null;
        if (_arguments != null)
        {
            var snapshot = ShaderBindingSnapshot.Capture(_arguments, owner.ReadShaderData);
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
