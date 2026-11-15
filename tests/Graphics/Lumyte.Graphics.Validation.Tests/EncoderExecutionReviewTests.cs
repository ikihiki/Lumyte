using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Exercises execution diagnostics independently of native backend validation.</summary>
public sealed class EncoderExecutionReviewTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly Type _checksType = typeof(ValidationDevice).Assembly.GetType("Lumyte.Graphics.Validation.ValidationEncoderChecks", true)!;
    private static readonly BarrierScope _read = new(PipelineStage.VertexShader, ResourceAccess.ShaderRead);
    private static readonly BarrierScope _write = new(PipelineStage.VertexShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite);

    /// <summary>Zero work skips resource and index execution accesses but still admits the bindings.</summary>
    /// <param name="indexed">Uses an indexed draw.</param>
    /// <param name="count">The work count.</param>
    /// <param name="instances">The instance count.</param>
    [Theory]
    [InlineData(false, 0u, 1u)]
    [InlineData(false, 3u, 0u)]
    [InlineData(true, 0u, 1u)]
    [InlineData(true, 3u, 0u)]
    public void ZeroWorkSkipsExecutionAccess(bool indexed, uint count, uint instances)
    {
        using var device = new ValidationDevice(new TypedBackend());
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = CreateBuffer<TypedNumber>(device, TypedBackend.Artifact(), 8);
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 1 });
        var reference = (IShaderReference)table.WriteBuffer(0, buffer.SliceElements(0, 4));
        using IGraphicsPipeline pipeline = Pipeline(device);
        var raw = new ExecutionReviewCommand();
        using IGraphicsCommandBuffer command = Command(device, raw);
        IRenderEncoder render = command.BeginRenderPass(new());
        render.SetPipeline(pipeline);
        object checks = render.GetType().GetField("_checks", Instance)!.GetValue(render)!;
        int admissions = 0;
        Action<IReadOnlyCollection<IShaderReference>> admit = references =>
        {
            Assert.Contains(reference, references);
            admissions++;
        };
        _checksType.GetMethod("CommitRoot", Instance)!.Invoke(checks, [new ShaderDependencyMetadata([reference]), admit]);
        GraphicsValidation tracker = Tracker(device);
        tracker.Access(command, buffer, new(PipelineStage.Copy, ResourceAccess.CopyWrite), 0, 16);
        object indices = new();
        if (indexed)
        {
            render.GetType().GetField("_indexBuffer", Instance)!.SetValue(render, indices);
            render.GetType().GetField("_indexSize", Instance)!.SetValue(render, 32UL);
            render.GetType().GetField("_indexStride", Instance)!.SetValue(render, 4u);
            tracker.Access(command, indices, new(PipelineStage.Copy, ResourceAccess.CopyWrite), 0, 32);
            render.DrawIndexed(count, instances);
        }
        else
        {
            render.Draw(count, instances);
        }

        Assert.Equal(1, admissions);
        Assert.Equal(1, raw.Render.Draws);
        Assert.Throws<InvalidOperationException>(() => render.Draw(3));
        table.ReleaseBuffer(0);
        Assert.Throws<InvalidOperationException>(() => render.Draw(0));
        render.End();
    }

    /// <summary>Skipping invocations does not skip required pipeline, root or index bindings.</summary>
    [Fact]
    public void ZeroWorkPreservesRequiredBindings()
    {
        using var device = new ValidationDevice(new TypedBackend());
        using IGraphicsPipeline pipeline = Pipeline(device);
        using IGraphicsCommandBuffer command = Command(device, new());
        IRenderEncoder render = command.BeginRenderPass(new());
        Assert.Throws<InvalidOperationException>(() => render.Draw(0));
        render.SetPipeline(pipeline);
        Assert.Throws<InvalidOperationException>(() => render.Draw(0));
        object checks = render.GetType().GetField("_checks", Instance)!.GetValue(render)!;
        _checksType.GetMethod("CommitRoot", Instance)!.Invoke(checks, [new ShaderDependencyMetadata([]), null]);
        Assert.Throws<InvalidOperationException>(() => render.DrawIndexed(0));
        render.Draw(0);
        render.End();
    }

    /// <summary>Intersections union access only inside the overlap and record each byte once.</summary>
    /// <param name="reverse">Reverses registration order.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlapPreservesReadOnlyPrefix(bool reverse)
    {
        (ulong Offset, ulong End, ResourceAccess Access)[] ranges = [(0, 16, ResourceAccess.ShaderRead), (8, 24, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite)];
        IReadOnlyList<(ulong Offset, ulong End, ResourceAccess Access)> segments = Partition(reverse ? ranges.Reverse() : ranges);
        Assert.Equal([(0UL, 8UL, ResourceAccess.ShaderRead), (8UL, 24UL, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite)], segments);
        var validation = new GraphicsValidation();
        object command = new(), resource = new();
        validation.Access(command, resource, new(PipelineStage.Copy, ResourceAccess.CopyWrite), 0, 24);
        validation.Barrier(command, resource, new(PipelineStage.Copy, ResourceAccess.CopyWrite), _read, 0, 8);
        validation.Barrier(command, resource, new(PipelineStage.Copy, ResourceAccess.CopyWrite), _write, 8, 16);
        foreach ((ulong Offset, ulong End, ResourceAccess Access) segment in segments)
        {
            validation.Access(command, resource, new(PipelineStage.VertexShader, segment.Access), segment.Offset, segment.End - segment.Offset);
        }

        Assert.Throws<InvalidOperationException>(() => validation.Access(command, resource, _write, 8, 16));
    }

    /// <summary>The actual execution path partitions overlapping registrations rather than their union.</summary>
    [Fact]
    public void ExecutionPartitionsOverlappingRegistrations()
    {
        using var device = new ValidationDevice(new TypedBackend());
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = CreateBuffer<TypedNumber>(device, TypedBackend.Artifact(), 8);
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 2 });
        var read = (IShaderReference)table.WriteBuffer(0, buffer.SliceElements(0, 4));
        var original = (IShaderReference)table.WriteBuffer(1, buffer.SliceElements(2, 4));
        object revision = original.GetType().GetField("_revision", Instance)!.GetValue(original)!;
        Type writableType = typeof(ValidationDevice).Assembly.GetType("Lumyte.Graphics.Validation.ValidationGpuRef`2", true)!.MakeGenericType(typeof(TypedNumber), typeof(TypedNumber));
        var write = (IShaderReference)Activator.CreateInstance(writableType, Instance, null, [table, new ExecutionReviewReference<TypedNumber>(original, ShaderBufferAccess.ReadWrite), buffer, revision, 2], null)!;
        using IGraphicsPipeline pipeline = Pipeline(device);
        using IGraphicsCommandBuffer command = Command(device, new());
        IRenderEncoder render = command.BeginRenderPass(new());
        render.SetPipeline(pipeline);
        object checks = render.GetType().GetField("_checks", Instance)!.GetValue(render)!;
        _checksType.GetMethod("CommitRoot", Instance)!.Invoke(checks, [new ShaderDependencyMetadata([read, write]), null]);
        GraphicsValidation tracker = Tracker(device);
        var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
        tracker.Access(command, buffer, copy, 0, 24);
        tracker.Barrier(command, buffer, copy, _read, 0, 8);
        tracker.Barrier(command, buffer, copy, _write, 8, 16);
        render.Draw(3);
        Assert.Throws<InvalidOperationException>(() => render.Draw(3));
        render.End();
    }

    /// <summary>Nested and duplicate ranges never create execution self-conflicts or fill gaps.</summary>
    [Fact]
    public void PartitionPreservesReadOnlySuffixAndGaps()
    {
        IReadOnlyList<(ulong Offset, ulong End, ResourceAccess Access)> segments = Partition([(0, 32, ResourceAccess.ShaderRead), (8, 16, ResourceAccess.ShaderWrite), (8, 16, ResourceAccess.ShaderWrite), (40, 48, ResourceAccess.ShaderRead), (32, 32, ResourceAccess.ShaderWrite)]);
        Assert.Equal([(0UL, 8UL, ResourceAccess.ShaderRead), (8UL, 16UL, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite), (16UL, 32UL, ResourceAccess.ShaderRead), (40UL, 48UL, ResourceAccess.ShaderRead)], segments);
    }

    internal static IGraphicsCommandBuffer Command(ValidationDevice device, ExecutionReviewCommand raw)
        => (IGraphicsCommandBuffer)Create("ValidationCommandBuffer", device, raw, new CommandBufferDesc());

    internal static GraphicsValidation Tracker(ValidationDevice device)
        => (GraphicsValidation)typeof(ValidationDevice).GetProperty("Tracker", Instance)!.GetValue(device)!;

    private static IReadOnlyList<(ulong Offset, ulong End, ResourceAccess Access)> Partition(IEnumerable<(ulong Offset, ulong End, ResourceAccess Access)> ranges)
        => (IReadOnlyList<(ulong Offset, ulong End, ResourceAccess Access)>)_checksType.GetMethod("Partition", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [ranges])!;

    private static IGraphicsShaderDataBuffer<T> CreateBuffer<T>(IGraphicDevice device, ShaderArtifact artifact, ulong count)
        where T : struct, IShaderArguments => device.CreateShaderDataBuffer<T>(artifact, count);

    private static IGraphicsPipeline Pipeline(ValidationDevice device)
    {
        var artifact = ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Vertex, "main", "test", [1], """{"parameters":[{"name":"arguments","type":{"kind":"constantBuffer"}}]}""");
        var desc = new GraphicsPipelineDesc { VertexShader = new FactoryShader(artifact) };
        return (IGraphicsPipeline)Create("ValidationPipeline", device, new FactoryPipeline(desc), desc);
    }

    private static object Create(string name, params object[] arguments)
        => Activator.CreateInstance(typeof(ValidationDevice).Assembly.GetType("Lumyte.Graphics.Validation." + name, true)!, Instance, null, arguments, null)!;
}
