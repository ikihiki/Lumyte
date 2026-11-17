using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Verifies native immutable payload sharing while old and changed versions overlap.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks actual transfers, native references and all pixels with three live Vulkan submissions.</summary>
    /// <returns>The asynchronous GPU verification.</returns>
    [GpuFact]
    public async Task VulkanResidentVersionsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckResidentVersionsAsync(device, false);
        await CheckResidentVersionsAsync(device, true);
        await CheckResidentColdSharingAsync(device);
    }

    /// <summary>Checks the same contracts on native wgpu.</summary>
    /// <returns>The asynchronous GPU verification.</returns>
    [GpuFact]
    public async Task WgpuResidentVersionsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckResidentVersionsAsync(device, false);
        await CheckResidentVersionsAsync(device, true);
        await CheckResidentColdSharingAsync(device);
    }

    private static async Task CheckResidentVersionsAsync(IGraphicDevice device, bool standard)
    {
        using Renderer2D renderer = standard ? Renderer2D.CreateDefault(device, retentionOptions: new() { EnableResidentInstances = true }) : new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var scene = new DrawingScene2D();
        DrawingNodeId2D leaf = scene.Insert(scene.Root, 0, ResidentPrimitive(false));
        scene.Insert(scene.Root, 1, ResidentCorrelatedPath());
        using DrawingSnapshot2D original = scene.CaptureSnapshot();
        using var warm = new SharedArgumentFrame(device);
        warm.Frame = await renderer.PrepareAsync(original, new() { View = warm.View, LoadOp = AttachmentLoadOp.Clear });
        PartitionCommands initialized = SubmitResidentSlot(device, warm);
        Assert.True(initialized.PayloadCopyBytes > 0);
        Assert.Equal(standard, initialized.ComputePasses > 0);
        await RetireResidentSlotAsync(warm);
        long compilations = renderer.RetentionStatistics.CompiledRuns;
        var slots = new List<SharedArgumentFrame>();
        var snapshots = new List<DrawingSnapshot2D> { original };
        try
        {
            var old = new SharedArgumentFrame(device);
            slots.Add(old);
            old.Frame = await renderer.PrepareAsync(original, new() { View = old.View, LoadOp = AttachmentLoadOp.Clear });
            scene.SetTransform(leaf, Matrix3x2.CreateTranslation(2, -1));
            DrawingSnapshot2D moved = scene.CaptureSnapshot();
            snapshots.Add(moved);
            var next = new SharedArgumentFrame(device);
            slots.Add(next);
            next.Frame = await renderer.PrepareAsync(moved, new() { View = next.View, LoadOp = AttachmentLoadOp.Clear });
            Assert.Equal(compilations, renderer.RetentionStatistics.CompiledRuns);
            scene.ReplaceContent(leaf, ResidentPrimitive(true));
            scene.Move(leaf, scene.Root, 1);
            DrawingSnapshot2D replaced = scene.CaptureSnapshot();
            snapshots.Add(replaced);
            var changed = new SharedArgumentFrame(device);
            slots.Add(changed);
            changed.Frame = await renderer.PrepareAsync(replaced, new() { View = changed.View, LoadOp = AttachmentLoadOp.Clear });
            Assert.Equal(compilations + 1, renderer.RetentionStatistics.CompiledRuns);
            scene.Dispose();

            // All three submissions are accepted before any completion wait or frame retirement.
            PartitionCommands before = SubmitResidentSlot(device, old);
            PartitionCommands after = SubmitResidentSlot(device, next);
            PartitionCommands replacement = SubmitResidentSlot(device, changed);
            Assert.Equal(3, renderer.RetentionStatistics.ActiveFrames);
            Assert.All(slots, slot => Assert.NotNull(slot.Submission));
            Assert.Equal(0ul, before.PayloadCopyBytes);
            Assert.Equal(0ul, after.PayloadCopyBytes);
            Assert.InRange(replacement.PayloadCopyBytes, 1ul, initialized.PayloadCopyBytes - 1);
            Console.WriteLine($"resident standard={standard} initializedBytes={initialized.PayloadCopyBytes} noOpBytes={before.PayloadCopyBytes} translationBytes={after.PayloadCopyBytes} dirtyBytes={replacement.PayloadCopyBytes} noOpRoots={before.Roots.Count} translationRoots={after.Roots.Count} simultaneousFrames={renderer.RetentionStatistics.ActiveFrames}");
            Assert.Equal(0, before.ComputePasses + after.ComputePasses + replacement.ComputePasses);
            Assert.Equal(initialized.Roots.Count, before.Roots.Count);
            for (int i = 0; i < before.Roots.Count; i++)
            {
                IShaderReference originalReference = Assert.IsAssignableFrom<IShaderReference>(initialized.Roots[i].Snapshot.Values.Single(value => value.Offset == 0).Reference);
                IShaderReference oldReference = Assert.IsAssignableFrom<IShaderReference>(before.Roots[i].Snapshot.Values.Single(value => value.Offset == 0).Reference);
                IShaderReference movedReference = Assert.IsAssignableFrom<IShaderReference>(after.Roots[i].Snapshot.Values.Single(value => value.Offset == 0).Reference);
                Assert.Same(originalReference.Resource, oldReference.Resource);
                Assert.Same(oldReference.Resource, movedReference.Resource);
                Assert.Same(originalReference.Table, movedReference.Table);
            }

            // Deliberately retire in a different order from submission and CPU publication.
            foreach (int index in new[] { 2, 0, 1 })
            {
                await RetireResidentSlotAsync(slots[index]);
                Vector4[] actual = await DecodeOraclePixelsAsync(slots[index].Readback, 8, 8, slots[index].Pitch);
                Vector4[] reference = await FreshResidentReferenceAsync(device, index, standard);
                Assert.Equal(reference, actual);
                Assert.Throws<ObjectDisposedException>(slots[index].Frame!.Retire);
            }

            Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
        }
        finally
        {
            foreach (SharedArgumentFrame slot in slots)
            {
                slot.Dispose();
            }

            foreach (DrawingSnapshot2D snapshot in snapshots.Skip(1))
            {
                snapshot.Dispose();
            }
        }
    }

    private static async Task CheckResidentColdSharingAsync(IGraphicDevice device)
    {
        DrawList2D content = ResidentPrimitive(false);
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, content);
        DrawingNodeId2D second = scene.Insert(scene.Root, 1, content);
        scene.SetTransform(second, Matrix3x2.CreateTranslation(1, 0));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using var renderer = Renderer2D.CreateDefault(device, retentionOptions: new() { EnableResidentInstances = true });
        using var slot = new SharedArgumentFrame(device);
        slot.Frame = await renderer.PrepareAsync(snapshot, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear });
        Assert.Equal(1, renderer.RetentionStatistics.CompiledRuns);
        PartitionCommands observed = SubmitResidentSlot(device, slot);
        Assert.Equal(1, observed.ComputePasses);
        IShaderReference first = Assert.IsAssignableFrom<IShaderReference>(observed.Roots[0].Snapshot.Values.Single(value => value.Offset == 0).Reference);
        Assert.All(observed.Roots, root =>
        {
            IShaderReference reference = Assert.IsAssignableFrom<IShaderReference>(root.Snapshot.Values.Single(value => value.Offset == 0).Reference);
            Assert.Same(first.Resource, reference.Resource);
        });
        await RetireResidentSlotAsync(slot);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Replay(content);
        canvas.Translate(new(1, 0));
        canvas.Replay(content);
        Vector4[] pixels = await DecodeOraclePixelsAsync(slot.Readback, 8, 8, slot.Pitch);
        Assert.Equal(await ReadResidentDirectAsync(device, canvas.End(), true), pixels);
    }

    private static DrawList2D ResidentPrimitive(bool replacement)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        if (replacement)
        {
            canvas.FillRoundedRect(new(2, 2, 3, 3), 0.75f, new SRgba<float>(0, 1, 0, 0.5f));
        }
        else
        {
            canvas.FillCircle(new(3.25f, 3.5f), 1.5f, new SRgba<float>(1, 0, 0, 0.5f));
            var path = new PathBuilder2D();
            path.MoveTo(new(2, 2));
            path.QuadraticTo(new(5, 1.5f), new(5, 4));
            path.LineTo(new(2, 5));
            path.Close();
            Path2D geometry = path.Build();
            canvas.FillPath(geometry, new SRgba<float>(1, 1, 0, 0.25f));
            canvas.StrokePath(geometry, new SRgba<float>(1, 0, 1, 0.5f), new() { Width = 0.25f });
        }

        return canvas.End();
    }

    private static DrawList2D ResidentCorrelatedPath()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new(2.5f, 1.25f, 4, 5));
        var path = new PathBuilder2D();
        path.MoveTo(new(1.5f, 1.25f));
        path.LineTo(new(7, 1.25f));
        path.LineTo(new(7, 7));
        path.LineTo(new(1.5f, 7));
        path.Close();
        path.MoveTo(new(3, 3));
        path.LineTo(new(5, 3));
        path.LineTo(new(5, 5));
        path.LineTo(new(3, 5));
        path.Close();
        canvas.FillPath(path.Build(), new SRgba<float>(0, 0, 1, 0.5f), FillRule2D.EvenOdd);
        return canvas.End();
    }

    private static PartitionCommands SubmitResidentSlot(IGraphicDevice device, SharedArgumentFrame slot)
    {
        var observed = new PartitionCommands(slot.Commands);
        ResidentTargetBegin(observed, slot.Texture);
        slot.Frame!.Record(observed);
        ResidentTargetReadback(observed, slot);
        observed.Finish();
        slot.Submission = device.Queue.Submit([slot.Commands]);
        slot.Frame.AttachSubmission(slot.Submission);
        return observed;
    }

    private static async Task RetireResidentSlotAsync(SharedArgumentFrame slot)
    {
        await slot.Submission!.WaitAsync();
        slot.Commands.Dispose();
        slot.Frame!.Retire();
        slot.Retired = true;
    }

    private static async Task<Vector4[]> FreshResidentReferenceAsync(IGraphicDevice device, int version, bool standard)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        if (version == 2)
        {
            canvas.Replay(ResidentCorrelatedPath());
        }

        canvas.Save();
        if (version > 0)
        {
            canvas.Translate(new(2, -1));
        }

        canvas.Replay(ResidentPrimitive(version == 2));
        canvas.Restore();
        if (version < 2)
        {
            canvas.Replay(ResidentCorrelatedPath());
        }

        return await ReadResidentDirectAsync(device, canvas.End(), standard);
    }

    private static async Task<Vector4[]> ReadResidentDirectAsync(IGraphicDevice device, DrawList2D content, bool standard, float pixelScale = 1)
    {
        using Renderer2D renderer = standard ? Renderer2D.CreateDefault(device, retentionOptions: new() { EnableResidentInstances = true }) : new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var slot = new SharedArgumentFrame(device);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(content, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear, PixelScale = pixelScale });
        ResidentTargetBegin(slot.Commands, slot.Texture);
        prepared.Record(slot.Commands);
        ResidentTargetReadback(slot.Commands, slot);
        slot.Commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([slot.Commands]);
        await submission.WaitAsync();
        slot.Commands.Dispose();
        return await DecodeOraclePixelsAsync(slot.Readback, 8, 8, slot.Pitch);
    }

    private static void ResidentTargetBegin(IGraphicsCommandBuffer commands, IGraphicsTexture texture)
        => commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite) });

    private static void ResidentTargetReadback(IGraphicsCommandBuffer commands, SharedArgumentFrame slot)
    {
        commands.Barrier(new TextureBarrierDesc { Texture = slot.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite), After = _copyRead });
        commands.CopyTextureToBuffer(new() { Texture = slot.Texture, Width = 8, Height = 8 }, new() { Buffer = slot.Readback.SliceElements(0, slot.Readback.Count), BytesPerRow = slot.Pitch, RowsPerImage = 8 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = slot.Readback.SliceElements(0, slot.Readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
    }
}
