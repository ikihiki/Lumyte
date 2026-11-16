using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises physical whole-table dependencies after independent retained asset eviction.</summary>
public sealed class RetainedArgumentLifetimeRegressionTests
{
    /// <summary>Exposes exact fake native allocation lifetime.</summary>
    private interface ITrackedBuffer
    {
        /// <summary>Gets a value indicating whether the allocation has been released.</summary>
        bool Disposed { get; }

        /// <summary>Gets the allocation memory class.</summary>
        MemoryPreference Memory { get; }

        /// <summary>Gets the native allocation size.</summary>
        ulong SizeInBytes { get; }
    }

    /// <summary>Evicting one asset cannot leave a disposed buffer in another asset's table.</summary>
    /// <param name="assetLimit">The idle asset limit.</param>
    /// <param name="byteLimit">The independent finite memory limit.</param>
    /// <param name="residentAssets">The number of assets that fit the finite shared ownership budget.</param>
    /// <returns>The asynchronous test completion.</returns>
    [Theory]
    [InlineData(1, 67108864, 1)]
    [InlineData(512, 4096, 1)]
    public async Task PartialIdleEvictionPreservesEveryPhysicalBindingAsync(int assetLimit, long byteLimit, int residentAssets)
    {
        var device = new TrackedDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxCachedAssets = assetLimit, MaxCachedBytes = byteLimit, MaxFramesInFlight = 1 });
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        for (int i = 0; i < 2; i++)
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            canvas.FillRect(new(i, 0, 1, 1), new SRgba<float>(1, 0, 0, 1));
            scene.Insert(scene.Root, i, canvas.End());
        }

        using DrawingSceneReadLease2D source = scene.Borrow();
        for (int iteration = 0; iteration < 4; iteration++)
        {
            using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(source, new() { View = view });
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await renderer.PrepareAsync(source, new() { View = view }));
            using var commands = new ProfilingCommands();
            frame.Record(commands);

            // Native Wgpu resolves every registration, not just references reachable from this root.
            try
            {
                Assert.NotEmpty(commands.Events.OfType<TrackedTable>());
                foreach (TrackedTable table in commands.Events.OfType<TrackedTable>())
                {
                    table.ValidateBindings();
                }
            }
            catch
            {
                commands.Dispose();
                frame.AbortUnsubmitted();
                throw;
            }

            commands.Submit();
            frame.AttachSubmission(new CompletedSubmission());
            commands.Dispose();
            frame.Retire();
            Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
            Assert.Equal(residentAssets, renderer.RetentionStatistics.CachedAssets);
            Assert.True(renderer.RetentionStatistics.CachedBytes <= byteLimit);
            Assert.True(renderer.RetentionStatistics.CachedBytes >= device.Buffers.Concat(device.RawBuffers).Where(buffer => !buffer.Disposed).Sum(buffer => (long)buffer.SizeInBytes));
            Assert.Equal(residentAssets, device.Buffers.Count(buffer => !buffer.Disposed && buffer.Memory != MemoryPreference.Upload));
        }

        Assert.Equal(residentAssets * 3, renderer.RetentionStatistics.ReusedRuns);
        renderer.Dispose();
        Assert.All(device.Buffers.Concat(device.RawBuffers), buffer => Assert.True(buffer.Disposed));
    }

    /// <summary>Zero-budget completion and unsubmitted abandonment release every shader allocation.</summary>
    /// <param name="complete">Whether to prove submission completion or abandon before recording.</param>
    /// <returns>The asynchronous test completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZeroBudgetNeverRetainsHiddenBufferOwnersAsync(bool complete)
    {
        var device = new TrackedDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxCachedBytes = 0, MaxFramesInFlight = 1 });
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), new SRgba<float>(1, 0, 0, 1));
        scene.Insert(scene.Root, 0, canvas.End());
        using DrawingSceneReadLease2D source = scene.Borrow();
        for (int iteration = 0; iteration < 3; iteration++)
        {
            using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(source, new() { View = view });
            Assert.Equal(2, device.Buffers.Count(buffer => !buffer.Disposed));
            if (complete)
            {
                using var commands = new ProfilingCommands();
                frame.Record(commands);
                commands.Submit();
                frame.AttachSubmission(new CompletedSubmission());
                commands.Dispose();
                frame.Retire();
            }
            else
            {
                frame.Dispose();
            }

            Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
            Assert.Equal(0, renderer.RetentionStatistics.CachedAssets);
            Assert.Equal(0, renderer.RetentionStatistics.CachedBytes);
            Assert.All(device.Buffers.Concat(device.RawBuffers), buffer => Assert.True(buffer.Disposed));
        }
    }

    /// <summary>Duplicate registrations and inherited aliases cannot release a live sibling's slot.</summary>
    [Fact]
    public void RetirementCountsDistinctReservationUsersAndKeepsSiblingSlotsStable()
    {
        var device = new TrackedDevice();
        using var plan = new ArgumentTablePlan(device);
        using IGraphicsBuffer<uint> shared = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.ShaderRead });
        using IGraphicsBuffer<uint> unique = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.ShaderRead });
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsTextureView uniqueView = texture.CreateView();
        using var sampler = new DispatchSampler();
        ShaderResources first = plan.CreateShaderResources();
        var alias = new ResourceAlias(first);
        ShaderResources sibling = first.CreateSibling();
        IGpuRef<uint>? a = null;
        IGpuRef<uint>? duplicate = null;
        IGpuRef<uint>? b = null;
        first.RegisterBuffer(shared.SliceElements(0, 4), reference => a = reference);
        alias.RegisterBuffer(shared.SliceElements(0, 4), reference => duplicate = reference);
        first.RegisterBuffer(unique.SliceElements(0, 4), _ => { });
        sibling.RegisterBuffer(shared.SliceElements(0, 4), reference => b = reference);
        first.RegisterTexture(view, _ => { });
        first.RegisterTexture(uniqueView, _ => { });
        sibling.RegisterTexture(view, _ => { });
        first.RegisterSampler(sampler, _ => { });
        sibling.RegisterSampler(sampler, _ => { });
        plan.Materialize();
        TrackedTable table = Assert.IsType<TrackedTable>(Assert.Single(plan.Tables));
        Assert.Same(a, duplicate);
        Assert.Same(a, b);
        uint sharedSlot = ((IShaderReference)b!).Slot;
        Assert.Equal(2, table.LiveBuffers);
        first.Retire();
        alias.Retire();
        first.Retire();
        Assert.False(first.IsReady);
        Assert.False(alias.IsReady);
        Assert.Throws<InvalidOperationException>(() => _ = plan.Reservations[0].Table);
        Assert.True(sibling.IsReady);
        Assert.Equal(1, table.LiveBuffers);
        Assert.Equal(1, table.LiveTextures);
        Assert.Equal(1, table.LiveSamplers);
        Assert.Equal(1, table.BufferReleases);
        Assert.Equal(sharedSlot, ((IShaderReference)b).Slot);
        Assert.Same(table, ((IShaderReference)b).Table);
        unique.Dispose();
        uniqueView.Dispose();
        table.ValidateBindings();
        Assert.False(((ITrackedBuffer)shared).Disposed);
        sibling.Retire();
        Assert.Equal(0, table.LiveBuffers);
        Assert.Equal(0, table.LiveTextures);
        Assert.Equal(0, table.LiveSamplers);
        Assert.Equal(2, table.BufferReleases);
        Assert.False(((ITrackedBuffer)shared).Disposed);
    }

    /// <summary>Partial native slot release quarantines recording and retries only pending releases.</summary>
    [Fact]
    public void RetirementRetriesFailedNativeReleaseWithoutRepeatingSuccessfulSlots()
    {
        var device = new TrackedDevice();
        using var plan = new ArgumentTablePlan(device);
        using IGraphicsBuffer<uint> first = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.ShaderRead });
        using IGraphicsBuffer<uint> second = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.ShaderRead });
        ShaderResources resources = plan.CreateShaderResources();
        resources.RegisterBuffer(first.SliceElements(0, 4), _ => { });
        resources.RegisterBuffer(second.SliceElements(0, 4), _ => { });
        Assert.Throws<InvalidOperationException>(resources.Retire);
        plan.Materialize();
        TrackedTable table = Assert.IsType<TrackedTable>(Assert.Single(plan.Tables));
        table.FailBufferRelease = 2;
        Assert.Throws<ArithmeticException>(resources.Retire);
        Assert.False(resources.IsReady);
        Assert.Throws<InvalidOperationException>(() => _ = plan.Reservations[0].Table);
        Assert.Equal(1, table.LiveBuffers);
        Assert.Equal(1, table.BufferReleases);
        resources.Retire();
        resources.Retire();
        Assert.Equal(0, table.LiveBuffers);
        Assert.Equal(2, table.BufferReleases);
        Assert.All(device.RawBuffers, buffer => Assert.False(buffer.Disposed));
    }

    /// <summary>A failed unregister cannot destroy registered storage or consume a frame slot.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task BatchUnregisterFailureKeepsStorageOwnedUntilRetryAsync()
    {
        var device = new TrackedDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxCachedBytes = 0, MaxFramesInFlight = 1 });
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), new SRgba<float>(1, 0, 0, 1));
        scene.Insert(scene.Root, 0, canvas.End());
        using DrawingSceneReadLease2D source = scene.Borrow();
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(source, new() { View = view });
        using var commands = new ProfilingCommands();
        frame.Record(commands);
        TrackedTable table = Assert.Single(commands.Events.OfType<TrackedTable>());
        table.FailBufferRelease = 1;
        commands.Submit();
        frame.AttachSubmission(new CompletedSubmission());
        commands.Dispose();
        Assert.Throws<ArithmeticException>(frame.Retire);
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
        Assert.Equal(2, table.LiveBuffers);
        Assert.All(device.Buffers.Concat(device.RawBuffers), buffer => Assert.False(buffer.Disposed));
        renderer.Dispose();
        frame.Retire();
        frame.Dispose();
        Assert.All(device.Buffers.Concat(device.RawBuffers), buffer => Assert.True(buffer.Disposed));
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
    }

    private sealed class ResourceAlias(ShaderResources resources) : ShaderResources(resources)
    {
    }

    private sealed class TrackedDevice : IGraphicDevice
    {
        private readonly ProfilingDevice _inner = new();

        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
        {
            add
            {
            }

            remove
            {
            }
        }

        public GraphicsDeviceStatus Status => _inner.Status;

        public GraphicsDeviceLoss? Loss => _inner.Loss;

        public DeviceCaps Caps => _inner.Caps;

        public IGraphicsQueue Queue => _inner.Queue;

        internal List<ITrackedBuffer> Buffers { get; } = [];

        internal List<ITrackedBuffer> RawBuffers { get; } = [];

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => _inner.WaitForRetirementAsync(cancellationToken);

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged => _inner.GetBufferLayout<T>();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged
        {
            var buffer = new TrackedRawBuffer<T>(_inner.CreateBuffer(desc));
            RawBuffers.Add(buffer);
            return buffer;
        }

        public IGraphicsTexture CreateTexture(TextureDesc desc) => _inner.CreateTexture(desc);

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => _inner.CreateSampler(desc);

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => new TrackedTable(desc);

        public IGraphicsShader CreateShader(ShaderArtifact artifact) => _inner.CreateShader(artifact);

        public IGraphicsSemaphore CreateSemaphore() => _inner.CreateSemaphore();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => _inner.CreateCommandBuffer(desc);

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => _inner.GetTextureCopyLayout(format);

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => _inner.CreateGraphicsPipeline(desc);

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => _inner.CreateComputePipeline(desc);

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : struct, IShaderArguments
        {
            var buffer = new TrackedBuffer<T>(_inner.CreateShaderDataBuffer<T>(artifact, count, memory));
            Buffers.Add(buffer);
            return buffer;
        }
    }

    private sealed class TrackedRawBuffer<T>(IGraphicsBuffer<T> inner) : IGraphicsBuffer<T>, ITrackedBuffer
        where T : unmanaged
    {
        public bool Disposed { get; private set; }

        public ulong SizeInBytes => inner.SizeInBytes;

        public BufferLayout<T> Layout => inner.Layout;

        public ulong Count => inner.Count;

        public BufferUsage Usage => inner.Usage;

        public MemoryPreference Memory => inner.Memory;

        public bool IsMapped => inner.IsMapped;

        public void ValidateByteRange(ulong offset, ulong length) => inner.ValidateByteRange(offset, length);

        public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length) => inner.CopyBytesFrom(source, offset, length);

        public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => inner.CopyBytesTo(destination, offset, length);

        public ValueTask MapAsync(CancellationToken cancellationToken = default) => inner.MapAsync(cancellationToken);

        public void Unmap() => inner.Unmap();

        public BufferSlice<T> SliceElements(ulong offset, ulong count) => new(this, offset, count);

        public void CopyFrom(ReadOnlySpan<T> source) => inner.CopyFrom(source);

        public void CopyTo(Span<T> destination) => inner.CopyTo(destination);

        public void Dispose()
        {
            inner.Dispose();
            Disposed = true;
        }
    }

    private sealed class TrackedBuffer<T>(IGraphicsShaderDataBuffer<T> inner) : IGraphicsShaderDataBuffer<T>, ITrackedBuffer
        where T : struct, IShaderArguments
    {
        public bool Disposed { get; private set; }

        public ulong Count => inner.Count;

        public ulong SizeInBytes => inner.SizeInBytes;

        public ulong ShaderElementStrideInBytes => inner.ShaderElementStrideInBytes;

        public MemoryPreference Memory => inner.Memory;

        public bool IsMapped => inner.IsMapped;

        public ValueTask MapAsync(CancellationToken cancellationToken = default) => inner.MapAsync(cancellationToken);

        public void Unmap() => inner.Unmap();

        public void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0) => inner.CopyFrom(source, elementOffset);

        public ShaderDataSlice<T> SliceElements(ulong offset, ulong count) => new(this, offset, count);

        public void Dispose()
        {
            inner.Dispose();
            Disposed = true;
        }
    }

    private sealed class TrackedTable(ArgumentTableDesc desc) : IArgumentTable
    {
        private readonly Dictionary<uint, object> _buffers = [];
        private readonly Dictionary<uint, object> _textures = [];
        private readonly Dictionary<uint, object> _samplers = [];
        private int _releaseAttempts;
        private bool _disposed;

        public uint TextureCapacity => desc.TextureCapacity;

        public uint SamplerCapacity => desc.SamplerCapacity;

        public uint BufferCapacity => desc.BufferCapacity;

        internal int LiveBuffers => _buffers.Count;

        internal int LiveTextures => _textures.Count;

        internal int LiveSamplers => _samplers.Count;

        internal int BufferReleases { get; private set; }

        internal int FailBufferRelease { get; set; }

        public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view)
        {
            _textures[slot] = view;
            return new Reference<IGraphicsTextureView>(this, view, slot, 1, 0, 0);
        }

        public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler)
        {
            _samplers[slot] = sampler;
            return new Reference<IGraphicsSampler>(this, sampler, slot, 1, 0, 0);
        }

        public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)
            where T : unmanaged
        {
            _buffers[slot] = range.Buffer;
            return new Reference<T>(this, range.Buffer, slot, range.Count, range.OffsetInBytes, range.SizeInBytes);
        }

        public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
            where T : struct, IShaderArguments
        {
            _buffers[slot] = range.Buffer;
            return new Reference<T>(this, range.Buffer, slot, range.Count, range.Offset * range.Buffer.ShaderElementStrideInBytes, range.Count * range.Buffer.ShaderElementStrideInBytes);
        }

        public void ReleaseTexture(uint slot) => _textures.Remove(slot);

        public void ReleaseSampler(uint slot) => _samplers.Remove(slot);

        public void ReleaseBuffer(uint slot)
        {
            if (++_releaseAttempts == FailBufferRelease)
            {
                throw new ArithmeticException("Injected native release failure.");
            }

            Assert.True(_buffers.Remove(slot));
            BufferReleases++;
        }

        public void Dispose()
        {
            _disposed = true;
            _buffers.Clear();
            _textures.Clear();
            _samplers.Clear();
        }

        internal void ValidateBindings()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (ITrackedBuffer buffer in _buffers.Values.OfType<ITrackedBuffer>())
            {
                ObjectDisposedException.ThrowIf(buffer.Disposed, buffer);
            }
        }
    }

    private sealed class Reference<T>(object table, object resource, uint slot, ulong count, ulong offset, ulong size) : IGpuRef<T>, IShaderReference
    {
        public object Table => table;

        public object Resource => resource;

        public uint Slot => slot;

        public ulong Count => count;

        public ulong OffsetInBytes => offset;

        public ulong SizeInBytes => size;

        public ulong RegistrationOffsetInBytes => offset;

        public ulong RegistrationSizeInBytes => size;

        public ShaderBufferAccess BufferAccess => ShaderBufferAccess.ReadOnly;

        public IGpuRef<T> GetElement(ulong index) => new Reference<T>(table, resource, slot, 1, offset + (index * size / count), size / count);
    }

    private sealed class CompletedSubmission : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Completed;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
