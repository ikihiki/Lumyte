using System.Reflection;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Exercises real compiled upload processors with managed storage, never native execution.</summary>
public sealed class UploadRetentionTests
{
    /// <summary>Original constructors/helpers and explicit Source all retain exact completed source bytes.</summary>
    /// <param name="declaration">The constructor/helper form.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SourceDeclarationsShareOnlyPublishedSource(int declaration)
    {
        Upload<Words> upload = declaration switch
        {
            0 => new Upload<Words>(),
            1 => Transfers.Upload<Words>(),
            _ => Transfers.Upload<Words>(UploadRetention.Source),
        };
        using var fixture = new Fixture(upload);
        using PipelineResult<Device<Words>> first = fixture.Prepare(0);
        IGraphicsBuffer<uint> firstBuffer = first.Value.Value!.GetBuffer<uint>().Buffer;
        Assert.Equal(8, fixture.Cache.Snapshot().Created);
        using (PipelineResult<Device<Words>> pending = fixture.Prepare(0))
        {
            Assert.NotSame(firstBuffer, pending.Value.Value!.GetBuffer<uint>().Buffer);
            Assert.Equal(16, fixture.Cache.Snapshot().Created);
            Assert.Equal(0, fixture.Cache.Snapshot().Hits);
        }

        using GraphicsPipelineRecordPlan plan = fixture.Record(first);

        // Unit-level completion signal only: this does not claim recording or GPU completion.
        plan.State.Run.PublishImmutableUploads();
        object[] originalOwners = fixture.Cache.Snapshot().Owners;
        using PipelineResult<Device<Words>> second = fixture.Prepare(0);
        Assert.Equal(originalOwners, fixture.Cache.Snapshot().Owners);
        Assert.Equal(8, fixture.Cache.Snapshot().Hits);
        Assert.Equal(16, fixture.Cache.Snapshot().Created);
        Assert.Equal(128, fixture.Cache.Snapshot().Bytes);
        Assert.All(fixture.Cache.Snapshot().Owners, owner => Assert.False(((IGraphicsBuffer<uint>)owner).Usage.HasFlag(BufferUsage.ShaderWrite)));
        Assert.Equal(Enumerable.Range(0, 32).Select(value => (uint)value), ((MemoryBuffer)firstBuffer).Values);
        using PipelineResult<Device<Words>> changed = fixture.Prepare(1);
        Assert.Equal(24, fixture.Cache.Snapshot().Created);
        Assert.Equal(256, fixture.Cache.Snapshot().Bytes);
        Assert.Equal(Enumerable.Range(0, 32).Select(value => (uint)value), ((MemoryBuffer)firstBuffer).Values);
        Assert.All(originalOwners, owner => Assert.False(((MemoryBuffer)owner).Disposed));
    }

    /// <summary>Fresh identical and translated payloads neither share storage nor consume immutable capacity.</summary>
    /// <param name="capacity">The source cache ceiling, including the zero-admission case.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    public void FreshDerivedTransfersNeverEnterSourceCache(int capacity)
    {
        using var fixture = new Fixture(Transfers.Upload<Words>(UploadRetention.Fresh), capacity);
        using PipelineResult<Device<Words>> first = fixture.Prepare(0);
        using PipelineResult<Device<Words>> identical = fixture.Prepare(0);
        using PipelineResult<Device<Words>> translated = fixture.Prepare(1);
        using GraphicsPipelineRecordPlan firstPlan = fixture.Record(first);
        using GraphicsPipelineRecordPlan identicalPlan = fixture.Record(identical);
        using GraphicsPipelineRecordPlan plan = fixture.Record(translated);
        var firstBuffer = (MemoryBuffer)first.Value.Value!.GetBuffer<uint>().Buffer;
        var identicalBuffer = (MemoryBuffer)identical.Value.Value!.GetBuffer<uint>().Buffer;
        var translatedBuffer = (MemoryBuffer)translated.Value.Value!.GetBuffer<uint>().Buffer;
        Assert.NotSame(firstBuffer, identicalBuffer);
        Assert.NotSame(firstBuffer, translatedBuffer);
        Assert.Equal(Enumerable.Range(0, 32).Select(value => (uint)value), firstBuffer.Values);
        Assert.Equal(Enumerable.Range(1, 32).Select(value => (uint)value), translatedBuffer.Values);
        plan.State.Run.PublishImmutableUploads();
        Assert.Equal(0, fixture.Cache.Snapshot().Created);
        Assert.Equal(0, fixture.Cache.Snapshot().Hits);
        Assert.Equal(0, fixture.Cache.Snapshot().Bytes);
        Assert.Empty(fixture.Cache.Snapshot().Owners);
        Assert.Equal(6, fixture.Device.Buffers.Count);
        firstPlan.Dispose();
        identicalPlan.Dispose();
        first.Dispose();
        identical.Dispose();
        plan.Dispose();
        translated.Dispose();
        Assert.All(fixture.Device.Buffers, buffer => Assert.True(buffer.Disposed));
    }

    /// <summary>Writer preflight observes the same edge declaration, including through independent device uses.</summary>
    /// <param name="retention">The immutable transfer declaration.</param>
    /// <param name="copy">Whether the write is a copy rather than shader access.</param>
    [Theory]
    [InlineData(UploadRetention.Source, false)]
    [InlineData(UploadRetention.Source, true)]
    [InlineData(UploadRetention.Fresh, false)]
    [InlineData(UploadRetention.Fresh, true)]
    public void WriterPreflightMatchesTransferEligibility(UploadRetention retention, bool copy)
    {
        using var fixture = new Fixture();
        var graph = new PipelineGraph<int>();
        Port<Device<Words>> input = graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>(retention)).Then(Transfers.Passthrough<Words>());
        var writer = new Writer(copy);
        if (retention == UploadRetention.Source)
        {
            Assert.Throws<NotSupportedException>(() => fixture.Build.Build(graph.Complete(graph.Stage(writer, input))));
            Assert.Equal(0, writer.Creates);
        }
        else
        {
            Assert.Throws<ProcessorReachedException>(() => fixture.Build.Build(graph.Complete(graph.Stage(writer, input))));
            Assert.Equal(1, writer.Creates);
        }

        Assert.Empty(fixture.Device.Buffers);
    }

    /// <summary>Invalid declarations are rejected before acquiring any native owner.</summary>
    [Fact]
    public void InvalidRetentionRejectsAtDeclaration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Upload<Words>((UploadRetention)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Transfers.Upload<Words>((UploadRetention)(-1)));
    }

    /// <summary>A cancelled mapped Fresh upload cleans its run and a fresh retry uses no cache credit.</summary>
    [Fact]
    public void CancelledFreshUploadReleasesOwnersAndRetries()
    {
        using var fixture = new Fixture(Transfers.Upload<Words>(UploadRetention.Fresh), 0);
        using var cancellation = new CancellationTokenSource();
        fixture.Device.OnMap = cancellation.Cancel;
        Assert.ThrowsAny<OperationCanceledException>(() => fixture.Prepare(0, cancellation.Token));
        Assert.All(fixture.Device.Buffers, buffer => Assert.True(buffer.Disposed));
        Assert.All(fixture.Device.Buffers, buffer => Assert.False(buffer.IsMapped));
        fixture.Device.OnMap = null;
        using PipelineResult<Device<Words>> retry = fixture.Prepare(1);
        Assert.Equal(0, fixture.Cache.Snapshot().Created);
        Assert.Empty(fixture.Cache.Snapshot().Owners);
    }

    /// <summary>Map/copy faults preserve run cleanup and allow a fresh retry without immutable admission.</summary>
    /// <param name="map">Whether failure occurs before mapping rather than during host copy.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedFreshTransferCleansAndRetries(bool map)
    {
        using var fixture = new Fixture(Transfers.Upload<Words>(UploadRetention.Fresh), 0);
        fixture.Device.FailMap = map;
        fixture.Device.FailCopy = !map;
        Assert.Throws<TransferFailureException>(() => fixture.Prepare(0));
        Assert.All(fixture.Device.Buffers, buffer => Assert.True(buffer.Disposed));
        Assert.All(fixture.Device.Buffers, buffer => Assert.False(buffer.IsMapped));
        fixture.Device.FailMap = false;
        fixture.Device.FailCopy = false;
        using PipelineResult<Device<Words>> retry = fixture.Prepare(1);
        Assert.Equal(0, fixture.Cache.Snapshot().Created);
        Assert.Empty(fixture.Cache.Snapshot().Owners);
    }

    /// <summary>A failed fresh-buffer cleanup remains owned until compiled-pipeline disposal retries it.</summary>
    [Fact]
    public void FailedFreshCleanupRemainsOwnedForRetry()
    {
        using var fixture = new Fixture(Transfers.Upload<Words>(UploadRetention.Fresh), 0);
        fixture.Device.FailCopy = true;
        fixture.Device.FailDisposeOnce = true;
        Assert.Throws<AggregateException>(() => fixture.Prepare(0));
        Assert.Contains(fixture.Device.Buffers, buffer => !buffer.Disposed);
        Assert.Empty(fixture.Cache.Snapshot().Owners);
        fixture.Pipeline.Dispose();
        Assert.All(fixture.Device.Buffers, buffer => Assert.True(buffer.Disposed));
        Assert.All(fixture.Device.Buffers, buffer => Assert.False(buffer.IsMapped));
    }

    /// <summary>Permits the managed device proxy to participate in real processor preparation.</summary>
    public class DeviceProxy : DispatchProxy
    {
        internal IGraphicDevice Device { get; set; } = null!;

        internal List<MemoryBuffer> Buffers { get; } = [];

        internal Action? OnMap { get; set; }

        internal bool FailMap { get; set; }

        internal bool FailCopy { get; set; }

        internal bool FailDisposeOnce { get; set; }

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "get_Status": return GraphicsDeviceStatus.Ready;
                case "get_Caps": return new DeviceCaps { MaxComputeInvocationsPerWorkgroup = 64 };
                case "CreateBuffer":
                    var buffer = new MemoryBuffer(Device, (BufferDesc<uint>)args![0]!, this);
                    Buffers.Add(buffer);
                    return buffer;
                default: throw new NotSupportedException(targetMethod.Name);
            }
        }
    }

    /// <summary>Executes only managed buffer copies and records no native work.</summary>
    public class UploadCommandsProxy : DispatchProxy
    {
        internal IGraphicDevice Device { get; set; } = null!;

        internal CommandBufferState State { get; private set; } = CommandBufferState.Recording;

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "get_Device": return Device;
                case "get_State": return State;
                case "ValidateOrigin": return null;
                case "Barrier": return null;
                case "Dispose": State = CommandBufferState.Disposed; return null;
                case "CopyBuffer":
                    var source = (BufferSlice<uint>)args![0]!;
                    var destination = (BufferSlice<uint>)args[1]!;
                    Assert.False(source.Buffer.IsMapped);
                    Assert.False(destination.Buffer.IsMapped);
                    ((MemoryBuffer)source.Buffer).Values.AsSpan((int)source.Offset, (int)source.Count)
                        .CopyTo(((MemoryBuffer)destination.Buffer).Values.AsSpan((int)destination.Offset, (int)destination.Count));
                    return null;
                default: throw new NotSupportedException(targetMethod.Name);
            }
        }
    }

    internal sealed class MemoryBuffer(IGraphicDevice device, BufferDesc<uint> desc, DeviceProxy owner) : IGraphicsBuffer<uint>, IGraphicsNativeOrigin
    {
        public IGraphicDevice Device => device;

        public object AllocationIdentity => this;

        public ulong SizeInBytes => Count * 4;

        public BufferLayout<uint> Layout => new(4, 4, 1, 1);

        public ulong Count => desc.Count;

        public BufferUsage Usage => desc.Usage;

        public MemoryPreference Memory => desc.Memory;

        public bool IsMapped { get; private set; }

        internal uint[] Values { get; } = new uint[checked((int)desc.Count)];

        internal bool Disposed { get; private set; }

        public void ValidateOrigin() => ObjectDisposedException.ThrowIf(Disposed, this);

        public void ValidateByteRange(ulong offset, ulong length) => Assert.True(offset <= SizeInBytes && length <= SizeInBytes - offset);

        public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length) => throw new NotSupportedException();

        public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => throw new NotSupportedException();

        public ValueTask MapAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner.FailMap)
            {
                throw new TransferFailureException();
            }

            IsMapped = true;
            owner.OnMap?.Invoke();
            return ValueTask.CompletedTask;
        }

        public void Unmap() => IsMapped = false;

        public BufferSlice<uint> SliceElements(ulong offset, ulong count) => new(this, offset, count);

        public void CopyFrom(ReadOnlySpan<uint> source)
        {
            Assert.True(IsMapped);
            if (owner.FailCopy)
            {
                throw new TransferFailureException();
            }

            source.CopyTo(Values);
        }

        public void CopyTo(Span<uint> destination) => throw new NotSupportedException();

        public void Dispose()
        {
            if (owner.FailDisposeOnce)
            {
                owner.FailDisposeOnce = false;
                throw new TransferFailureException();
            }

            Disposed = true;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ExecutionRuntime _runtime = new([]);

        internal Fixture(Upload<Words>? upload = null, int cacheBytes = 1024)
        {
            IGraphicDevice device = DispatchProxy.Create<IGraphicDevice, DeviceProxy>();
            Device = (DeviceProxy)(object)device;
            Device.Device = device;
            _runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new());
            Pump(_runtime.StartAsync().AsTask());
            Build = new(_runtime, device, new NoArtifacts(), new(65536, 1024, 1024, 4) { ImmutableUploads = new(cacheBytes, 64, 16) });
            var graph = new PipelineGraph<int>();
            Pipeline = Build.Build(graph.Complete(graph.Stage(new Producer(), graph.Input).Then(upload ?? Transfers.Upload<Words>())));
        }

        internal DeviceProxy Device { get; }

        internal GraphicsPipelineBuildHost Build { get; }

        internal IPipeline<int, Device<Words>> Pipeline { get; }

        internal ImmutableUploadCache Cache => ((IGraphicsPipelineInfo)Pipeline).ImmutableUploads!;

        public void Dispose()
        {
            Pipeline.Dispose();
            Build.Dispose();
            Pump(_runtime.StopAsync().AsTask());
        }

        internal GraphicsPipelineRecordPlan Record(PipelineResult<Device<Words>> result)
        {
            GraphicsPipelineRecordPlan plan = result.AcquireRecordPlan(Pipeline, new InlineContext());
            TransferSynchronizationTests.IOriginCommands commands = DispatchProxy.Create<TransferSynchronizationTests.IOriginCommands, UploadCommandsProxy>();
            ((UploadCommandsProxy)(object)commands).Device = Device.Device;
            try
            {
                plan.Record(commands);
            }
            finally
            {
                commands.Dispose();
            }

            return plan;
        }

        internal PipelineResult<Device<Words>> Prepare(int translation, CancellationToken token = default)
        {
            Task<PipelineResult<Device<Words>>> task = Pipeline.PrepareAsync(translation, token).AsTask();
            Pump(task);
            return task.GetAwaiter().GetResult();
        }

        private void Pump(Task task)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                _runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
                _runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
                if (timeout.Elapsed > TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException();
                }

                Thread.Yield();
            }

            task.GetAwaiter().GetResult();
        }
    }

    private sealed class Words : ISchema
    {
        public static SchemaDescriptor Descriptor { get; } = new(1, typeof(uint), 4, new Dictionary<string, object>());
    }

    private sealed class Producer : IStage<int, Host<Words>>, IStageFactory<int, Host<Words>>, IStageProcessor<int, Host<Words>>
    {
        public IStageFactory<int, Host<Words>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(512, []) { OutputMaxElements = 32 };

        public IStageProcessor<int, Host<Words>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<Words>>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken)
        {
            int translation = context.Read(a);
            uint[] values = Enumerable.Range(translation, 32).Select(value => checked((uint)value)).ToArray();
            return ValueTask.FromResult(context.PublishHost(context.CopyHostData<Words, uint>(values, new(32, new Dictionary<string, object>()))));
        }

        public void Dispose()
        {
        }
    }

    private sealed class Writer(bool copy) : IStage<Device<Words>, Device<Words>>, IStageFactory<Device<Words>, Device<Words>>
    {
        public IStageFactory<Device<Words>, Device<Words>> Factory => this;

        internal int Creates { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => new(0, [new GraphicsStageRequirements(GraphicsExecutionDomain.Compute, 128, [], [new(0, copy ? ResourceAccess.CopyWrite : ResourceAccess.ShaderWrite)])]);

        public IStageProcessor<Device<Words>, Device<Words>> Create(StageBuildContext context)
        {
            Creates++;
            throw new ProcessorReachedException();
        }
    }

    private sealed class ProcessorReachedException : Exception;

    private sealed class TransferFailureException : Exception;

    private sealed class NoArtifacts : IStageArtifactProvider
    {
        public StageArtifactDescription Describe(StageArtifactKey key, IGraphicDevice device) => throw new NotSupportedException();

        public StageArtifactLease Acquire(StageArtifactKey key, StageArtifactBuildContext context) => throw new NotSupportedException();
    }

    private sealed class InlineContext : IGraphicsOwnerContext
    {
        public ValueTask InvokeAsync(Func<ValueTask> operation) => operation();
    }
}
