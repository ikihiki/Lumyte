using System.Numerics;
using System.Reflection;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises shared output descriptions through real compiled preparation, without native execution.</summary>
public sealed class GenericOutputExtentTests
{
    /// <summary>Exposes deterministic managed owner cleanup.</summary>
    internal interface IManagedBuffer
    {
        /// <summary>Gets a value indicating whether the owner was returned.</summary>
        bool Disposed { get; }
    }

    /// <summary>The shared producer must admit readback preparation before any recording or submission.</summary>
    /// <param name="paged">Whether to use the paged description.</param>
    /// <returns>The managed preparation check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompiledCoverageProducerAdmitsDownloadPreparationAsync(bool paged)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new());
        Pump(runtime, runtime.StartAsync().AsTask());
        IGraphicDevice device = DispatchProxy.Create<IGraphicDevice, ManagedDevice>();
        var managed = (ManagedDevice)(object)device;
        managed.Device = device;
        using var build = new GraphicsPipelineBuildHost(runtime, device, new NoArtifacts(), new(256L * 1048576, 1024, 16L * 1048576, 2));
        var graph = new PipelineGraph<int>();
        using IPipeline<int, Host<Coverage16V1>> pipeline = build.Build(graph.Complete(graph.Stage(new GeometryProducer(), graph.Input).Then(Transfers.Upload<FrameV1>(UploadRetention.Fresh)).Then(new CoverageProducer(paged)).Then(new Download<Coverage16V1>())));
        using var cancellation = new CancellationTokenSource();
        PipelineResult<Host<Coverage16V1>>? result = null;
        try
        {
            Task<PipelineResult<Host<Coverage16V1>>> preparation = pipeline.PrepareAsync(0, cancellation.Token).AsTask();
            Pump(runtime, preparation);
            result = await preparation;
            ManagedBuffer<float>[] outputs = managed.Buffers.OfType<ManagedBuffer<float>>().ToArray();
            Assert.Equal(2, outputs.Length);
            Assert.All(outputs, buffer => Assert.Equal(1052672UL, buffer.Count));
            Assert.Equal(MemoryPreference.Readback, outputs[1].Memory);
        }
        finally
        {
            // Stop at the real scheduler-owned readback boundary; never submit work.
            cancellation.Cancel();
            if (result is not null)
            {
                ReturnResult(runtime, result);
            }

            Pump(runtime, runtime.StopAsync().AsTask());
        }

        Assert.All(managed.Buffers, buffer => Assert.True(((IManagedBuffer)buffer).Disposed));
    }

    /// <summary>Compiled preparation rejects empty output, excessive samples and arithmetic overflow without native allocation.</summary>
    /// <param name="paged">Whether to use the paged description.</param>
    /// <param name="width">The captured width.</param>
    /// <param name="commands">The captured command count.</param>
    /// <param name="errorType">The original rejection type.</param>
    /// <returns>The rejected preparation check.</returns>
    [Theory]
    [InlineData(false, 1L, 0L, typeof(InvalidOperationException))]
    [InlineData(true, 1L, 0L, typeof(InvalidOperationException))]
    [InlineData(false, 0L, 1L, typeof(NotSupportedException))]
    [InlineData(true, 0L, 1L, typeof(NotSupportedException))]
    [InlineData(false, 1048577L, 1L, typeof(NotSupportedException))]
    [InlineData(true, 1048577L, 1L, typeof(NotSupportedException))]
    [InlineData(false, long.MaxValue, 2L, typeof(OverflowException))]
    [InlineData(true, long.MaxValue, 2L, typeof(OverflowException))]
    public async Task CompiledProducerRejectsInvalidGeometryAsync(bool paged, long width, long commands, Type errorType)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new());
        Pump(runtime, runtime.StartAsync().AsTask());
        IGraphicDevice device = DispatchProxy.Create<IGraphicDevice, ManagedDevice>();
        var managed = (ManagedDevice)(object)device;
        managed.Device = device;
        try
        {
            using var build = new GraphicsPipelineBuildHost(runtime, device, new NoArtifacts(), new(256L * 1048576, 1024, 16L * 1048576, 2));
            var graph = new PipelineGraph<int>();
            using IPipeline<int, Host<Coverage16V1>> pipeline = build.Build(graph.Complete(graph.Stage(new GeometryProducer(width, commands), graph.Input).Then(Transfers.Upload<FrameV1>(UploadRetention.Fresh)).Then(new CoverageProducer(paged)).Then(new Download<Coverage16V1>())));
            Task<PipelineResult<Host<Coverage16V1>>> preparation = pipeline.PrepareAsync(0).AsTask();
            Exception? error = Record.Exception(() => Pump(runtime, preparation));
            Assert.IsType(errorType, error);
            await Assert.ThrowsAsync(errorType, async () => await preparation);
            Assert.Empty(managed.Buffers.OfType<ManagedBuffer<float>>());
            Assert.All(managed.Buffers, buffer => Assert.True(((IManagedBuffer)buffer).Disposed));
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    /// <summary>Every production factory describes canonical elements, not bytes, in build and actual-input phases.</summary>
    /// <returns>The CPU contract probe.</returns>
    [Fact]
    public async Task ProductionFactoriesPreserveLogicalShapeAsync()
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new());
        Pump(runtime, runtime.StartAsync().AsTask());
        try
        {
            using var build = new PipelineBuildHost(runtime, new(4, 4096, 1));
            var graph = new PipelineGraph<int>();
            var probe = new ShapeProbe();
            using IPipeline<int, int> pipeline = build.Build(graph.Complete(graph.Stage(new GeometryProducer(), graph.Input).Then(probe)));
            Task<PipelineResult<int>> preparation = pipeline.PrepareAsync(0).AsTask();
            Pump(runtime, preparation);
            using PipelineResult<int> result = await preparation;
            Assert.Equal(1, result.Value);
            Assert.Equal(1, probe.BuildDescriptions);
            Assert.Equal(1, probe.ActualDescriptions);
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    /// <summary>Empty geometry, excessive samples and overflow keep their original behavior.</summary>
    [Fact]
    public void GeometryBoundariesRemainExplicit()
    {
        Assert.Equal(0, GenericGpuPreparation2D.Count(Extent(1, 1, 0)));
        Assert.Throws<NotSupportedException>(() => GenericGpuPreparation2D.Count(Extent(0, 1, 1)));
        Assert.Throws<NotSupportedException>(() => GenericGpuPreparation2D.Count(Extent(1048577, 1, 1)));
        Assert.Throws<OverflowException>(() => GenericGpuPreparation2D.Count(Extent(long.MaxValue, 2, 1)));
        Assert.Equal(GenericGpuPreparation2D.MaxSampleEntries, GenericGpuPreparation2D.Count(Extent(1048576, 1, 1)));
    }

    private static SchemaExtent Extent(long width, long height, long commands) => new(1, new Dictionary<string, object>
    {
        ["source"] = new Guid("1d681218-25a5-4303-91f7-f63497918b87"),
        ["samples"] = 16,
        ["gridVersion"] = 1,
        ["width"] = width,
        ["height"] = height,
        ["commandCount"] = commands,
        ["viewCount"] = 0,
        ["samplerCount"] = 0,
    });

    private static void ReturnResult(ExecutionRuntime runtime, PipelineResult<Host<Coverage16V1>> result)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                result.Dispose();
                return;
            }
            catch (InvalidOperationException error) when (error.Message == "The segmented preparation still owns unfinished CPU continuations." && timeout.Elapsed < TimeSpan.FromSeconds(10))
            {
                // The public disposal contract requires pumping and retrying after aborting an unaccepted execution.
                runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
                runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
                Thread.Yield();
            }
        }
    }

    private static void Pump(ExecutionRuntime runtime, Task task)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            if (timeout.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException();
            }

            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    /// <summary>A managed device supporting only preparation of canonical float buffers.</summary>
    public class ManagedDevice : DispatchProxy
    {
        internal IGraphicDevice Device { get; set; } = null!;

        internal List<IDisposable> Buffers { get; } = [];

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "get_Status": return GraphicsDeviceStatus.Ready;
                case "get_Caps": return new DeviceCaps { MaxComputeInvocationsPerWorkgroup = 64 };
                case "CreateBuffer":
                    IDisposable buffer = args![0] is BufferDesc<float> floats
                        ? new ManagedBuffer<float>(Device, floats)
                        : new ManagedBuffer<Vector4>(Device, (BufferDesc<Vector4>)args[0]!);
                    Buffers.Add(buffer);
                    return buffer;
                default: throw new NotSupportedException(targetMethod.Name);
            }
        }
    }

    internal sealed class ManagedBuffer<T>(IGraphicDevice device, BufferDesc<T> desc) : IGraphicsBuffer<T>, IGraphicsNativeOrigin, IManagedBuffer
        where T : unmanaged
    {
        public IGraphicDevice Device => device;

        public object AllocationIdentity => this;

        public ulong SizeInBytes => Count * (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>();

        public BufferLayout<T> Layout => new((uint)System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), (uint)System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), 1, 1);

        public ulong Count => desc.Count;

        public BufferUsage Usage => desc.Usage;

        public MemoryPreference Memory => desc.Memory;

        public bool IsMapped { get; private set; }

        public bool Disposed { get; private set; }

        public void ValidateOrigin() => ObjectDisposedException.ThrowIf(Disposed, this);

        public void ValidateByteRange(ulong offset, ulong length) => Assert.True(offset <= SizeInBytes && length <= SizeInBytes - offset);

        public BufferSlice<T> SliceElements(ulong offset, ulong count) => new(this, offset, count);

        public ValueTask MapAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsMapped = true;
            return ValueTask.CompletedTask;
        }

        public void Unmap() => IsMapped = false;

        public void CopyFrom(ReadOnlySpan<T> source) => Assert.True(IsMapped);

        public void CopyTo(Span<T> destination) => throw new NotSupportedException();

        public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length) => throw new NotSupportedException();

        public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => throw new NotSupportedException();

        public void Dispose() => Disposed = true;
    }

    private sealed class CoverageProducer(bool paged) : IStage<Device<FrameV1>, Device<Coverage16V1>>, IStageFactory<Device<FrameV1>, Device<Coverage16V1>>, IStageProcessor<Device<FrameV1>, Device<Coverage16V1>>
    {
        public IStageFactory<Device<FrameV1>, Device<Coverage16V1>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            StageRequirements described = paged
                ? PagedGpuPreparation2D.Describe<CoverageProducer>(inputs, GraphicsExecutionDomain.Compute, 4, limits: new())
                : GenericGpuPreparation2D.Describe<CoverageProducer>(inputs, GraphicsExecutionDomain.Compute, 4);
            var graphics = (GraphicsStageRequirements)described.Extensions[0];

            // Production shape/bytes contract; this managed producer has no shader artifact.
            return new(described.MaxHostBytes, [new GraphicsStageRequirements(graphics.Domain, graphics.MaxDeviceBytes, [], [new(0, ResourceAccess.ShaderRead)])]) { OutputMaxElements = described.OutputMaxElements };
        }

        public IStageProcessor<Device<FrameV1>, Device<Coverage16V1>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Device<Coverage16V1>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<FrameV1>> a, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphicsStagePreparationContext gpu = ((IGraphicsStageCapabilities)context.Capabilities).Preparation;
            SchemaExtent geometry = gpu.GetDeviceBindings(a).Extent;
            long count = GenericGpuPreparation2D.Count(geometry);
            ValueLease<Device<Coverage16V1>> output = gpu.AllocateDeviceOutput<Coverage16V1>(new(count, geometry.Metadata));
            gpu.GetDeviceBindings(output).GetBuffer<float>(0);
            gpu.Encode(output, _ => throw new NotSupportedException("This test never records GPU work."));
            return ValueTask.FromResult(output);
        }

        public void Dispose()
        {
        }
    }

    private sealed class GeometryProducer(long width = 32896, long commands = 2) : IStage<int, Host<FrameV1>>, IStageFactory<int, Host<FrameV1>>, IStageProcessor<int, Host<FrameV1>>
    {
        public IStageFactory<int, Host<FrameV1>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(512, []) { OutputMaxElements = 1 };

        public IStageProcessor<int, Host<FrameV1>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<FrameV1>>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.PublishHost(context.CopyHostData<FrameV1, Vector4>([default], Extent(width, 1, commands))));

        public void Dispose()
        {
        }
    }

    private sealed class ShapeProbe : IStage<Host<FrameV1>, int>, IStageFactory<Host<FrameV1>, int>, IStageProcessor<Host<FrameV1>, int>
    {
        public IStageFactory<Host<FrameV1>, int> Factory => this;

        internal int BuildDescriptions { get; private set; }

        internal int ActualDescriptions { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            var samples = new SamplePolicy2D(16, 1);
            var limits = new GenericPagingLimits2D();
            bool upper = GenericGpuPreparation2D.Upper(inputs);
            long count = upper ? GenericGpuPreparation2D.MaxSampleEntries : GenericGpuPreparation2D.Count(inputs.Extents[0]);
            StageRequirements[] sampleOutputs =
            [
                new ComputeCoverage2D(samples).Factory.Describe(inputs),
                new ComputePaint2D(samples).Factory.Describe(inputs),
                new ComputeClip2D(samples).Factory.Describe(inputs),
                new PagedComputeCoverage2D(samples, limits).Factory.Describe(inputs),
                new PagedComputePaint2D(samples, limits).Factory.Describe(inputs),
                new PagedComputeClip2D(samples, limits).Factory.Describe(inputs),
            ];
            Assert.All(sampleOutputs, requirement => Assert.Equal(count, requirement.OutputMaxElements));
            StageRequirements[] dependencyOutputs =
            [
                new RasterCompose2D(samples).Factory.Describe(inputs),
                new RasterEvaluateAndComposite2D(samples).Factory.Describe(inputs),
                new MeshEvaluateAndComposite2D(samples).Factory.Describe(inputs),
                new PagedRasterCompose2D(samples, limits).Factory.Describe(inputs),
            ];
            Assert.All(dependencyOutputs, requirement => Assert.Equal(1, requirement.OutputMaxElements));
            if (upper)
            {
                BuildDescriptions++;
            }
            else
            {
                ActualDescriptions++;
            }

            return new(128, []);
        }

        public IStageProcessor<Host<FrameV1>, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<Host<FrameV1>> a, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(1));

        public void Dispose()
        {
        }
    }

    private sealed class NoArtifacts : IStageArtifactProvider
    {
        public StageArtifactDescription Describe(StageArtifactKey key, IGraphicDevice device) => throw new NotSupportedException();

        public StageArtifactLease Acquire(StageArtifactKey key, StageArtifactBuildContext context) => throw new NotSupportedException();
    }
}
