using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Checks explicit final interop barriers rather than dummy passes or hidden copies.</summary>
public sealed class ExportStateConsumerTests
{
    /// <summary>Final sampled state is recorded after attachment work on the exact exported image.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task TextureExportTransitionsAfterFinalCommandsAsync()
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanTexture texture = frame.Plan.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled });
        PlanPass clear = frame.Plan.AddPass("clear");
        texture = clear.Write(texture, texture.Range, PlanWriteMode.Discard, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), TextureState.ColorAttachment);
        clear.Clear(texture, default);
        frame.Plan.Export(texture, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.RecordAsync();
        Assert.Equal(TextureState.Undefined, native.Commands!.TextureBarriers[0].BeforeState);
        TextureBarrierDesc final = native.Commands.TextureBarriers[^1];
        Assert.Equal(TextureState.ColorAttachment, final.BeforeState);
        Assert.Equal(TextureState.Sampled, final.AfterState);
        Assert.Equal(ResourceAccess.ShaderRead, final.After.Access);
        Assert.True(native.Events.LastIndexOf("barrier") > native.Events.LastIndexOf("end"));
        Assert.Single(compiled.Passes);
    }

    /// <summary>Buffer exports carry the actual preceding GPU write into the final host read dependency.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task BufferExportIncludesHostReadBarrierAsync()
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> source = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        PlanBuffer<int> target = frame.Plan.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopyDestination });
        PlanPass copy = frame.Plan.AddPass("readback");
        copy.Read(source, source.Range, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        target = copy.Write(target, target.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        copy.Copy(source, source.Range, target, target.Range);
        frame.Plan.Export(target, new BarrierScope(PipelineStage.Host, ResourceAccess.HostRead));
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.RecordAsync();
        MemoryBarrierDesc final = native.Commands!.Barriers[^1];
        Assert.True((final.Before.Access & ResourceAccess.CopyWrite) != 0);
        Assert.Equal(new BarrierScope(PipelineStage.Host, ResourceAccess.HostRead), final.After);
        Assert.True(native.Events.LastIndexOf("barrier") > native.Events.FindIndex(e => e.StartsWith("copy:", StringComparison.Ordinal)));
    }
}
