using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Enforces the preparation/recording boundary through an allocation-counting native double.</summary>
[Collection("Static codec counters")]
public sealed class EnforcingNativePreparationTests
{
    /// <summary>Recording only consumes immutable command-local bytes, even when all ABI providers throw.</summary>
    [Fact]
    public void RecordingDoesNotAllocateMapOrInvokeCodec()
    {
        var backend = new EnforcingNativePreparationBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShader shader = device.CreateShader(Artifact());
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        var arguments = new EnforcingNativePreparationArguments(new object());
        EnforcingNativePreparationArguments.Poison = false;
        EnforcingNativePreparationArguments.Writes = 0;
        command.PrepareCompute(pipeline, null, in arguments);
        Assert.Equal(1, backend.Allocations);
        Assert.Equal(1, backend.Maps);
        Assert.Equal(1, backend.Writes);
        Assert.Equal(1, EnforcingNativePreparationArguments.Writes);
        EnforcingNativePreparationArguments.Poison = true;
        try
        {
            IComputeEncoder encoder = command.BeginComputePass(new());
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(in arguments);
            encoder.Dispatch(1);
            encoder.SetArguments(in arguments);
            encoder.Dispatch(1);
            encoder.End();
            command.Finish();
            Assert.Equal(1, backend.Allocations);
            Assert.Equal(1, backend.Maps);
            Assert.Equal(1, backend.Writes);
            Assert.Equal(1, EnforcingNativePreparationArguments.Writes);
            Assert.Equal(2, backend.Executions);
        }
        finally
        {
            EnforcingNativePreparationArguments.Poison = false;
        }
    }

    /// <summary>An identity prepared on another command must not trigger lazy native work.</summary>
    [Fact]
    public void ForeignCommandIdentityRejectsBeforeExecution()
    {
        var backend = new EnforcingNativePreparationBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShader shader = device.CreateShader(Artifact());
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsCommandBuffer prepared = device.CreateCommandBuffer(new());
        using IGraphicsCommandBuffer foreign = device.CreateCommandBuffer(new());
        var arguments = new EnforcingNativePreparationArguments(new object());
        prepared.PrepareCompute(pipeline, null, in arguments);
        IComputeEncoder encoder = foreign.BeginComputePass(new());
        encoder.SetPipeline(pipeline);
        Assert.Throws<InvalidOperationException>(() => encoder.SetArguments(in arguments));
        Assert.Equal(0, backend.Executions);
        Assert.Equal(1, backend.Allocations);
        Assert.Equal(1, backend.Writes);
        encoder.End();
    }

    /// <summary>Changing program selection cannot reuse a prepared entry.</summary>
    [Fact]
    public void PipelineMismatchRejectsBeforeExecution()
    {
        var backend = new EnforcingNativePreparationBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShader shader = device.CreateShader(Artifact());
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsComputePipeline other = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        var arguments = new EnforcingNativePreparationArguments(new object());
        command.PrepareCompute(pipeline, null, in arguments);
        IComputeEncoder encoder = command.BeginComputePass(new());
        encoder.SetPipeline(other);
        Assert.Throws<InvalidOperationException>(() => encoder.SetArguments(in arguments));
        Assert.Equal(0, backend.Executions);
        encoder.End();
    }

    /// <summary>Preparation after recording is rejected rather than silently extending the cache.</summary>
    [Fact]
    public void PreparationAfterFirstPassIsRejected()
    {
        var backend = new EnforcingNativePreparationBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShader shader = device.CreateShader(Artifact());
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        command.BeginComputePass(new()).End();
        var arguments = new EnforcingNativePreparationArguments(new object());
        Assert.Throws<InvalidOperationException>(() => command.PrepareCompute(pipeline, null, in arguments));
        Assert.Equal(0, backend.Allocations);
    }

    /// <summary>A missing preparation identity cannot allocate or encode native root data.</summary>
    [Fact]
    public void MissingIdentityRejectsWithoutNativeWork()
    {
        var backend = new EnforcingNativePreparationBackend();
        using IGraphicsShader shader = backend.CreateShader(Artifact());
        using IGraphicsComputePipeline pipeline = backend.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsCommandBuffer command = backend.CreateCommandBuffer(new());
        var arguments = new EnforcingNativePreparationArguments(null);
        Assert.Throws<ArgumentException>(() => command.PrepareCompute(pipeline, null, in arguments));
        Assert.Equal(0, backend.Allocations);
        Assert.Equal(0, backend.Writes);
    }

    /// <summary>A command-local cache cannot keep a disposed command executable.</summary>
    [Fact]
    public void ExpiredCommandRejectsPreparedArguments()
    {
        var backend = new EnforcingNativePreparationBackend();
        using IGraphicsShader shader = backend.CreateShader(Artifact());
        using IGraphicsComputePipeline pipeline = backend.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsCommandBuffer command = backend.CreateCommandBuffer(new());
        var arguments = new EnforcingNativePreparationArguments(new object());
        command.PrepareCompute(pipeline, null, in arguments);
        IComputeEncoder encoder = command.BeginComputePass(new());
        encoder.SetPipeline(pipeline);
        encoder.SetArguments(in arguments);
        command.Dispose();
        Assert.Throws<InvalidOperationException>(() => encoder.SetArguments(in arguments));
        Assert.Throws<InvalidOperationException>(() => encoder.Dispatch(1));
        Assert.Equal(0, backend.Executions);
        Assert.Equal(1, backend.Writes);
    }

    /// <summary>Binding another table cannot silently materialize the prepared root again.</summary>
    [Fact]
    public void TableMismatchRejectsBeforeExecution()
    {
        var backend = new EnforcingNativePreparationBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShader shader = device.CreateShader(Artifact());
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IArgumentTable table = device.CreateArgumentTable(new() { SamplerCapacity = 1 });
        using IArgumentTable other = device.CreateArgumentTable(new() { SamplerCapacity = 1 });
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        var arguments = new EnforcingNativePreparationArguments(new object());
        command.PrepareCompute(pipeline, table, in arguments);
        IComputeEncoder encoder = command.BeginComputePass(new());
        encoder.SetPipeline(pipeline);
        encoder.SetArgumentTable(other);
        Assert.Throws<InvalidOperationException>(() => encoder.SetArguments(in arguments));
        Assert.Equal(0, backend.Executions);
        Assert.Equal(1, backend.Writes);
        encoder.End();
    }

    /// <summary>No-root programs still select command-local preparations through the generic setter.</summary>
    [Fact]
    public void EmptyRootUsesPreparedConsumption()
    {
        var backend = new EnforcingNativePreparationBackend();
        using var device = new ValidationDevice(backend);
        var artifact = ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "empty-preparation", [1], "{\"parameters\":[]}");
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        var arguments = new EmptyNativePreparationArguments(new object());
        command.PrepareCompute(pipeline, null, arguments);
        IComputeEncoder encoder = command.BeginComputePass(new());
        encoder.SetPipeline(pipeline);
        encoder.SetArguments(arguments);
        encoder.Dispatch(1);
        encoder.End();
        Assert.Equal(1, backend.Allocations);
        Assert.Equal(1, backend.Executions);
    }

    private static ShaderArtifact Artifact()
    {
        const string Reflection = """
            {"parameters":[{"name":"arguments","type":{"kind":"constantBuffer","elementType":{"kind":"struct","name":"PreparedNumber","sizes":[{"kind":"uniform","value":4,"alignment":4}],"fields":[{"name":"Value","type":{"kind":"scalar","scalarType":"uint32"},"binding":{"offset":0,"size":4}}]}},"binding":{"kind":"pushConstantBuffer","size":4}}]}
            """;
        return ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "preparation-test", [1], Reflection);
    }
}
