using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Tests;
using Lumyte.UI.Application.WebView.Cef;
using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Exercises real uploader and retained-reference hooks with an explicitly delayed queue.</summary>
public sealed class TextureUploaderTests
{
    /// <summary>Aligned staging survives until actual GPU completion and color normalization happens before decoding.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task StagingIsChargedUntilCompletionAsync()
    {
        var gpu = new FakeGpu();
        var cpuBudget = new WebViewPixelBudget(4096);
        var gpuBudget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpuBudget);
        using var uploader = new WebViewTextureUploader(cpuBudget, gpuBudget);
        Assert.True(mailbox.Publish(new byte[] { 16, 32, 64, 128, 0, 0, 0, 255 }, 1, 2, 0));
        Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        Assert.False(upload.IsCompleted);
        Assert.Equal(512, cpuBudget.Used);
        Assert.Equal(8, gpuBudget.Used);
        Assert.False(gpu.Staging!.Disposed);
        Assert.Equal(256U, gpu.Copy!.BytesPerRow);
        Assert.Equal(new byte[] { 128, 64, 32, 128 }, gpu.Staging.Bytes[..4]);
        Assert.Equal((byte)255, gpu.Staging.Bytes[259]);
        Assert.Throws<InvalidOperationException>(uploader.Dispose);
        gpu.Complete();
        Image2D image = (await upload)!;
        Assert.Equal(AlphaRepresentation2D.Straight, image.Alpha);
        Assert.Equal(0, cpuBudget.Used);
        Assert.True(gpu.Staging.Disposed);
        uploader.Dispose();
        Assert.Equal(0, gpuBudget.Used);
    }

    /// <summary>Upload buffers require an explicit completed map and must be unmapped before GPU recording.</summary>
    /// <returns>The upload verification.</returns>
    [Fact]
    public async Task UploadMapsBeforeCpuCopyAndUnmapsBeforeSubmissionAsync()
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        mailbox.Publish(new byte[] { 16, 32, 64, 128 }, 1, 1, 0);
        Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        gpu.Complete();
        Assert.NotNull(await upload);
        Assert.Equal(new[] { "map", "mapped", "copy", "unmap", "record", "submit", "dispose" }, gpu.Events);
        Assert.Equal(new byte[] { 128, 64, 32, 128 }, gpu.Staging!.Bytes[..4]);
        Assert.False(gpu.Staging.IsMapped);
        Assert.Equal(0, cpu.Used);
        uploader.Dispose();
        Assert.Equal(0, budget.Used);
    }

    /// <summary>A delayed map retains the original pixels and all budgets, and closure cannot publish its stale image.</summary>
    /// <returns>The mapping-lifetime verification.</returns>
    [Fact]
    public async Task PendingMapRetainsPixelsAndBudgetUntilCompletedAsync()
    {
        var gate = new TaskCompletionSource();
        var gpu = new FakeGpu { MapGate = gate };
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        byte[] raw = [16, 32, 64, 128];
        mailbox.Publish(raw, 1, 1, 0);
        Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        Assert.False(upload.IsCompleted);
        Assert.False(gpu.Staging!.IsMapped);
        Assert.Equal(260, cpu.Used);
        Assert.Equal(4, budget.Used);
        Assert.Equal(new[] { "map" }, gpu.Events);
        Assert.False(gpu.Texture!.Disposed);
        Assert.Throws<InvalidOperationException>(uploader.Dispose);
        await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.RetireAsync().AsTask());
        mailbox.Publish(raw, 1, 1, 0);
        Assert.Null(await uploader.TryUploadAsync(gpu.Device, mailbox));
        using (WebViewPixelMailbox.PixelLease pending = mailbox.Take()!)
        {
            Assert.Equal(raw, pending.Bytes);
        }

        mailbox.Resize(1);
        mailbox.Dispose();
        Assert.Equal(260, cpu.Used);
        Assert.Null(gpu.Copy);
        gate.SetResult();
        await gpu.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(gpu.Staging.IsMapped);
        Assert.False(gpu.Staging.Disposed);
        Assert.Equal(256, cpu.Used);
        gpu.Complete();
        Assert.Null(await upload);
        Assert.Null(uploader.Current);
        Assert.Equal(0, cpu.Used);
        Assert.True(gpu.Staging.Disposed);
        uploader.Dispose();
        Assert.Equal(0, budget.Used);
    }

    /// <summary>Pre-submission map, CPU-copy and unmap failures release their pixels, staging and destination.</summary>
    /// <param name="stage">The injected failure boundary.</param>
    /// <returns>The failure and fresh-frame retry verification.</returns>
    [Theory]
    [InlineData("map")]
    [InlineData("map-cancel")]
    [InlineData("copy")]
    [InlineData("unmap")]
    public async Task MappingFailuresReleaseResourcesWithoutReturningConvertedPixelsAsync(string stage)
    {
        var gpu = new FakeGpu
        {
            FailMap = stage == "map",
            CancelMap = stage == "map-cancel",
            FailCpuCopy = stage == "copy",
            FailUnmap = stage == "unmap",
        };
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        byte[] raw = [16, 32, 64, 128];
        mailbox.Publish(raw, 1, 1, 0);
        Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        if (stage == "map-cancel")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upload);
        }
        else
        {
            Assert.Equal(stage + "-failed", (await Assert.ThrowsAsync<InvalidOperationException>(() => upload)).Message);
        }

        Assert.True(gpu.Staging!.Disposed);
        Assert.True(gpu.Texture!.Disposed);
        Assert.False(gpu.Staging.IsMapped);
        Assert.Null(gpu.Copy);
        Assert.False(gpu.Submitted.Task.IsCompleted);
        Assert.Null(mailbox.Take());
        Assert.Null(uploader.Current);
        Assert.Equal(0, cpu.Used);
        Assert.Equal(0, budget.Used);
        gpu.FailMap = false;
        gpu.CancelMap = false;
        gpu.FailCpuCopy = false;
        gpu.FailUnmap = false;
        mailbox.Publish(raw, 1, 1, 0);
        Task<Image2D?> retry = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        gpu.Complete();
        Assert.NotNull(await retry);
        Assert.Equal(new byte[] { 128, 64, 32, 128 }, gpu.Staging!.Bytes[..4]);
        Assert.Equal(0, cpu.Used);
        uploader.Dispose();
        Assert.Equal(0, budget.Used);
    }

    /// <summary>A frozen frame survives scene replacement; independent frame leases survive snapshot disposal.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task SceneSnapshotAndGpuFrameRetireIndependentlyAsync()
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        mailbox.Publish(new byte[4], 1, 1, 0);
        Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        gpu.Complete();
        Image2D image = (await upload)!;
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawImage(image, new(0, 0, 1, 1), new(0, 0, 1, 1));
        using var scene = new DrawingScene2D();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, canvas.End());
        DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        IDisposable frame = snapshot.RetainResources();
        scene.Remove(node);
        scene.Dispose();
        Assert.Throws<InvalidOperationException>(uploader.Dispose);
        snapshot.Dispose();
        Assert.Throws<InvalidOperationException>(uploader.Dispose);
        frame.Dispose();
        uploader.Dispose();
        Assert.Equal(0, budget.Used);
    }

    /// <summary>Three live recordings exhaust the slot bound and leave the latest CPU frame pending.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ReferencedImagesAreNeverOverwrittenAsync()
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        var retained = new List<Image2D>();
        for (int i = 0; i < 3; i++)
        {
            mailbox.Publish(new byte[4], 1, 1, 0);
            Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
            gpu.Complete();
            retained.Add((await upload)!);
        }

        mailbox.Publish(new byte[] { 1, 2, 3, 255 }, 1, 1, 0);
        Assert.Null(await uploader.TryUploadAsync(gpu.Device, mailbox));
        Assert.Equal(3, gpu.TextureCount);
        Assert.Equal(12, budget.Used);
        Assert.Equal(4, cpu.Used);
        GC.KeepAlive(retained);
    }

    /// <summary>Resize invalidates an upload already in flight, without releasing its staging early.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task LateGenerationIsNotPublishedAsync()
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, new(4096));
        mailbox.Publish(new byte[4], 1, 1, 0);
        Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        mailbox.Resize(1);
        Assert.Equal(256, cpu.Used);
        gpu.Complete();
        Assert.Null(await upload);
        Assert.Null(uploader.Current);
        Assert.Equal(0, cpu.Used);
    }

    /// <summary>Budget backpressure restores the newest mailbox frame without allocating staging.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task TemporaryCpuBudgetPressurePreservesPendingPixelsAsync()
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(260);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, new(4096));
        mailbox.Publish(new byte[4], 1, 1, 0);
        Assert.True(cpu.TryReserve(1));
        Assert.Null(await uploader.TryUploadAsync(gpu.Device, mailbox));
        Assert.Null(gpu.Staging);
        Assert.Equal(5, cpu.Used);
        cpu.Release(1);
        Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        gpu.Complete();
        Assert.NotNull(await upload);
        Assert.Equal(0, cpu.Used);
    }

    /// <summary>An ambiguous submit failure retains all allocations until device retirement, never just until a failed wait.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task SubmissionFailureRequiresDeviceRetirementAsync()
    {
        var gpu = new FakeGpu { FailSubmit = true };
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        mailbox.Publish(new byte[4], 1, 1, 0);
        Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        Assert.False(upload.IsCompleted);
        Assert.False(gpu.Staging!.Disposed);
        Assert.Equal(256, cpu.Used);
        Assert.Throws<InvalidOperationException>(uploader.Dispose);
        gpu.Retire();
        await Assert.ThrowsAsync<InvalidOperationException>(() => upload);
        Assert.True(gpu.Staging.Disposed);
        Assert.Equal(0, cpu.Used);
        Assert.Equal(0, budget.Used);
    }

    /// <summary>An unsubmitted recording failure releases staging and its destination immediately.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task RecordingFailureDiscardsUnsubmittedResourcesAsync()
    {
        var gpu = new FakeGpu { FailRecord = true };
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        mailbox.Publish(new byte[4], 1, 1, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.TryUploadAsync(gpu.Device, mailbox).AsTask());
        Assert.True(gpu.Staging!.Disposed);
        Assert.Equal(0, cpu.Used);
        Assert.Equal(0, budget.Used);
    }

    /// <summary>Scene disposal releases only its own reference, not an existing read guard.</summary>
    [Fact]
    public void SceneReadLeaseSurvivesSceneDisposal()
    {
        Image2D image = new(new DispatchTexture().CreateView(), new DispatchSampler());
        ImageResourceRegistry2D.Resource resource = ImageResourceRegistry2D.Register(image);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawImage(image, new(0, 0, 1, 1), new(0, 0, 1, 1));
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, canvas.End());
        using DrawingSceneReadLease2D reader = scene.Borrow();
        scene.Dispose();
        Assert.True(resource.IsReferenced);
        reader.Dispose();
        Assert.False(resource.IsReferenced);
    }

    /// <summary>Dynamic interface shim for methods without ref-like arguments.</summary>
    public class Proxy : DispatchProxy
    {
        internal Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        internal static T Create<T>(Func<MethodInfo, object?[]?, object?> handler)
            where T : class
        {
            T proxy = Create<T, Proxy>();
            ((Proxy)(object)proxy).Handler = handler;
            return proxy;
        }

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    private sealed class FakeGpu
    {
        private readonly TaskCompletionSource _retirement = new();
        private TaskCompletionSource _completion = new();
        private SubmissionStatus _status;

        internal FakeGpu()
        {
            IGraphicsQueue queue = Proxy.Create<IGraphicsQueue>((method, args) =>
            {
                Assert.False(Staging!.IsMapped);
                Events.Add("submit");
                if (FailSubmit)
                {
                    throw new InvalidOperationException("ambiguous-submit");
                }

                _completion = new();
                _status = SubmissionStatus.Pending;
                Submitted.TrySetResult();
                return Proxy.Create<IGraphicsSubmission>((member, _) => member.Name switch
                {
                    "get_Status" => _status,
                    "WaitAsync" => new ValueTask(_completion.Task),
                    "Dispose" => null,
                    _ => throw new NotSupportedException(member.Name),
                });
            });
            Device = Proxy.Create<IGraphicDevice>((method, args) => method.Name switch
            {
                "get_Queue" => queue,
                "get_Status" => GraphicsDeviceStatus.Ready,
                "GetTextureCopyLayout" => new TextureCopyLayout { BytesPerTexel = 4, BytesPerRowAlignment = 256, BufferOffsetAlignmentInBytes = 256 },
                "CreateBuffer" => Staging = new Buffer((BufferDesc<byte>)args![0]!, this),
                "CreateTexture" => CreateTexture((TextureDesc)args![0]!),
                "CreateSampler" => new DispatchSampler(),
                "CreateCommandBuffer" => Proxy.Create<IGraphicsCommandBuffer>((member, values) =>
                {
                    if (member.Name == "CopyBufferToTexture")
                    {
                        Assert.False(Staging!.IsMapped);
                        Events.Add("record");
                        if (FailRecord)
                        {
                            throw new InvalidOperationException("record-failed");
                        }

                        Copy = (BufferTextureCopyLayout)values![0]!;
                    }

                    return null;
                }),
                "WaitForRetirementAsync" => new ValueTask(_retirement.Task),
                _ => throw new NotSupportedException(method.Name),
            });
        }

        internal bool FailMap { get; set; }

        internal bool CancelMap { get; set; }

        internal bool FailCpuCopy { get; set; }

        internal bool FailUnmap { get; set; }

        internal TaskCompletionSource? MapGate { get; init; }

        internal TaskCompletionSource Submitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal List<string> Events { get; } = [];

        internal bool FailSubmit { get; init; }

        internal bool FailRecord { get; init; }

        internal IGraphicDevice Device { get; }

        internal Buffer? Staging { get; private set; }

        internal BufferTextureCopyLayout? Copy { get; private set; }

        internal int TextureCount { get; private set; }

        internal DispatchTexture? Texture { get; private set; }

        internal void Retire() => _retirement.SetResult();

        internal void Complete()
        {
            _status = SubmissionStatus.Completed;
            _completion.SetResult();
        }

        private DispatchTexture CreateTexture(TextureDesc desc)
        {
            TextureCount++;
            return Texture = new() { Width = desc.Width, Height = desc.Height, Format = desc.Format, Usage = desc.Usage };
        }
    }

    private sealed class Buffer(BufferDesc<byte> desc, FakeGpu gpu) : IGraphicsBuffer<byte>
    {
        public ulong SizeInBytes => Count;

        public BufferLayout<byte> Layout => new(1, 1, 1, 1);

        public ulong Count => desc.Count;

        public BufferUsage Usage => desc.Usage;

        public MemoryPreference Memory => desc.Memory;

        public bool IsMapped { get; private set; }

        internal byte[] Bytes { get; } = new byte[checked((int)desc.Count)];

        internal bool Disposed { get; private set; }

        public void ValidateByteRange(ulong offset, ulong length) => Assert.True(offset + length <= Count);

        public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
        {
            if (!IsMapped || Memory != MemoryPreference.Upload)
            {
                throw new InvalidOperationException("CPU copy requires an explicitly mapped Upload buffer.");
            }

            gpu.Events.Add("copy");
            if (gpu.FailCpuCopy)
            {
                throw new InvalidOperationException("copy-failed");
            }

            source.CopyTo(Bytes.AsSpan(checked((int)offset), checked((int)length)));
        }

        public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => throw new NotSupportedException();

        public async ValueTask MapAsync(CancellationToken cancellationToken = default)
        {
            Assert.False(IsMapped);
            gpu.Events.Add("map");
            if (gpu.MapGate is { } gate)
            {
                await gate.Task;
            }

            if (gpu.CancelMap)
            {
                throw new OperationCanceledException(new CancellationToken(true));
            }

            if (gpu.FailMap)
            {
                throw new InvalidOperationException("map-failed");
            }

            IsMapped = true;
            gpu.Events.Add("mapped");
        }

        public void Unmap()
        {
            Assert.True(IsMapped);
            gpu.Events.Add("unmap");
            if (gpu.FailUnmap)
            {
                throw new InvalidOperationException("unmap-failed");
            }

            IsMapped = false;
        }

        public BufferSlice<byte> SliceElements(ulong offset, ulong count) => new(this, offset, count);

        public void CopyFrom(ReadOnlySpan<byte> source) => throw new NotSupportedException();

        public void CopyTo(Span<byte> destination) => throw new NotSupportedException();

        public void Dispose()
        {
            // A backend may unmap while destroying an unsubmitted upload allocation.
            Assert.True(gpu.MapGate?.Task.IsCompleted ?? true);
            IsMapped = false;
            Disposed = true;
            gpu.Events.Add("dispose");
        }
    }
}
