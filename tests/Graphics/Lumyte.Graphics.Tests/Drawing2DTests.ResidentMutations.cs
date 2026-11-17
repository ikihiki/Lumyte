using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks dependency invalidation and caller-borrowed image updates on resident runs.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks each scene mutation and live texture color on Vulkan.</summary>
    /// <returns>The asynchronous pixel checks.</returns>
    [GpuFact]
    public async Task VulkanResidentMutationsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckResidentMutationsAsync(device);
        await CheckResidentBorrowedImageAsync(device);
    }

    /// <summary>Checks the same public routes on native wgpu.</summary>
    /// <returns>The asynchronous pixel checks.</returns>
    [GpuFact]
    public async Task WgpuResidentMutationsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckResidentMutationsAsync(device);
        await CheckResidentBorrowedImageAsync(device);
    }

    /// <summary>CPU snapshots rebuild resident payloads for each fresh physical Vulkan device owner.</summary>
    /// <returns>The asynchronous generation checks.</returns>
    [GpuFact]
    public async Task VulkanResidentDeviceGenerationsAsync()
    {
        using var scene = new DrawingScene2D();
        DrawList2D content = ResidentPrimitive(false);
        scene.Insert(scene.Root, 0, content);
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        scene.Dispose();
        for (int generation = 0; generation < 2; generation++)
        {
            using var device = VulkanDevice.Create();
            await CheckResidentGenerationAsync(device, snapshot, content);
        }
    }

    /// <summary>CPU snapshots rebuild resident payloads for each fresh physical native-wgpu owner.</summary>
    /// <returns>The asynchronous generation checks.</returns>
    [GpuFact]
    public async Task WgpuResidentDeviceGenerationsAsync()
    {
        using var scene = new DrawingScene2D();
        DrawList2D content = ResidentPrimitive(false);
        scene.Insert(scene.Root, 0, content);
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        scene.Dispose();
        for (int generation = 0; generation < 2; generation++)
        {
            using var device = WgpuDevice.Create();
            await CheckResidentGenerationAsync(device, snapshot, content);
        }
    }

    private static async Task CheckResidentGenerationAsync(IGraphicDevice device, DrawingSnapshot2D snapshot, DrawList2D content)
    {
        using Renderer2D renderer = CreateResidentRenderer(device);
        using var slot = new SharedArgumentFrame(device);
        slot.Frame = await renderer.PrepareAsync(snapshot, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear });
        Assert.Equal(1, renderer.RetentionStatistics.CompiledRuns);
        PartitionCommands observed = SubmitResidentSlot(device, slot);
        Assert.True(observed.PayloadCopyBytes > 0);
        Assert.Equal(0, observed.ComputePasses);
        await RetireResidentSlotAsync(slot);
        Vector4[] pixels = await DecodeOraclePixelsAsync(slot.Readback, 8, 8, slot.Pitch);
        Assert.Equal(await ReadResidentDirectAsync(device, content), pixels);
    }

    private static async Task CheckResidentMutationsAsync(IGraphicDevice device)
    {
        using Renderer2D renderer = CreateResidentRenderer(device);
        using var scene = new DrawingScene2D();
        DrawingNodeId2D group = scene.AddGroup(scene.Root, 0);
        DrawList2D content = ResidentPrimitive(false);
        DrawingNodeId2D leaf = scene.Insert(group, 0, content);
        scene.Insert(scene.Root, 1, ResidentCorrelatedPath());
        using DrawingSnapshot2D original = scene.CaptureSnapshot();
        Matrix3x2 transform = Matrix3x2.Identity;
        Path2D? clip = null;
        bool reversed = false;
        bool visible = true;
        float pixelScale = 1;
        long[] compilations = [2, 0, 0, 1, 0, 0, 0, 0, 1, 2];
        for (int mutation = 0; mutation < compilations.Length; mutation++)
        {
            switch (mutation)
            {
                case 2:
                    transform = Matrix3x2.CreateTranslation(1, 0);
                    scene.SetTransform(group, transform);
                    break;
                case 3:
                    clip = Rectangle(2.5f, 1.25f, 3, 4.5f);
                    scene.SetClip(group, clip);
                    break;
                case 4:
                    reversed = true;
                    scene.Move(group, scene.Root, 1);
                    break;
                case 5:
                    visible = false;
                    scene.Remove(leaf);
                    break;
                case 6:
                    visible = true;
                    DrawingNodeId2D replacement = scene.Insert(group, 0, content);
                    Assert.NotEqual(leaf, replacement);
                    leaf = replacement;
                    break;
                case 7:
                    clip = null;
                    scene.SetClip(group, null);
                    break;
                case 8:
                    transform = Matrix3x2.CreateScale(1.25f);
                    scene.SetTransform(group, transform);
                    break;
                case 9:
                    pixelScale = 2;
                    break;
            }

            using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
            using var slot = new SharedArgumentFrame(device);
            long before = renderer.RetentionStatistics.CompiledRuns;
            slot.Frame = await renderer.PrepareAsync(snapshot, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear, PixelScale = pixelScale });
            Assert.Equal(compilations[mutation], renderer.RetentionStatistics.CompiledRuns - before);
            PartitionCommands observed = SubmitResidentSlot(device, slot);
            if (compilations[mutation] == 0)
            {
                Assert.Equal(0ul, observed.PayloadCopyBytes);
                Assert.Equal(0, observed.ComputePasses);
            }

            await RetireResidentSlotAsync(slot);
            Vector4[] actual = await DecodeOraclePixelsAsync(slot.Readback, 8, 8, slot.Pitch);
            DrawList2D referenceContent = ResidentMutationReference(content, transform, clip, reversed, visible);
            Vector4[] reference = await ReadResidentDirectAsync(device, referenceContent, pixelScale);
            Assert.Equal(reference, actual);
        }

        // Roll back to the still-readable old snapshot after editing and disposing the CPU scene.
        scene.Dispose();
        using var rollback = new SharedArgumentFrame(device);
        rollback.Frame = await renderer.PrepareAsync(original, new() { View = rollback.View, LoadOp = AttachmentLoadOp.Clear });
        PartitionCommands restored = SubmitResidentSlot(device, rollback);
        Assert.Equal(0ul, restored.PayloadCopyBytes);
        await RetireResidentSlotAsync(rollback);
        Vector4[] rollbackPixels = await DecodeOraclePixelsAsync(rollback.Readback, 8, 8, rollback.Pitch);
        Assert.Equal(await FreshResidentReferenceAsync(device, 0), rollbackPixels);
    }

    private static DrawList2D ResidentMutationReference(DrawList2D content, Matrix3x2 transform, Path2D? clip, bool reversed, bool visible)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        if (reversed)
        {
            canvas.Replay(ResidentCorrelatedPath());
        }

        canvas.Save();
        canvas.ConcatTransform(transform);
        if (clip is not null)
        {
            canvas.ClipPath(clip);
        }

        if (visible)
        {
            canvas.Replay(content);
        }

        canvas.Restore();
        if (!reversed)
        {
            canvas.Replay(ResidentCorrelatedPath());
        }

        return canvas.End();
    }

    private static async Task CheckResidentBorrowedImageAsync(IGraphicDevice device)
    {
        using IGraphicsTexture image = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView imageView = image.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest });
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawImage(new(imageView, sampler), new(0, 0, 1, 1), new(2, 2, 4, 4));
        DrawList2D content = canvas.End();
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, content);
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using Renderer2D renderer = CreateResidentRenderer(device);
        for (int revision = 0; revision < 2; revision++)
        {
            await UploadAsync(device, image, revision == 0 ? [0, 0, 255, 255] : [255, 0, 0, 255]);
            using var slot = new SharedArgumentFrame(device);
            slot.Frame = await renderer.PrepareAsync(snapshot, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear });
            PartitionCommands observed = SubmitResidentSlot(device, slot);
            if (revision == 1)
            {
                Assert.Equal(0ul, observed.PayloadCopyBytes);
                Assert.Equal(1, renderer.RetentionStatistics.CompiledRuns);
            }

            await RetireResidentSlotAsync(slot);
            Vector4[] actual = await DecodeOraclePixelsAsync(slot.Readback, 8, 8, slot.Pitch);
            Vector4[] reference = await ReadResidentDirectAsync(device, content);
            Assert.Equal(reference, actual);
            AssertPixel(actual, 8, 3, 3, revision == 0 ? new(0, 0, 1, 1) : new(1, 0, 0, 1));
        }
    }
}
