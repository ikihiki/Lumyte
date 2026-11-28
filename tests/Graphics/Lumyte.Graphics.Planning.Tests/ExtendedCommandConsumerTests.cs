using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Deterministic public consumer coverage of typed native command families.</summary>
public sealed class ExtendedCommandConsumerTests
{
    private static readonly BarrierScope _indices = new(PipelineStage.IndexInput, ResourceAccess.IndexRead);
    private static readonly BarrierScope _indirect = new(PipelineStage.DrawIndirect, ResourceAccess.IndirectRead);
    private static readonly BarrierScope _depth = new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite);

    /// <summary>Preparation resolves native ranges without recording or rereading captured arguments.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task RecordsTypedIndexedIndirectAndDepthOnlyDrawsAsync()
    {
        await using var host = new PlanningHost();
        var device = new ExtendedManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        RasterKernel<CaptureProbeArguments> raster = session.ImportRasterKernel<CaptureProbeArguments>(new ManagedKernel(), new TestLease());
        ComputeKernel<CaptureProbeArguments> compute = session.ImportComputeKernel<CaptureProbeArguments>(new ManagedKernel(), new TestLease());
        PlanBuffer<ushort> indices16 = Import<ushort>(frame, device, 8);
        PlanBuffer<uint> indices32 = Import<uint>(frame, device, 8);
        PlanBuffer<DrawIndirectArguments> draws = Import<DrawIndirectArguments>(frame, device, 2);
        PlanBuffer<DrawIndexedIndirectArguments> indexed = Import<DrawIndexedIndirectArguments>(frame, device, 2);
        PlanBuffer<DispatchIndirectArguments> dispatch = Import<DispatchIndirectArguments>(frame, device, 2);
        var captures = new List<string>();
        var arguments = new CaptureProbeArguments(captures);
        CompleteRasterState state = State();
        int ordinal = 0;
        PlanPass NextPass()
        {
            PlanPass next = frame.Plan.AddPass($"extended-{ordinal++}");
            next.Read(indices16, indices16.Range, _indices);
            next.Read(indices32, indices32.Range, _indices);
            next.Read(draws, draws.Range, _indirect);
            next.Read(indexed, indexed.Range, _indirect);
            next.Read(dispatch, dispatch.Range, _indirect);
            next.KeepAlive();
            return next;
        }

        PlanPass pass = NextPass();
        pass.Draw(raster, arguments, Depth(frame, pass), state, 3);
        pass = NextPass();
        pass.DrawIndexed(raster, arguments, Depth(frame, pass), state, indices16, new(2, 4), 3, firstIndex: 1, baseVertex: -2, firstInstance: 4);
        pass = NextPass();
        pass.DrawIndexed(raster, arguments, Depth(frame, pass), state, indices32, new(1, 6), 5);
        pass = NextPass();
        pass.DrawIndirect(raster, arguments, Depth(frame, pass), state, draws, new(1, 1));
        pass = NextPass();
        pass.DrawIndexedIndirect(raster, arguments, Depth(frame, pass), state, indices16, new(2, 4), indexed, new(1, 1));
        pass = NextPass();
        pass.DrawIndexedIndirect(raster, arguments, Depth(frame, pass), state, indices32, new(1, 6), indexed, new(0, 1));
        pass = NextPass();
        pass.DispatchIndirect(compute, arguments, dispatch, new(1, 1));
        Assert.Equal(7, captures.Count);
        Assert.Empty(device.Events);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        Assert.DoesNotContain("render", device.Events);
        Assert.DoesNotContain("compute", device.Events);
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.RecordAsync();
        Assert.Equal(7, captures.Count);
        Assert.Contains("index16:2:4", device.Events);
        Assert.Contains("index32:1:6", device.Events);
        Assert.Contains("indexed:3:1:1:-2:4", device.Events);
        Assert.Contains("indexed:5:1:0:0:0", device.Events);
        Assert.Contains("draw-indirect:1:1", device.Events);
        Assert.Contains("indexed-indirect:1:1", device.Events);
        Assert.Contains("indexed-indirect:0:1", device.Events);
        Assert.Contains("dispatch-indirect:1:1", device.Events);
        Assert.Contains("blend:0.25", device.Events);
        Assert.Contains("stencil:9", device.Events);
        Assert.NotNull(device.Commands);
        Assert.NotNull(device.Commands.RenderPass);
        Assert.Empty(device.Commands.RenderPass.ColorAttachments);
        RenderDepthStencilAttachmentDesc depth = Assert.IsType<RenderDepthStencilAttachmentDesc>(device.Commands.RenderPass.DepthStencilAttachment);
        Assert.Equal(0.75f, depth.DepthClearValue);
        Assert.Equal(AttachmentStoreOp.Discard, depth.StencilStoreOp);
        Assert.Equal(7, device.Commands.ArgumentBytes.Count);
        await execution.Value.RetireAsync();
    }

    /// <summary>Identity, ranges and exact index/indirect access are checked before argument serialization.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task RejectsInvalidBindingsBeforeArgumentCaptureAsync()
    {
        await using var host = new PlanningHost();
        var device = new ExtendedManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        await using var foreign = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        RasterKernel<CaptureProbeArguments> raster = session.ImportRasterKernel<CaptureProbeArguments>(new ManagedKernel(), new TestLease());
        RasterKernel<CaptureProbeArguments> other = foreign.ImportRasterKernel<CaptureProbeArguments>(new ManagedKernel(), new TestLease());
        ComputeKernel<CaptureProbeArguments> compute = session.ImportComputeKernel<CaptureProbeArguments>(new ManagedKernel(), new TestLease());
        PlanBuffer<ushort> indices = Import<ushort>(frame, device, 4);
        PlanBuffer<DispatchIndirectArguments> indirect = Import<DispatchIndirectArguments>(frame, device, 2);
        PlanPass pass = frame.Plan.AddPass("invalid");
        pass.Read(indices, indices.Range, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        pass.Read(indirect, indirect.Range, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        RasterAttachments attachments = Depth(frame, pass);
        CompleteRasterState state = State();
        var captures = new List<string>();
        var arguments = new CaptureProbeArguments(captures);
        Assert.Throws<InvalidOperationException>(() => pass.DrawIndexed(raster, arguments, attachments, state, indices, indices.Range, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => pass.DrawIndexed(raster, arguments, attachments, state, indices, indices.Range, 3, firstIndex: 2));
        Assert.Throws<InvalidOperationException>(() => pass.DispatchIndirect(compute, arguments, indirect, new(0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => pass.DispatchIndirect(compute, arguments, indirect, new(0, 2)));
        Assert.Throws<ArgumentException>(() => pass.Draw(other, arguments, attachments, state, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => pass.Draw(raster, arguments, attachments, state, 0));
        Assert.Empty(captures);
        Assert.Empty(device.Events);
    }

    /// <summary>Command admission enforces bounded capacity before capturing user values.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task CommandBudgetPrecedesCaptureAsync()
    {
        await using var host = new PlanningHost();
        var device = new ExtendedManagedDevice();
        await using var session = new SessionOwner(host.Module, device, new() { MaximumCommands = 1 });
        await using var frame = new FrameOwner(session.CreateFrame());
        RasterKernel<CaptureProbeArguments> raster = session.ImportRasterKernel<CaptureProbeArguments>(new ManagedKernel(), new TestLease());
        PlanPass pass = frame.Plan.AddPass("bounded");
        RasterAttachments attachments = Depth(frame, pass);
        CompleteRasterState state = State();
        var captures = new List<string>();
        var arguments = new CaptureProbeArguments(captures);
        pass.Draw(raster, arguments, attachments, state, 3);
        Assert.Throws<InvalidOperationException>(() => pass.Draw(raster, arguments, attachments, state, 3));
        Assert.Single(captures);
    }

    /// <summary>Caller-owned attachment and color-state collections cannot alter a declared command.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task SnapshotsAttachmentsAndRasterCollectionsAsync()
    {
        await using var host = new PlanningHost();
        var device = new ExtendedManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        RasterKernel<CaptureProbeArguments> kernel = session.ImportRasterKernel<CaptureProbeArguments>(new ManagedKernel(), new TestLease());
        PlanPass pass = frame.Plan.AddPass("snapshot");
        PlanTexture texture = frame.Plan.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        PlanTexture output = pass.Write(texture, texture.Range, PlanWriteMode.Discard, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), TextureState.ColorAttachment);
        RasterColorAttachment[] colors = [new() { Texture = output, Store = AttachmentStoreOp.Discard }];
        var targets = new List<ColorBlendStateDesc> { new() };
        CompleteRasterState state = State() with
        {
            Raster = State().Raster with { RenderState = GraphicsRenderStateDesc.ForColorTargets(1) with { ColorTargets = targets } },
        };
        var captures = new List<string>();
        pass.Draw(kernel, new CaptureProbeArguments(captures), new() { Colors = colors }, state, 3);
        colors[0] = colors[0] with { Store = AttachmentStoreOp.Store, Load = AttachmentLoadOp.Load };
        targets.Clear();
        pass.KeepAlive();
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.RecordAsync();
        Assert.NotNull(device.Commands);
        Assert.NotNull(device.Commands.RenderPass);
        RenderColorAttachmentDesc color = Assert.Single(device.Commands.RenderPass.ColorAttachments);
        Assert.Equal(AttachmentStoreOp.Discard, color.StoreOp);
        Assert.Equal(AttachmentLoadOp.Clear, color.LoadOp);
        Assert.NotNull(device.Commands.RenderState);
        Assert.Single(device.Commands.RenderState.ColorTargets);
        Assert.Single(captures);
        await execution.Value.RetireAsync();
    }

    private static PlanBuffer<T> Import<T>(FrameOwner frame, ExtendedManagedDevice device, ulong count)
        where T : unmanaged => frame.Plan.ImportBuffer(new ManagedBuffer<T>(count, device.Events), new TestLease());

    private static RasterAttachments Depth(FrameOwner frame, PlanPass pass)
    {
        PlanTexture texture = frame.Plan.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Depth24Stencil8, Usage = TextureUsage.RenderAttachment });
        PlanTexture output = pass.Write(texture, texture.Range, PlanWriteMode.Discard, _depth, TextureState.DepthStencilAttachment);
        return new() { DepthStencil = new() { Texture = output, DepthClear = 0.75f, StencilClear = 3, StencilStore = AttachmentStoreOp.Discard } };
    }

    private static CompleteRasterState State() => new()
    {
        Raster = new()
        {
            RenderState = GraphicsRenderStateDesc.ForColorTargets(0) with { DepthStencil = new() { DepthTestEnable = true, DepthWriteEnable = true } },
            Viewport = new(0, 0, 8, 8, 0, 1),
            Scissor = new(0, 0, 8, 8),
        },
        BlendConstant = new(0.25f, 0, 0, 0),
        StencilReference = 9,
    };
}
