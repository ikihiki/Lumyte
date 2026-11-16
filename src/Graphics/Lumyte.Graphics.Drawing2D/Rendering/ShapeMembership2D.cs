using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

// Independent pass-local table: never joins the raster read-only reservation.
internal sealed class ShapeMembership2D : IDisposable
{
    private readonly ArgumentTablePlan _plan;
    private readonly ShaderResources _scope;
    private readonly IGraphicsComputePipeline _pipeline;
    private readonly List<Dispatch> _dispatches;
    private IGpuRef<DrawRecord2D>? _records;
    private IGpuRef<Vector4>? _numeric;
    private IGpuRef<uint>? _output;

    internal ShapeMembership2D(IGraphicDevice device, IGraphicsComputePipeline pipeline, IGraphicsShaderDataBuffer<DrawRecord2D> records, IGraphicsBuffer<Vector4> numeric, IGraphicsBuffer<uint> output, List<Dispatch> dispatches)
    {
        _pipeline = pipeline;
        _dispatches = dispatches;
        _plan = new(device);
        try
        {
            _scope = _plan.CreateShaderResources();
            _plan.ConfigureExecution(_scope, 0, false);
            _scope.RegisterBuffer(records.SliceElements(0, records.Count), value => _records = value);
            _scope.RegisterBuffer(numeric.SliceElements(0, numeric.Count), value => _numeric = value);
            _scope.RegisterBuffer(output.SliceElements(0, output.Count), value => _output = value, ArgumentBufferAccess.ReadWrite);
            _plan.MaterializeResources([_scope]);
        }
        catch
        {
            _plan.Dispose();
            throw;
        }
    }

    public void Dispose() => _plan.Dispose();

    internal static Dispatch Plan(uint record, Vector4 bounds, uint offset, DeviceCaps caps)
    {
        if (caps.MaxComputeInvocationsPerWorkgroup < 256 || caps.MaxComputeWorkgroupSizeX < 16 || caps.MaxComputeWorkgroupSizeY < 16 || caps.MaxComputeWorkgroupSizeZ < 1)
        {
            throw new NotSupportedException("Shape membership requires 16x16 compute workgroups.");
        }

        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Z) || !float.IsFinite(bounds.W)
            || bounds.X < 0 || bounds.Y < 0 || bounds.Z <= bounds.X || bounds.W <= bounds.Y
            || MathF.Floor(bounds.X) != bounds.X || MathF.Floor(bounds.Y) != bounds.Y
            || MathF.Floor(bounds.Z) != bounds.Z || MathF.Floor(bounds.W) != bounds.W)
        {
            throw new NotSupportedException("Shape membership requires finite nonnegative integer bounds.");
        }

        uint width = checked((uint)(bounds.Z - bounds.X)), height = checked((uint)(bounds.W - bounds.Y));
        if (width == 0 || height == 0)
        {
            throw new NotSupportedException("Shape membership requires nonempty integer bounds.");
        }

        uint x = checked((width + 15) / 16), y = checked((height + 15) / 16);
        if (x > caps.MaxComputeWorkgroupsPerDimension || y > caps.MaxComputeWorkgroupsPerDimension)
        {
            throw new NotSupportedException("Shape membership dispatch exceeds device limits.");
        }

        _ = checked(offset + checked(width * height));
        return new(record, width, height, offset, x, y);
    }

    internal void Record(IGraphicsCommandBuffer commands, IGraphicsShaderDataBuffer<DrawRecord2D> records, IGraphicsBuffer<Vector4> numeric, IGraphicsBuffer<uint> output)
    {
        var read = new BarrierScope(PipelineStage.ComputeShader, ResourceAccess.ShaderRead);
        commands.Barrier(new ShaderDataBufferBarrierDesc<DrawRecord2D> { Buffer = records.SliceElements(0, records.Count), Before = new(PipelineStage.Copy | PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead), After = read });
        commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = numeric.SliceElements(0, numeric.Count), Before = new(PipelineStage.Copy | PipelineStage.FragmentShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead), After = read });
        IComputeEncoder compute = commands.BeginComputePass(new());
        compute.SetPipeline(_pipeline);
        foreach (Dispatch dispatch in _dispatches)
        {
            var args = new ShapeMembershipArguments2D { Records = _records!, Numeric = _numeric!, Output = _output!, RecordIndex = dispatch.Record, Width = dispatch.Width, Height = dispatch.Height, Offset = dispatch.Offset };
            _scope.Dispatch(compute, in args, dispatch.GroupsX, dispatch.GroupsY);
        }

        compute.End();
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.SliceElements(0, output.Count), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
        commands.Barrier(new ShaderDataBufferBarrierDesc<DrawRecord2D> { Buffer = records.SliceElements(0, records.Count), Before = read, After = new(PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
        commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = numeric.SliceElements(0, numeric.Count), Before = read, After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
    }

    internal readonly record struct Dispatch(uint Record, uint Width, uint Height, uint Offset, uint GroupsX, uint GroupsY);
}
