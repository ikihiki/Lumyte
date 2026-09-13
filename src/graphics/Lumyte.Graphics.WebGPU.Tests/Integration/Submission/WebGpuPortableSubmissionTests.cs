using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableSubmissionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShaderAndPipelineFailuresAreReportedAfterGpuUseEnds(bool invalidModule)
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuShaderModuleHandle module = fixture.Module(invalidModule
            ? "this is invalid WGSL"
            : "@compute @workgroup_size(1) fn main() { }");
        P.GpuComputePipelineHandle pipeline = fixture.Pipeline(new P.GpuShaderProgramDescription(
            [new(module, P.GpuShaderStage.Compute, invalidModule ? "main" : "missing")], []));
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.Dispatch(1);
        commands.EndCompute();
        P.GpuSemaphore completion = fixture.Semaphore();

        fixture.Queue.Submit([commands], completion, 1);
        P.GpuExecutionException failure = await Assert.ThrowsAsync<P.GpuExecutionException>(async () =>
            await fixture.Queue.WaitAsync(completion, 1));

        Assert.Equal(new P.GpuFenceValue(completion, 1), failure.FenceValue);
        Assert.NotEmpty(failure.Diagnostics);
        Assert.True(fixture.Queue.IsComplete(completion, 1));
        Assert.Equal(0, fixture.Backend.PendingCommandCount);
        if (invalidModule)
        {
            IReadOnlyList<P.GpuDiagnostic> moduleDiagnostics = await fixture.Backend.GetCreationDiagnostics(module);
            Assert.NotEmpty(moduleDiagnostics);
            Assert.All(moduleDiagnostics, item => Assert.Contains(item, failure.Diagnostics));
        }
    }

    [Fact]
    public async Task ReferencedResourceCreationFailuresRemainPartOfSubmissionDiagnostics()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuBufferHandle invalidBuffer = fixture.Buffer(32, P.GpuBufferUsage.Storage | P.GpuBufferUsage.MapRead);
        P.GpuBindingLayoutHandle layout = fixture.Layout(new P.GpuBindingLayoutEntry(0, P.GpuShaderStage.Compute,
            new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)));
        P.GpuBindingsHandle bindings = fixture.Bindings(layout, P.GpuBindingEntry.Buffer(0, new(invalidBuffer)));
        const string source = """
            @group(0) @binding(0) var<storage, read_write> output: array<u32>;
            @compute @workgroup_size(1)
            fn main() { output[0] = 1u; }
            """;
        P.GpuComputePipelineHandle pipeline = fixture.Pipeline(source, bindingLayouts: [layout]);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.SetComputeBindings(0, bindings);
        commands.Dispatch(1);
        commands.EndCompute();
        P.GpuSemaphore completion = fixture.Semaphore();

        fixture.Queue.Submit([commands], completion, 1);
        P.GpuExecutionException failure = await Assert.ThrowsAsync<P.GpuExecutionException>(async () =>
            await fixture.Queue.WaitAsync(completion, 1));

        IReadOnlyList<P.GpuDiagnostic> bufferDiagnostics = await fixture.Backend.GetCreationDiagnostics(invalidBuffer);
        Assert.NotEmpty(bufferDiagnostics);
        Assert.All(bufferDiagnostics, item => Assert.Contains(item, failure.Diagnostics));
        Assert.True(fixture.Queue.IsComplete(completion, 1));
    }

    [Fact]
    public async Task InvalidCommandRejectsTheWholeBatchWithoutPoisoningALaterIndependentSubmission()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuBufferHandle upload = await fixture.UploadAsync([41u, 42, 43, 44]);
        P.GpuBufferHandle gpu = fixture.Buffer(16, P.GpuBufferUsage.CopySource | P.GpuBufferUsage.CopyDestination);
        P.GpuBufferHandle readback = fixture.Buffer(16, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuCommandBuffer validFirst = fixture.Record();
        P.GpuCommandBuffer invalidSecond = fixture.Record();
        validFirst.CopyBuffer(new(upload), new(gpu));
        invalidSecond.CopyBuffer(new(upload, 1, 4), new(gpu, 0, 4));
        P.GpuSemaphore completion = fixture.Semaphore();

        fixture.Queue.Submit([validFirst, invalidSecond], completion, 1);
        P.GpuCommandBuffer read = fixture.Record();
        read.CopyBuffer(new(gpu), new(readback));
        fixture.Queue.Submit([read], completion, 2);
        await fixture.Queue.WaitAsync(completion, 2);
        P.GpuExecutionException firstFailure = await Assert.ThrowsAsync<P.GpuExecutionException>(async () =>
            await fixture.Queue.WaitAsync(completion, 1));

        Assert.Equal(new uint[4], await fixture.ReadAsync(readback, 4));
        Assert.True(fixture.Queue.IsComplete(completion, 1));
        Assert.True(fixture.Queue.IsComplete(completion, 2));
        P.GpuExecutionException repeatedFailure = await Assert.ThrowsAsync<P.GpuExecutionException>(async () =>
            await fixture.Queue.WaitAsync(completion, 1));
        Assert.Equal(firstFailure.Diagnostics, repeatedFailure.Diagnostics);
        Assert.Equal(0, fixture.Backend.PendingCommandCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(12)]
    public async Task IncompleteOrOversizedRootsCannotExecuteEarlierWorkOrReserveTheSignal(int invalidRootSize)
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuBindingLayoutHandle layout = fixture.Layout(new P.GpuBindingLayoutEntry(0, P.GpuShaderStage.Compute,
            new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)));
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingsHandle bindings = fixture.Bindings(layout, P.GpuBindingEntry.Buffer(0, new(output)));
        P.GpuComputePipelineHandle pipeline = fixture.Pipeline(WebGpuPortableComputeTests.RootWriter, 8, "main", layout);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.SetComputeBindings(0, bindings);
        commands.SetComputeRootData(new WebGpuPortableComputeTests.RootData { Destination = 0, Value = 123 });
        commands.Dispatch(1);
        commands.SetComputeRootData(new byte[invalidRootSize]);
        commands.Dispatch(1);
        commands.EndCompute();
        P.GpuSemaphore completion = fixture.Semaphore();

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            fixture.Queue.Submit([commands], completion, 1));
        Assert.Contains("complete root byte sequence", failure.Message);
        commands.Dispose();
        P.GpuCommandBuffer read = fixture.Record();
        read.CopyBuffer(new(output), new(readback));
        fixture.Queue.Submit([read], completion, 1);
        await fixture.Queue.WaitAsync(completion, 1);

        Assert.Equal(new uint[2], await fixture.ReadAsync(readback, 2));
        Assert.Equal(0, fixture.Backend.PendingCommandCount);
    }

    [Fact]
    public async Task AnAcceptedRecordingCannotBeSubmittedAgain()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuBufferHandle upload = await fixture.UploadAsync([7u, 8]);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBuffer(new(upload), new(readback));
        P.GpuSemaphore completion = fixture.Semaphore();
        fixture.Queue.Submit([commands], completion, 1);
        await fixture.Queue.WaitAsync(completion, 1);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            fixture.Queue.Submit([commands], completion, 2));

        Assert.Contains("cannot be reused", failure.Message);
        Assert.Equal(new uint[] { 7, 8 }, await fixture.ReadAsync(readback, 2));
    }

    [Fact]
    public async Task DuplicateRecordingRejectionLeavesTheRecordingAvailableForOneSubmission()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuCommandBuffer commands = fixture.Record();
        P.GpuSemaphore completion = fixture.Semaphore();

        ArgumentException failure = Assert.Throws<ArgumentException>(() =>
            fixture.Queue.Submit([commands, commands], completion, 1));
        Assert.Equal("commandBuffers", failure.ParamName);
        fixture.Queue.Submit([commands], completion, 1);
        await fixture.Queue.WaitAsync(completion, 1);

        Assert.True(fixture.Queue.IsComplete(completion, 1));
    }

    [Fact]
    public async Task ForeignQueueRecordingsAndSemaphoresAreRejectedBeforeAcceptance()
    {
        using WebGpuPortableComputeFixture first = await WebGpuPortableComputeFixture.CreateAsync();
        using WebGpuPortableComputeFixture second = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuCommandBuffer commands = first.Record();
        P.GpuSemaphore ownCompletion = first.Semaphore();
        P.GpuSemaphore foreignCompletion = second.Semaphore();

        Assert.Throws<ArgumentException>(() => second.Queue.Submit([commands], foreignCompletion, 1));
        Assert.Throws<ArgumentException>(() => first.Queue.Submit([commands], foreignCompletion, 1));
        first.Queue.Submit([commands], ownCompletion, 1);
        await first.Queue.WaitAsync(ownCompletion, 1);

        Assert.True(first.Queue.IsComplete(ownCompletion, 1));
    }
}
