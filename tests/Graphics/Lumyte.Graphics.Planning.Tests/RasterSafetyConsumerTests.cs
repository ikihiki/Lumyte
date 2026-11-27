using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Checks content outcomes, exact view aliases and argument-free mesh operations.</summary>
public sealed class RasterSafetyConsumerTests
{
    /// <summary>Discarded color or stencil content cannot be exported as initialized bytes.</summary>
    /// <param name="depth">Whether to discard stencil from a depth/stencil target.</param>
    /// <returns>The asynchronous completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardedAttachmentContentsRejectExportAsync(bool depth)
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ExtendedManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        RasterKernel<NoShaderArguments> kernel = session.ImportRasterKernel<NoShaderArguments>(new ManagedKernel(), new TestLease());
        PlanTexture texture = frame.Plan.CreateTexture(new() { Width = 4, Height = 4, Format = depth ? TextureFormat.Depth24Stencil8 : TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        PlanPass pass = frame.Plan.AddPass("discard");
        texture = pass.Write(texture, texture.Range, PlanWriteMode.Discard, new(depth ? PipelineStage.DepthStencil : PipelineStage.ColorOutput, depth ? ResourceAccess.DepthStencilWrite : ResourceAccess.ColorWrite), depth ? TextureState.DepthStencilAttachment : TextureState.ColorAttachment);
        RasterAttachments attachments = depth
            ? new RasterAttachments { DepthStencil = new() { Texture = texture, StencilStore = AttachmentStoreOp.Discard } }
            : new RasterAttachments { Colors = [new() { Texture = texture, Store = AttachmentStoreOp.Discard }] };
        pass.Draw(kernel, default(NoShaderArguments), attachments, State(depth ? 0 : 1), 3);
        frame.Plan.Export(texture);
        CompiledGraphicsPlan? compiled = null;
        Exception? failure = Record.Exception(() => compiled = frame.CompileAsync().GetAwaiter().GetResult());
        compiled?.Dispose();
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("undefined", failure!.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Aliased mip declarations share permission by physical identity/version and prepare only the actual view.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task PreparesExactAliasedAttachmentViewAsync()
    {
        await using var host = new PlanningHost();
        var native = new ExtendedManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        RasterKernel<NoShaderArguments> kernel = session.ImportRasterKernel<NoShaderArguments>(new ManagedKernel(), new TestLease());
        PlanTexture texture = frame.Plan.CreateTexture(new() { Width = 8, Height = 8, MipLevels = 2, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        PlanPass pass = frame.Plan.AddPass("mip-one");
        texture = pass.Write(texture, new(1, 1), PlanWriteMode.Discard, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), TextureState.ColorAttachment);
        var selection = new TextureViewDesc { BaseMipLevel = 1, MipLevelCount = 1, BaseArrayLayer = 0, ArrayLayerCount = 1 };
        PlanTexture view = frame.Plan.View(texture, selection);
        Assert.Same(view, frame.Plan.View(texture, selection));
        pass.Draw(kernel, default(NoShaderArguments), new RasterAttachments { Colors = [new() { Texture = view }] }, State(1), 3);
        frame.Plan.Export(view);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        Assert.Single(native.Events, e => e == "allocate-view");
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.RecordAsync();
        RenderColorAttachmentDesc attachment = Assert.Single(native.Commands!.RenderPass!.ColorAttachments);
        Assert.Equal(1u, attachment.View.Info.BaseMipLevel);
        Assert.Equal(1u, attachment.View.Info.MipLevelCount);
        Assert.Empty(native.Commands.ArgumentBytes);
        Assert.Single(native.Events, e => e == "arguments");
        Assert.Single(native.Events, e => e == "prepare-native-render");
    }

    /// <summary>Mesh execution selects its typed imported program and emits no synthetic arguments for rootless shaders.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task RecordsArgumentFreeMeshWithExplicitStateAsync()
    {
        await using var host = new PlanningHost();
        var native = new ExtendedManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        MeshKernel<NoShaderArguments> kernel = session.Value.ImportMeshKernel<NoShaderArguments>(new MeshProgram(), new TestLease());
        PlanTexture texture = frame.Plan.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        PlanPass pass = frame.Plan.AddPass("mesh");
        texture = pass.Write(texture, texture.Range, PlanWriteMode.Discard, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), TextureState.ColorAttachment);
        pass.MeshTasks(kernel, default(NoShaderArguments), new() { Colors = [new() { Texture = texture }] }, State(1), 2, 3, 4);
        frame.Plan.Export(texture);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.RecordAsync();
        Assert.Contains("mesh-pipeline", native.Events);
        Assert.Contains("mesh:2:3:4", native.Events);
        Assert.Empty(native.Commands!.ArgumentBytes);
    }

    private static CompleteRasterState State(int colors) => new()
    {
        Raster = new() { RenderState = GraphicsRenderStateDesc.ForColorTargets((int)colors), Viewport = new(0, 0, 4, 4, 0, 1), Scissor = new(0, 0, 4, 4) },
    };

    private sealed class MeshProgram : IMeshPipeline
    {
        public MeshPipelineDesc Desc => throw new NotSupportedException("This public consumer checks native command structure, not shader compilation.");

        public void Dispose()
        {
        }
    }
}
