using System.Runtime.CompilerServices;
using Lumyte.Memory;
using Lumyte.Resources.Execution;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies value outputs preserve generation ownership without boxing on reads.</summary>
public sealed class ResourceValueTypeTests
{
    /// <summary>Value source and transform stages share builds, handles and retained content revisions.</summary>
    /// <returns>Completion of construction and ordered release.</returns>
    [Fact]
    public async Task StructOutputsRetainTheirInputAcrossReferencesAndSnapshotsAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        var state = new TestState();
        await using ResourceManager manager = new(
            "values",
            new UnusedSource(),
            [new InputStage(state), new OutputStage(state)],
            memory,
            execution.Resources);
        var key = new ResourceKey<OutputValue>("asset");
        using ResourceReference<OutputValue> first = await manager.RetainAsync(key);
        using ResourceReference<OutputValue> again = await manager.RetainAsync(key);
        using ResourceLease<OutputValue> firstLease = first.Pin();
        using ResourceLease<OutputValue> secondLease = again.Pin();
        Assert.Same(firstLease.Value.Owner, secondLease.Value.Owner);
        Assert.Equal(1, state.InputBuilds);
        Assert.Equal(1, state.OutputBuilds);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ResourceHandle<OutputValue>>());
        Assert.Equal(16, Unsafe.SizeOf<ResourceHandle<OutputValue>>());

        ResourceHandle<OutputValue> handle = first.Handle;
        Assert.True(manager.TryRetain(handle, out ResourceReference<OutputValue>? recovered));
        Assert.NotNull(recovered);
        using (recovered)
        using (ResourceLease<OutputValue> lease = recovered.Pin())
        {
            Assert.Same(firstLease.Value.Owner, lease.Value.Owner);
        }

        using ResourceSnapshot snapshot = manager.CaptureSnapshot();
        Assert.True(snapshot.TryPin(handle, out ResourceLease<OutputValue>? retained));
        Assert.NotNull(retained);
        using (retained)
        {
            snapshot.Dispose();
            firstLease.Dispose();
            secondLease.Dispose();
            first.Dispose();
            again.Dispose();
            Assert.False(manager.TryRetain(handle, out _));
            Assert.Equal(0, await manager.TrimAsync());
            Assert.Empty(state.Releases);
            Assert.Equal(42, retained.Value.Owner.Input.Number);
        }

        Assert.Equal(2, await manager.TrimAsync());
        Assert.Equal(["output", "input"], state.Releases);
    }

    /// <summary>Repeated wrapper reads copy the existing value without creating another box.</summary>
    /// <returns>Completion of the manager's lifetime.</returns>
    [Fact]
    public async Task ReadingStructValuesDoesNotAllocateAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        var state = new TestState();
        await using ResourceManager manager = new("reads", new UnusedSource(), [new InputStage(state)], memory, execution.Resources);
        using ResourceReference<InputValue> reference = await manager.RetainAsync(new ResourceKey<InputValue>("asset"));
        using ResourceLease<InputValue> lease = reference.Pin();
        Assert.Equal(42_000, SumValues(lease, 1000));

        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = SumValues(lease, 10_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(420_000, sum);
        Assert.Equal(0, allocated);
    }

    /// <summary>Zero is a valid owned value and is not confused with an absent generation output.</summary>
    /// <returns>Completion of value construction and release.</returns>
    [Fact]
    public async Task DefaultScalarValueIsPublishedAndReleasedAsync()
    {
        await using MemoryManager memory = new();
        var stage = new ScalarStage();
        await using ResourceManager manager = new("zero", new UnusedSource(), [stage], memory, new InlineRuntime());
        using ResourceReference<int> reference = await manager.RetainAsync(new ResourceKey<int>("zero"));
        using ResourceLease<int> lease = reference.Pin();
        Assert.Equal(0, lease.Value);
        Assert.True(lease.IsUsable);
        lease.Dispose();
        reference.Dispose();

        Assert.Equal(1, await manager.TrimAsync());
        Assert.Equal(1, stage.Releases);
    }

    /// <summary>Retrying wrapper release reaches the same owner and does not release its dependency early.</summary>
    /// <returns>Completion of failed and successful release attempts.</returns>
    [Fact]
    public async Task StructReleaseFailureRetainsOwnerAndInputUntilRetryAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        var state = new TestState { FailOutputRelease = true };
        await using ResourceManager manager = new(
            "retry",
            new UnusedSource(),
            [new InputStage(state), new OutputStage(state)],
            memory,
            execution.Resources);
        using ResourceReference<OutputValue> reference = await manager.RetainAsync(new ResourceKey<OutputValue>("asset"));
        using ResourceLease<OutputValue> lease = reference.Pin();
        OutputOwner owner = lease.Value.Owner;
        lease.Dispose();
        reference.Dispose();

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(async () => await manager.TrimAsync());
        Assert.IsType<IOException>(Assert.Single(failure.Flatten().InnerExceptions));
        Assert.Equal(1, owner.ReleaseAttempts);
        Assert.Equal(["output failed"], state.Releases);

        Assert.Equal(2, await manager.TrimAsync());
        Assert.Equal(2, owner.ReleaseAttempts);
        Assert.Equal(["output failed", "output", "input"], state.Releases);
        Assert.Equal(0, await manager.TrimAsync());
        Assert.Equal(2, owner.ReleaseAttempts);
    }

    /// <summary>Only closed, nonnullable values that can be retained as objects may enter a pipeline.</summary>
    /// <param name="invalidType">The unsupported input or output type.</param>
    /// <returns>Completion of the memory manager's lifetime.</returns>
    [Theory]
    [InlineData(typeof(void))]
    [InlineData(typeof(Span<byte>))]
    [InlineData(typeof(int?))]
    [InlineData(typeof(List<>))]
    public async Task InvalidInputAndOutputTypesAreRejectedAsync(Type invalidType)
    {
        await using MemoryManager memory = new();
        var runtime = new InlineRuntime();
        ArgumentException outputError = Assert.Throws<ArgumentException>(() => new ResourceManager(
            "output", new UnusedSource(), [new InvalidStage(invalidType)], memory, runtime));
        ArgumentException inputError = Assert.Throws<ArgumentException>(() => new ResourceManager(
            "input", new UnusedSource(), [new InvalidStage(typeof(object), invalidType)], memory, runtime));
        Assert.Contains("nonnullable", outputError.Message, StringComparison.Ordinal);
        Assert.Contains("nonnullable", inputError.Message, StringComparison.Ordinal);
    }

    private static int SumValues(ResourceLease<InputValue> lease, int count)
    {
        int sum = 0;
        for (int index = 0; index < count; index++)
        {
            sum += lease.Value.Owner.Number;
        }

        return sum;
    }

    private readonly record struct InputValue(InputOwner Owner);

    private readonly record struct OutputValue(OutputOwner Owner);

    private sealed class InputOwner
    {
        internal int Number => 42;
    }

    private sealed class OutputOwner(InputOwner input)
    {
        internal InputOwner Input { get; } = input;

        internal int ReleaseAttempts { get; set; }
    }

    private sealed class TestState
    {
        internal int InputBuilds { get; set; }

        internal int OutputBuilds { get; set; }

        internal bool FailOutputRelease { get; set; }

        internal List<string> Releases { get; } = [];
    }

    private sealed class InputStage(TestState state) : IResourceSourceStage<InputValue, ResourceRuntime>
    {
        public async ValueTask<InputValue> BuildAsync(ResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Assert.Same(runtime, context.Runtime);
            state.InputBuilds++;
            await Task.Yield();
            return new(new());
        }

        public ValueTask ReleaseAsync(InputValue resource, ResourceRuntime runtime)
        {
            Assert.Equal(42, resource.Owner.Number);
            state.Releases.Add("input");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OutputStage(TestState state) : IResourceTransform<InputValue, OutputValue, ResourceRuntime>
    {
        public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Resident;

        public ResourceAddress GetInput(ResourceAddress output) => output;

        public ValueTask<OutputValue> BuildAsync(InputValue input, ResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Assert.Same(runtime, context.Runtime);
            state.OutputBuilds++;
            return ValueTask.FromResult(new OutputValue(new(input.Owner)));
        }

        public ValueTask ReleaseAsync(OutputValue resource, ResourceRuntime runtime)
        {
            resource.Owner.ReleaseAttempts++;
            if (state.FailOutputRelease)
            {
                state.FailOutputRelease = false;
                state.Releases.Add("output failed");
                throw new IOException("Release must be retried.");
            }

            state.Releases.Add("output");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScalarStage : IResourceSourceStage<int>
    {
        internal int Releases { get; private set; }

        public ValueTask<int> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(0);

        public ValueTask ReleaseAsync(int resource)
        {
            Assert.Equal(0, resource);
            Releases++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class InvalidStage(Type output, Type? input = null) : IResourceStage
    {
        public Type OutputType => output;

        public Type? InputType => input;

        public ValueTask<object> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask ReleaseAsync(object resource) => throw new NotSupportedException();
    }

    private sealed class InlineRuntime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource)
            => stage.ReleaseAsync(resource, runtime);
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);
    }
}
