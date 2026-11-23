#pragma warning disable xUnit1000 // Private probe types are run explicitly, never discovered as suite tests.
#pragma warning disable SA1401 // Probe state must be inspectable by the enclosing contract tests.
#pragma warning disable SA1204 // Keep probe helpers next to their use.
using System.Collections.Concurrent;
using Lumyte.Testing;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// Runner fixtures own their real concurrency inside Integration, never a CPU payload.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Lumyte.TestRunner.Tests;

/// <summary>Exercises the real xUnit method/case runners without spawning per-method processes.</summary>
[Collection("Runner contracts")]
[Trait("Execution", "Integration")]
public sealed class MethodRunnerTests
{
    /// <summary>Two methods in one class must meet a barrier, with shared and per-test async lifetimes intact.</summary>
    /// <returns>The regression run.</returns>
    [Fact]
    public async Task SameClassMethodsOverlapAndPreserveLifetimesAsync()
    {
        using var barrier = new Barrier(2);
        OverlapProbe.Barrier = barrier;
        OverlapProbe.Initialized = 0;
        OverlapProbe.Disposed = 0;
        SharedFixture.Created = 0;
        SharedFixture.Disposed = 0;
        var messages = new RecordingSink();
        RunSummary summary = await RunAsync(typeof(OverlapProbe), 2, messages: messages);
        Assert.Equal(2, summary.Total);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(2, OverlapProbe.Initialized);
        Assert.Equal(2, OverlapProbe.Disposed);
        Assert.Equal(1, SharedFixture.Created);
        Assert.Equal(1, SharedFixture.Disposed);
        Assert.Equal(new[] { "First output" + Environment.NewLine, "Second output" + Environment.NewLine }, messages.Output.Order());
    }

    /// <summary>The bound includes async methods suspended at an await, not just active threads.</summary>
    /// <returns>The bounded execution.</returns>
    [Fact]
    public async Task PendingAsyncMethodsRemainBoundedAsync()
    {
        BoundedProbe.Reset();
        Task<RunSummary> run = RunAsync(typeof(BoundedProbe), 2);
        await BoundedProbe.TwoEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, Volatile.Read(ref BoundedProbe.Active));
        Assert.False(run.IsCompleted);
        BoundedProbe.Release.TrySetResult();
        RunSummary summary = await run;
        Assert.Equal(4, summary.Total);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(2, BoundedProbe.Maximum);
    }

    /// <summary>Theory rows remain sequential within one method and retain their arguments.</summary>
    /// <returns>The theory execution.</returns>
    [Fact]
    public async Task TheoryRowsKeepArgumentsAndSequentialLifetimeAsync()
    {
        TheoryProbe.Active = 0;
        TheoryProbe.Seen = new ConcurrentBag<int>();
        RunSummary summary = await RunAsync(typeof(TheoryProbe), 2, theory: true);
        Assert.Equal(3, summary.Total);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(new[] { 1, 2, 3 }, TheoryProbe.Seen.Order());
    }

    /// <summary>Queued methods drain after cancellation without faulting the executor or starting new tests.</summary>
    /// <returns>The cancellation check.</returns>
    [Fact]
    public async Task CancellationDrainsQueuedMethodsAsync()
    {
        BoundedProbe.Reset();
        using var cancellation = new CancellationTokenSource();
        Task<RunSummary> run = RunAsync(typeof(BoundedProbe), 1, cancellation);
        await BoundedProbe.OneEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        BoundedProbe.Release.TrySetResult();
        RunSummary summary = await run;
        Assert.Equal(1, summary.Total);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(1, BoundedProbe.Maximum);
    }

    /// <summary>Explicit serial classes still use the shared global bound and never overlap their methods.</summary>
    /// <returns>The serial check.</returns>
    [Fact]
    public async Task SerialCollectionMethodsRemainSequentialAsync()
    {
        BoundedProbe.Reset();
        BoundedProbe.Release.TrySetResult();
        RunSummary summary = await RunAsync(typeof(BoundedProbe), 4, parallel: false);
        Assert.Equal(4, summary.Total);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(1, BoundedProbe.Maximum);
    }

    /// <summary>Failures and skips are reported through the original xUnit message protocol.</summary>
    /// <returns>The report check.</returns>
    [Fact]
    public async Task FailureAndSkipSummariesArePreservedAsync()
    {
        RunSummary summary = await RunAsync(typeof(ResultProbe), 2);
        Assert.Equal(3, summary.Total);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Skipped);
    }

    /// <summary>An invalid orderer reports an xUnit infrastructure error, not an unhandled async exception.</summary>
    /// <returns>The failure protocol check.</returns>
    [Fact]
    public async Task OrdererFailuresUseXunitErrorMessagesAsync()
    {
        var messages = new RecordingSink();
        RunSummary summary = await RunAsync(typeof(ResultProbe), 2, messages: messages, orderer: new ThrowingOrderer());
        Assert.Equal(0, summary.Total);
        Assert.Equal(1, messages.Errors);
    }

    private static async Task<RunSummary> RunAsync(Type type, int concurrency, CancellationTokenSource? cancellation = null, bool theory = false, bool parallel = true, RecordingSink? messages = null, ITestCaseOrderer? orderer = null)
    {
        var sink = new NullMessageSink();
        var assembly = new TestAssembly(new ReflectionAssemblyInfo(type.Assembly));
        var collection = new TestCollection(assembly, parallel ? null : new ReflectionTypeInfo(typeof(SerialDefinition)), "Probe");
        var reflectionType = new ReflectionTypeInfo(type);
        var testClass = new TestClass(collection, reflectionType);
        IXunitTestCase[] cases = type.GetMethods().Where(method => method.IsDefined(typeof(FactAttribute), true))
            .SelectMany(method =>
            {
                var testMethod = new TestMethod(testClass, new ReflectionMethodInfo(method));
                return theory
                    ? Enumerable.Range(1, 3).Select(value => (IXunitTestCase)new XunitTestCase(sink, TestMethodDisplay.ClassAndMethod, TestMethodDisplayOptions.None, testMethod, [value]))
                    : [new XunitTestCase(sink, TestMethodDisplay.ClassAndMethod, TestMethodDisplayOptions.None, testMethod)];
            }).ToArray();
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        using var ownCancellation = new CancellationTokenSource();
        using var bus = new MessageBus(messages ?? (IMessageSink)sink);
        var runner = new CpuMethodCollectionRunner(collection, cases, sink, bus, orderer ?? new DefaultTestCaseOrderer(sink), new ExceptionAggregator(), cancellation ?? ownCancellation, gate);
        return await runner.RunAsync();
    }

    private sealed class RecordingSink : LongLivedMarshalByRefObject, IMessageSink
    {
        public int Errors { get; private set; }

        public ConcurrentBag<string> Output { get; } = new();

        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestPassed passed)
            {
                Output.Add(passed.Output);
            }

            if (message is IErrorMessage)
            {
                Errors++;
            }

            return true;
        }
    }

#pragma warning disable xUnit1027 // Synthetic definition is supplied directly, not discovered.
    [CollectionDefinition("Probe serial", DisableParallelization = true)]
    private sealed class SerialDefinition
    {
    }
#pragma warning restore xUnit1027

    private sealed class ThrowingOrderer : LongLivedMarshalByRefObject, ITestCaseOrderer
    {
        public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
            where TTestCase : ITestCase
            => throw new InvalidOperationException("Intentional orderer failure");
    }

    private sealed class SharedFixture : IAsyncLifetime
    {
        public static int Created;
        public static int Disposed;

        public bool Ready { get; private set; }

        public Task InitializeAsync()
        {
            Interlocked.Increment(ref Created);
            Ready = true;
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            Interlocked.Increment(ref Disposed);
            return Task.CompletedTask;
        }
    }

    private sealed class OverlapProbe(SharedFixture fixture, ITestOutputHelper output) : IClassFixture<SharedFixture>, IAsyncLifetime
    {
        public static Barrier Barrier = null!;
        public static int Initialized;
        public static int Disposed;
        private bool _ready;

        [Fact]
        public void First()
        {
            Assert.True(_ready);
            Assert.True(fixture.Ready);
            output.WriteLine("First output");
            Assert.True(Barrier.SignalAndWait(TimeSpan.FromSeconds(10)), "Same-class methods did not overlap.");
        }

        [Fact]
        public async Task SecondAsync()
        {
            Assert.True(_ready);
            Assert.True(fixture.Ready);
            output.WriteLine("Second output");
            Assert.True(Barrier.SignalAndWait(TimeSpan.FromSeconds(10)), "Same-class methods did not overlap.");
            await Task.Yield();
        }

        public Task InitializeAsync()
        {
            _ready = true;
            Interlocked.Increment(ref Initialized);
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            Interlocked.Increment(ref Disposed);
            return Task.CompletedTask;
        }
    }

    private sealed class BoundedProbe
    {
        public static TaskCompletionSource OneEntered = null!;
        public static TaskCompletionSource TwoEntered = null!;
        public static TaskCompletionSource Release = null!;
        public static int Active;
        public static int Maximum;

        public static void Reset()
        {
            OneEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TwoEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Active = 0;
            Maximum = 0;
        }

        [Fact]
        public Task FirstAsync() => HoldAsync();

        [Fact]
        public Task SecondAsync() => HoldAsync();

        [Fact]
        public Task ThirdAsync() => HoldAsync();

        [Fact]
        public Task FourthAsync() => HoldAsync();

        private static async Task HoldAsync()
        {
            int active = Interlocked.Increment(ref Active);
            InterlockedExtensions.Max(ref Maximum, active);
            OneEntered.TrySetResult();
            if (active == 2)
            {
                TwoEntered.TrySetResult();
            }

            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Yield();
            Interlocked.Decrement(ref Active);
        }
    }

    private sealed class TheoryProbe
    {
        public static int Active;
        public static ConcurrentBag<int> Seen = new();

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task RowAsync(int value)
        {
            Assert.Equal(1, Interlocked.Increment(ref Active));
            await Task.Yield();
            Seen.Add(value);
            Interlocked.Decrement(ref Active);
        }
    }

    private sealed class ResultProbe
    {
        [Fact]
        public void Pass() => Assert.True(true);

        [Fact]
        public void Fail() => Assert.Fail("Intentional inner failure");

        [Fact(Skip = "Intentional inner skip")]
        public void Skip() => Assert.Fail("Must not run");
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref target);
                if (current >= value)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref target, value, current) != current);
        }
    }
}
