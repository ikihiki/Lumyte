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

    /// <summary>Three explicitly pinned recordings exhaust the slot bound and leave the latest CPU frame pending.</summary>
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
        var leases = new List<IDisposable>();
        for (int i = 0; i < 3; i++)
        {
            mailbox.Publish(new byte[4], 1, 1, 0);
            Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
            gpu.Complete();
            Image2D image = (await upload)!;
            retained.Add(image);
            leases.Add(ImageResourceRegistry2D.Retain([RecordImage(image)]));
        }

        mailbox.Publish(new byte[] { 1, 2, 3, 255 }, 1, 1, 0);
        Assert.Null(await uploader.TryUploadAsync(gpu.Device, mailbox));
        Assert.Equal(3, gpu.TextureCount);
        Assert.Equal(12, budget.Used);
        Assert.Equal(4, cpu.Used);
        leases[0].Dispose();
        Task<Image2D?> next = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        gpu.Complete();
        Assert.NotNull(await next);
        Assert.Equal(new byte[] { 3, 2, 1, 255 }, gpu.Staging!.Bytes[..4]);
        Assert.Equal(3, gpu.TextureCount);
        foreach (IDisposable lease in leases)
        {
            lease.Dispose();
        }

        GC.KeepAlive(retained);
    }

    /// <summary>Retired owners, rather than GC of live CPU descriptors, admit the next browser color.</summary>
    /// <returns>The deterministic publication check.</returns>
    [Fact]
    public async Task RetiredLeasesAllowLiveBrowserProgressWithoutCollectionAsync()
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        var retained = new List<Image2D>();
        var scenes = new List<DrawingScene2D>();
        for (byte i = 1; i <= 3; i++)
        {
            mailbox.Publish(new byte[] { i, 0, 0, 255 }, 1, 1, 0);
            Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
            gpu.Complete();
            Image2D image = (await upload)!;
            retained.Add(image);
            var canvas = new Canvas2D();
            canvas.Begin();
            canvas.DrawImage(image, new(0, 0, 1, 1), new(0, 0, 1, 1));
            var scene = new DrawingScene2D();
            scene.Insert(scene.Root, 0, canvas.End());
            scenes.Add(scene);
        }

        Assert.Equal(3, gpu.TextureCount);
        foreach (DrawingScene2D scene in scenes)
        {
            scene.Dispose();
        }

        mailbox.Publish(new byte[] { 255, 128, 0, 255 }, 1, 1, 0);
        Task<Image2D?> fourth = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();

        // Complete only admitted work: the unfixed implementation returns null before submission.
        if (!fourth.IsCompleted)
        {
            gpu.Complete();
        }

        Assert.NotNull(await fourth);
        Assert.Equal(new byte[] { 0, 128, 255, 255 }, gpu.Staging!.Bytes[..4]);
        Assert.Equal(new byte[] { 0, 128, 255, 255 }, gpu.TexturePixels[uploader.Current!.View.Texture]);
        Assert.Equal(3, gpu.TextureCount);
        Assert.Equal(12, budget.Used);
        Assert.Equal(0, cpu.Used);
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

    /// <summary>Old wrappers re-entering scenes pin the same allocation after a newer wrapper was published.</summary>
    /// <param name="direct">Whether the revived owner is a direct prepared drawing.</param>
    /// <returns>The live descriptor resurrection check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldDescriptorRetainsReusedAllocationAsync(bool direct)
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, new(4096));
        Image2D first = await UploadColorAsync(gpu, mailbox, uploader, 1);
        DrawList2D oldRecording = RecordImage(first);
        Image2D second = await UploadColorAsync(gpu, mailbox, uploader, 2);
        Image2D third = await UploadColorAsync(gpu, mailbox, uploader, 3);
        Assert.Same(first.View, third.View);
        Assert.NotSame(first, third);
        using var oldScene = new DrawingScene2D();
        oldScene.Insert(oldScene.Root, 0, oldRecording);
        using Renderer2D renderer = CreateImageRenderer(gpu.Device);
        IDisposable oldPin = direct ? await renderer.PrepareAsync(oldRecording, new() { View = new DispatchTexture().CreateView() }) : oldScene;
        if (direct)
        {
            oldScene.Dispose();
        }

        Image2D fourth = await UploadColorAsync(gpu, mailbox, uploader, 4);
        Assert.Same(second.View, fourth.View);
        using IDisposable fourthPin = ImageResourceRegistry2D.Retain([RecordImage(fourth)]);
        Image2D fifth = await UploadColorAsync(gpu, mailbox, uploader, 5);
        using IDisposable fifthPin = ImageResourceRegistry2D.Retain([RecordImage(fifth)]);
        mailbox.Publish(new byte[] { 6, 0, 0, 255 }, 1, 1, 0);
        Assert.Null(await uploader.TryUploadAsync(gpu.Device, mailbox));
        Assert.Equal(new byte[] { 0, 0, 3, 255 }, gpu.TexturePixels[first.View.Texture]);
        oldPin.Dispose();
        Image2D sixth = await CompletePendingAsync(gpu, mailbox, uploader);
        Assert.Same(first.View, sixth.View);
        Assert.Equal(new byte[] { 0, 0, 6, 255 }, gpu.TexturePixels[first.View.Texture]);
        Assert.Equal(3, gpu.TextureCount);
    }

    /// <summary>A writer holds admission across mapping and submission, and failed reader acquisition is transactional.</summary>
    /// <returns>The gated update check.</returns>
    [Fact]
    public async Task WriterRejectsRacingReadersAndRollsBackAcquisitionAsync()
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, new(4096));
        Image2D first = await UploadColorAsync(gpu, mailbox, uploader, 1);
        Image2D second = await UploadColorAsync(gpu, mailbox, uploader, 2);
        DrawList2D oldRecording = RecordImage(first);
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, oldRecording);
        scene.Dispose();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gpu.MapGate = gate;
        gpu.Submitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        mailbox.Publish(new byte[] { 3, 0, 0, 255 }, 1, 1, 0);
        Task<Image2D?> updating = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        Assert.False(updating.IsCompleted);
        Assert.Equal(new byte[] { 0, 0, 1, 255 }, gpu.TexturePixels[first.View.Texture]);
        using var rejected = new DrawingScene2D();
        Assert.Throws<InvalidOperationException>(() => rejected.Insert(rejected.Root, 0, oldRecording));
        using var existing = new DrawingScene2D();
        DrawingNodeId2D stable = existing.Insert(existing.Root, 0, RecordImage(second));
        Assert.Throws<InvalidOperationException>(() => existing.ReplaceContent(stable, oldRecording));
        using (DrawingSceneReadLease2D reader = existing.Borrow())
        {
            Assert.Single(((IDrawingSceneSource2D)reader).Entries);
        }

        using var freeImage = new SceneResourceFixture();
        Assert.Throws<InvalidOperationException>(() => ImageResourceRegistry2D.Retain([freeImage.Recording, oldRecording]));
        Assert.False(freeImage.Resource.IsReferenced);
        gate.SetResult();
        await gpu.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => ImageResourceRegistry2D.Retain([oldRecording]));
        gpu.Complete();
        Assert.NotNull(await updating);
        using IDisposable restored = ImageResourceRegistry2D.Retain([oldRecording]);
    }

    /// <summary>Independent owners, including a direct prepared recording, protect live pixels until their actual lifetime ends.</summary>
    /// <param name="owner">The owner kind.</param>
    /// <returns>The pin verification.</returns>
    [Theory]
    [InlineData("scene")]
    [InlineData("snapshot")]
    [InlineData("read")]
    [InlineData("direct")]
    [InlineData("retained")]
    [InlineData("retained-submitted")]
    public async Task ExplicitConsumersPinBrowserAllocationAsync(string owner)
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, new(4096));
        Image2D image = await UploadColorAsync(gpu, mailbox, uploader, 1);
        DrawList2D recording = RecordImage(image);
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, recording);
        using Renderer2D renderer = CreateImageRenderer(gpu.Device);
        var target = new RenderTarget2D { View = new DispatchTexture().CreateView(), LoadOp = AttachmentLoadOp.Load };
        IDisposable pin = owner switch
        {
            "scene" => scene,
            "snapshot" => scene.CaptureSnapshot(),
            "read" => scene.Borrow(),
            "direct" => await renderer.PrepareAsync(recording, target),
            "retained" or "retained-submitted" => await PrepareRetainedAsync(renderer, scene, target),
            _ => throw new InvalidOperationException(),
        };
        if (owner != "scene")
        {
            scene.Dispose();
        }

        CommandBufferState state = CommandBufferState.Recording;
        SubmissionStatus status = SubmissionStatus.Pending;
        IGraphicsCommandBuffer commands = Proxy.Create<IGraphicsCommandBuffer>((member, _) =>
        {
            if (member.Name == "Dispose")
            {
                state = CommandBufferState.Disposed;
                return null;
            }

            return member.Name == "get_State" ? state : null;
        });
        if (owner == "retained-submitted")
        {
            PreparedDrawingFrame2D submitted = Assert.IsType<PreparedDrawingFrame2D>(pin);
            submitted.Record(commands);
            state = CommandBufferState.Submitted;
            submitted.AttachSubmission(Proxy.Create<IGraphicsSubmission>((member, _) => member.Name == "get_Status" ? status : null));
            Assert.Throws<InvalidOperationException>(submitted.Dispose);
            Assert.Throws<InvalidOperationException>(submitted.Retire);
        }

        await UploadColorAsync(gpu, mailbox, uploader, 2);
        using IDisposable second = ImageResourceRegistry2D.Retain([RecordImage(uploader.Current!)]);
        await UploadColorAsync(gpu, mailbox, uploader, 3);
        using IDisposable third = ImageResourceRegistry2D.Retain([RecordImage(uploader.Current!)]);
        mailbox.Publish(new byte[] { 4, 0, 0, 255 }, 1, 1, 0);
        Assert.Null(await uploader.TryUploadAsync(gpu.Device, mailbox));
        Assert.Equal(new byte[] { 0, 0, 1, 255 }, gpu.TexturePixels[image.View.Texture]);
        if (owner == "retained-submitted")
        {
            status = SubmissionStatus.Completed;
            commands.Dispose();
            Assert.IsType<PreparedDrawingFrame2D>(pin).Retire();
        }
        else
        {
            pin.Dispose();
        }

        Image2D replacement = await CompletePendingAsync(gpu, mailbox, uploader);
        Assert.Same(image.View, replacement.View);
        Assert.Equal(3, gpu.TextureCount);
    }

    /// <summary>Retired browser descriptors reject late GPU preparation, without invalidating ordinary borrowed images.</summary>
    /// <returns>The allocation retirement check.</returns>
    [Fact]
    public async Task RetiredDescriptorsRejectLateConsumersBeforeGpuPreparationAsync()
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        Image2D first = await UploadColorAsync(gpu, mailbox, uploader, 1);
        await UploadColorAsync(gpu, mailbox, uploader, 2);
        DrawList2D old = RecordImage(first);
        mailbox.Publish(new byte[8], 2, 1, 0);
        await CompletePendingAsync(gpu, mailbox, uploader);
        Assert.True(((DispatchTexture)first.View.Texture).Disposed);
        using var scene = new DrawingScene2D();
        Assert.Throws<InvalidOperationException>(() => scene.Insert(scene.Root, 0, old));
        using var observer = new ObservingImageRenderer();
        using Renderer2D renderer = CreateImageRenderer(gpu.Device, observer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.PrepareAsync(old, new() { View = new DispatchTexture().CreateView() }).AsTask());
        Assert.Equal(0, observer.Preparations);
        Image2D staticImage = new(new DispatchTexture().CreateView(), new DispatchSampler());
        using (PreparedDraw2D prepared = await renderer.PrepareAsync(RecordImage(staticImage), new() { View = new DispatchTexture().CreateView() }))
        {
            Assert.Equal(1, observer.Preparations);
        }

        uploader.Dispose();
        Assert.Equal(0, budget.Used);
    }

    /// <summary>An uncertain reused-slot submission rejects old readers until retirement, including failed retirement retries.</summary>
    /// <returns>The quarantine verification.</returns>
    [Fact]
    public async Task ReusedSlotWriterSurvivesAmbiguousSubmitAndRetirementRetryAsync()
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        Image2D first = await UploadColorAsync(gpu, mailbox, uploader, 1);
        await UploadColorAsync(gpu, mailbox, uploader, 2);
        DrawList2D old = RecordImage(first);
        gpu.FailSubmit = true;
        gpu.RetirementFailures = 2;
        mailbox.Publish(new byte[] { 3, 0, 0, 255 }, 1, 1, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.TryUploadAsync(gpu.Device, mailbox).AsTask());
        Assert.Throws<InvalidOperationException>(() => ImageResourceRegistry2D.Retain([old]));
        Assert.Equal(256, cpu.Used);
        Assert.Equal(8, budget.Used);
        Assert.False(((DispatchTexture)first.View.Texture).Disposed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.RetireAsync().AsTask());
        Assert.Throws<InvalidOperationException>(() => ImageResourceRegistry2D.Retain([old]));
        gpu.Retire();
        await uploader.RetireAsync();
        Assert.Equal(0, cpu.Used);
        Assert.Equal(0, budget.Used);
        Assert.Throws<InvalidOperationException>(() => ImageResourceRegistry2D.Retain([old]));
    }

    /// <summary>A failed read guard rolls back its scene borrow and all partially acquired resources.</summary>
    [Fact]
    public void FailedReadAdmissionDoesNotPoisonSceneMutability()
    {
        Image2D image = new(new DispatchTexture().CreateView(), new DispatchSampler());
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, RecordImage(image));

        // Register after insertion to exercise a rejecting guard independently of the existing scene pin.
        ImageResourceRegistry2D.Resource resource = ImageResourceRegistry2D.Register(image);
        using (ImageResourceRegistry2D.Resource.WriteLease writer = resource.TryAcquireWrite()!)
        {
            Assert.Throws<InvalidOperationException>(() => scene.Borrow());
            scene.SetTransform(scene.Root, System.Numerics.Matrix3x2.Identity);
            Assert.False(resource.IsReferenced);
        }

        using DrawingSceneReadLease2D reader = scene.Borrow();
        Assert.True(resource.IsReferenced);
        Assert.Null(resource.TryAcquireWrite());
    }

    /// <summary>A snapshot pins old dimensions until release; resize then revokes every unleased old descriptor.</summary>
    /// <returns>The bounded resize verification.</returns>
    [Fact]
    public async Task ResizeRespectsSnapshotThenInvalidatesOldRecordingAsync()
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        Image2D first = await UploadColorAsync(gpu, mailbox, uploader, 1);
        DrawList2D old = RecordImage(first);
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, old);
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        scene.Dispose();
        Image2D second = await UploadColorAsync(gpu, mailbox, uploader, 2);
        using IDisposable secondPin = ImageResourceRegistry2D.Retain([RecordImage(second)]);
        mailbox.Publish(new byte[8], 2, 1, 0);
        Image2D resized = await CompletePendingAsync(gpu, mailbox, uploader);
        using IDisposable resizedPin = ImageResourceRegistry2D.Retain([RecordImage(resized)]);
        Assert.False(((DispatchTexture)first.View.Texture).Disposed);
        mailbox.Publish(new byte[8], 2, 1, 0);
        Assert.Null(await uploader.TryUploadAsync(gpu.Device, mailbox));
        snapshot.Dispose();
        await CompletePendingAsync(gpu, mailbox, uploader);
        Assert.True(((DispatchTexture)first.View.Texture).Disposed);
        Assert.Throws<InvalidOperationException>(() => ImageResourceRegistry2D.Retain([old]));
        Assert.Equal(20, budget.Used);
        resizedPin.Dispose();
        secondPin.Dispose();
        uploader.Dispose();
        Assert.Equal(0, budget.Used);
    }

    /// <summary>A failed update of a reused slot revokes old descriptors, while the published other slot stays valid.</summary>
    /// <param name="stage">The injected failure boundary.</param>
    /// <returns>The failed reuse and fresh admission check.</returns>
    [Theory]
    [InlineData("map")]
    [InlineData("map-cancel")]
    [InlineData("copy")]
    [InlineData("unmap")]
    [InlineData("record")]
    public async Task FailedReuseRevokesOldDescriptorsWithoutDestroyingCurrentAsync(string stage)
    {
        var gpu = new FakeGpu();
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        Image2D first = await UploadColorAsync(gpu, mailbox, uploader, 1);
        Image2D current = await UploadColorAsync(gpu, mailbox, uploader, 2);
        DrawList2D old = RecordImage(first);
        gpu.FailMap = stage == "map";
        gpu.CancelMap = stage == "map-cancel";
        gpu.FailCpuCopy = stage == "copy";
        gpu.FailUnmap = stage == "unmap";
        gpu.FailRecord = stage == "record";
        mailbox.Publish(new byte[] { 3, 0, 0, 255 }, 1, 1, 0);
        if (stage == "map-cancel")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => uploader.TryUploadAsync(gpu.Device, mailbox).AsTask());
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.TryUploadAsync(gpu.Device, mailbox).AsTask());
        }

        Assert.True(((DispatchTexture)first.View.Texture).Disposed);
        Assert.Same(current, uploader.Current);
        Assert.Throws<InvalidOperationException>(() => ImageResourceRegistry2D.Retain([old]));
        Assert.Equal(0, cpu.Used);
        Assert.Equal(4, budget.Used);
        gpu.FailMap = gpu.CancelMap = gpu.FailCpuCopy = gpu.FailUnmap = gpu.FailRecord = false;
        await UploadColorAsync(gpu, mailbox, uploader, 4);
        uploader.Dispose();
        Assert.Equal(0, budget.Used);
    }

    /// <summary>Retrying a failed slot cleanup must never return the same CPU reservation twice.</summary>
    /// <returns>The exact accounting check.</returns>
    [Fact]
    public async Task PendingSlotCleanupFailureDoesNotReleaseCpuBudgetTwiceAsync()
    {
        var gpu = new FakeGpu { TrackCleanup = true, FailCompletionWait = true, RetirementFailures = 1, CleanupFailure = "view" };
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        mailbox.Publish(new byte[] { 1, 2, 3, 255 }, 1, 1, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.TryUploadAsync(gpu.Device, mailbox).AsTask());
        Assert.Equal(256, cpu.Used);
        Assert.Equal(4, budget.Used);
        gpu.Retire();
        await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.RetireAsync().AsTask());
        Assert.Equal(0, cpu.Used);
        Assert.Equal(4, budget.Used);
        await uploader.RetireAsync();
        Assert.Equal(0, cpu.Used);
        Assert.Equal(0, budget.Used);
        Assert.False(cpu.TryReserve(cpu.Limit + 1));
    }

    /// <summary>Every successful cleanup step is checkpointed while unfinished native ownership remains quarantined.</summary>
    /// <param name="stage">The one-shot failing native Dispose.</param>
    /// <returns>The retry and exact budget verification.</returns>
    [Theory]
    [InlineData("submission")]
    [InlineData("commands")]
    [InlineData("staging")]
    [InlineData("view")]
    [InlineData("texture")]
    [InlineData("sampler")]
    public async Task PendingCleanupRetriesOnlyUnfinishedResourcesAsync(string stage)
    {
        var gpu = new FakeGpu { TrackCleanup = true, RejectDuplicateDisposals = true, FailCompletionWait = true, RetirementFailures = 1, CleanupFailure = stage };
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        mailbox.Publish(new byte[] { 1, 2, 3, 255 }, 1, 1, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.TryUploadAsync(gpu.Device, mailbox).AsTask());
        Assert.Empty(gpu.CleanupAttempts);
        Assert.Equal(256, cpu.Used);
        Assert.Equal(4, budget.Used);
        gpu.Retire();
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.RetireAsync().AsTask());
        Assert.Equal(stage + "-dispose-failed", failure.Message);
        Assert.Equal(stage is "submission" or "commands" or "staging" ? 256 : 0, cpu.Used);
        Assert.Equal(4, budget.Used);
        Assert.Equal(2, gpu.RetirementWaitCalls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.TryUploadAsync(gpu.Device, mailbox).AsTask());
        gpu.RetirementFailures = 1;
        await uploader.RetireAsync();
        Assert.Equal(2, gpu.RetirementWaitCalls);
        Assert.Equal(0, cpu.Used);
        Assert.Equal(0, budget.Used);
        Assert.Equal(6, gpu.CleanupSuccesses.Count);
        Assert.All(gpu.CleanupSuccesses.Values, count => Assert.Equal(1, count));
        Assert.Equal(2, gpu.CleanupAttempts[stage]);
        Assert.False(cpu.TryReserve(cpu.Limit + 1));
        Assert.False(budget.TryReserve(budget.Limit + 1));
        await uploader.RetireAsync();
        Assert.All(gpu.CleanupSuccesses.Values, count => Assert.Equal(1, count));
    }

    /// <summary>Normal retirement also keeps a retry owner instead of marking partially destroyed allocations disposed.</summary>
    /// <param name="stage">The failing slot component.</param>
    /// <returns>The final shutdown checkpoint verification.</returns>
    [Theory]
    [InlineData("view")]
    [InlineData("texture")]
    [InlineData("sampler")]
    public async Task NormalSlotRetirementRetriesUnfinishedComponentsAsync(string stage)
    {
        var gpu = new FakeGpu { TrackCleanup = true, RejectDuplicateDisposals = true, CleanupFailure = stage };
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        Image2D image = await UploadColorAsync(gpu, mailbox, uploader, 1);
        DrawList2D recording = RecordImage(image);
        Assert.Throws<InvalidOperationException>(uploader.Dispose);
        Assert.Throws<InvalidOperationException>(() => ImageResourceRegistry2D.Retain([recording]));
        Assert.Equal(0, cpu.Used);
        Assert.Equal(4, budget.Used);
        await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.TryUploadAsync(gpu.Device, mailbox).AsTask());
        await uploader.RetireAsync();
        Assert.Equal(0, gpu.RetirementWaitCalls);
        Assert.Equal(0, cpu.Used);
        Assert.Equal(0, budget.Used);
        Assert.Equal(6, gpu.CleanupSuccesses.Count);
        Assert.All(gpu.CleanupSuccesses.Values, count => Assert.Equal(1, count));
        uploader.Dispose();
        Assert.All(gpu.CleanupSuccesses.Values, count => Assert.Equal(1, count));
    }

    /// <summary>Even a completed upload retains an explicit cleanup owner if native resource disposal throws.</summary>
    /// <param name="stage">The failing upload resource.</param>
    /// <returns>The completed upload cleanup retry.</returns>
    [Theory]
    [InlineData("submission")]
    [InlineData("commands")]
    [InlineData("staging")]
    public async Task CompletedUploadCleanupFailureRemainsRetryableAsync(string stage)
    {
        var gpu = new FakeGpu { TrackCleanup = true, RejectDuplicateDisposals = true, CleanupFailure = stage };
        var cpu = new WebViewPixelBudget(4096);
        var budget = new WebViewPixelBudget(4096);
        using var mailbox = new WebViewPixelMailbox(cpu);
        using var uploader = new WebViewTextureUploader(cpu, budget);
        mailbox.Publish(new byte[] { 1, 2, 3, 255 }, 1, 1, 0);
        Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        gpu.Complete();
        await Assert.ThrowsAsync<InvalidOperationException>(() => upload);
        Assert.Equal(256, cpu.Used);
        Assert.Equal(4, budget.Used);
        DrawList2D recording = RecordImage(uploader.Current!);
        Assert.Throws<InvalidOperationException>(() => ImageResourceRegistry2D.Retain([recording]));
        gpu.RetirementFailures = 1;
        await uploader.RetireAsync();
        Assert.Equal(0, gpu.RetirementWaitCalls);
        Assert.Equal(0, cpu.Used);
        Assert.Equal(0, budget.Used);
        Assert.Equal(6, gpu.CleanupSuccesses.Count);
        Assert.All(gpu.CleanupSuccesses.Values, count => Assert.Equal(1, count));
    }

    private static async Task<PreparedDrawingFrame2D> PrepareRetainedAsync(Renderer2D renderer, DrawingScene2D scene, RenderTarget2D target)
    {
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        return await renderer.PrepareAsync(snapshot, target);
    }

    private static Renderer2D CreateImageRenderer(IGraphicDevice device, ObservingImageRenderer? imageRenderer = null)
    {
        var defaults = DrawingCommandRenderers2D.CreateDefault();
        var table = new DrawingCommandRenderers2D(defaults.FillRect, defaults.FillRoundedRect, defaults.FillCircle, defaults.DrawLine, defaults.FillPath, defaults.StrokePath, imageRenderer ?? new(), defaults.DrawCoverageMask, defaults.DrawDistanceField, defaults.DrawMesh, defaults.DrawPaintGraph);
        return new(device, table);
    }

    private static async Task<Image2D> UploadColorAsync(FakeGpu gpu, WebViewPixelMailbox mailbox, WebViewTextureUploader uploader, byte blue)
    {
        Assert.True(mailbox.Publish(new byte[] { blue, 0, 0, 255 }, 1, 1, 0));
        return await CompletePendingAsync(gpu, mailbox, uploader);
    }

    private static async Task<Image2D> CompletePendingAsync(FakeGpu gpu, WebViewPixelMailbox mailbox, WebViewTextureUploader uploader)
    {
        Task<Image2D?> upload = uploader.TryUploadAsync(gpu.Device, mailbox).AsTask();
        Assert.False(upload.IsCompleted);
        gpu.Complete();
        return Assert.IsType<Image2D>(await upload);
    }

    private static DrawList2D RecordImage(Image2D image)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawImage(image, new(0, 0, 1, 1), new(0, 0, 1, 1));
        return canvas.End();
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

    private sealed class SceneResourceFixture : IDisposable
    {
        internal SceneResourceFixture()
        {
            Image2D image = new(new DispatchTexture().CreateView(), new DispatchSampler());
            Resource = ImageResourceRegistry2D.Register(image);
            Recording = RecordImage(image);
        }

        internal ImageResourceRegistry2D.Resource Resource { get; }

        internal DrawList2D Recording { get; }

        public void Dispose()
        {
        }
    }

    private sealed class ObservingImageRenderer : IDrawImageRenderer2D, IDisposable
    {
        internal int Preparations { get; private set; }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken = default)
        {
            Preparations++;
            return ValueTask.FromResult<IPreparedCommandBatch2D>(new EmptyBatch());
        }

        public void Dispose()
        {
        }
    }

    private sealed class EmptyBatch : IPreparedCommandBatch2D
    {
        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
        }

        public void Dispose()
        {
        }
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
                    "WaitAsync" => FailCompletionWait ? ValueTask.FromException(new InvalidOperationException("completion-failed")) : new ValueTask(_completion.Task),
                    "Dispose" => Cleanup("submission"),
                    _ => throw new NotSupportedException(member.Name),
                });
            });
            Device = Proxy.Create<IGraphicDevice>((method, args) => method.Name switch
            {
                "get_Queue" => queue,
                "get_Status" => GraphicsDeviceStatus.Ready,
                "get_Caps" => new DeviceCaps(),
                "GetTextureCopyLayout" => new TextureCopyLayout { BytesPerTexel = 4, BytesPerRowAlignment = 256, BufferOffsetAlignmentInBytes = 256 },
                "CreateBuffer" => Staging = new Buffer((BufferDesc<byte>)args![0]!, this),
                "CreateTexture" => CreateTexture((TextureDesc)args![0]!),
                "CreateSampler" => CreateSampler(),
                "CreateCommandBuffer" => Proxy.Create<IGraphicsCommandBuffer>((member, values) =>
                {
                    if (member.Name == "Dispose")
                    {
                        return Cleanup("commands");
                    }

                    if (member.Name == "CopyBufferToTexture")
                    {
                        Assert.False(Staging!.IsMapped);
                        Events.Add("record");
                        if (FailRecord)
                        {
                            throw new InvalidOperationException("record-failed");
                        }

                        Copy = (BufferTextureCopyLayout)values![0]!;
                        Destination = ((TextureCopyRegion)values[1]!).Texture;
                    }

                    return null;
                }),
                "WaitForRetirementAsync" => RetirementWait(),
                _ => throw new NotSupportedException(method.Name),
            });
        }

        internal bool TrackCleanup { get; init; }

        internal bool RejectDuplicateDisposals { get; init; }

        internal bool FailCompletionWait { get; init; }

        internal string? CleanupFailure { get; set; }

        internal Dictionary<string, int> CleanupSuccesses { get; } = [];

        internal Dictionary<string, int> CleanupAttempts { get; } = [];

        internal bool FailMap { get; set; }

        internal bool CancelMap { get; set; }

        internal bool FailCpuCopy { get; set; }

        internal bool FailUnmap { get; set; }

        internal TaskCompletionSource? MapGate { get; set; }

        internal TaskCompletionSource Submitted { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal List<string> Events { get; } = [];

        internal bool FailSubmit { get; set; }

        internal bool FailRecord { get; set; }

        internal IGraphicDevice Device { get; }

        internal Buffer? Staging { get; private set; }

        internal BufferTextureCopyLayout? Copy { get; private set; }

        internal int RetirementFailures { get; set; }

        internal int RetirementWaitCalls { get; private set; }

        internal IGraphicsTexture? Destination { get; private set; }

        internal Dictionary<IGraphicsTexture, byte[]> TexturePixels { get; } = [];

        internal int TextureCount { get; private set; }

        internal DispatchTexture? Texture { get; private set; }

        internal void Retire() => _retirement.SetResult();

        internal void Complete()
        {
            TexturePixels[Destination!] = Staging!.Bytes[..4];
            _status = SubmissionStatus.Completed;
            _completion.SetResult();
        }

        internal object? Cleanup(string stage)
        {
            if (!TrackCleanup)
            {
                return null;
            }

            CleanupAttempts[stage] = CleanupAttempts.GetValueOrDefault(stage) + 1;
            if (CleanupFailure == stage)
            {
                CleanupFailure = null;
                throw new InvalidOperationException(stage + "-dispose-failed");
            }

            Assert.False(RejectDuplicateDisposals && CleanupSuccesses.GetValueOrDefault(stage) != 0, stage + " disposed successfully more than once");
            CleanupSuccesses[stage] = CleanupSuccesses.GetValueOrDefault(stage) + 1;
            return null;
        }

        private ValueTask RetirementWait()
        {
            RetirementWaitCalls++;
            if (RetirementFailures > 0)
            {
                RetirementFailures--;
                return ValueTask.FromException(new InvalidOperationException("retirement-failed"));
            }

            return new(_retirement.Task);
        }

        private IGraphicsSampler CreateSampler()
        {
            var sampler = new DispatchSampler();
            return !TrackCleanup ? sampler : Proxy.Create<IGraphicsSampler>((member, args) => member.Name == "Dispose" ? Cleanup("sampler") : member.Invoke(sampler, args));
        }

        private IGraphicsTexture CreateTexture(TextureDesc desc)
        {
            TextureCount++;
            Texture = new() { Width = desc.Width, Height = desc.Height, Format = desc.Format, Usage = desc.Usage };
            if (!TrackCleanup)
            {
                return Texture;
            }

            IGraphicsTexture? proxy = null;
            proxy = Proxy.Create<IGraphicsTexture>((member, args) => member.Name switch
            {
                "Dispose" => Cleanup("texture"),
                "CreateView" => Proxy.Create<IGraphicsTextureView>((view, _) => view.Name switch
                {
                    "get_Texture" => proxy,
                    "get_Info" => new TextureViewInfo { Format = desc.Format, Dimension = TextureViewDimension.D2, BaseMipLevel = 0, MipLevelCount = 1, BaseArrayLayer = 0, ArrayLayerCount = 1 },
                    "Dispose" => Cleanup("view"),
                    _ => throw new NotSupportedException(view.Name),
                }),
                _ => member.Invoke(Texture, args),
            });
            return proxy;
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
            gpu.Cleanup("staging");
            IsMapped = false;
            Disposed = true;
            gpu.Events.Add("dispose");
        }
    }
}
