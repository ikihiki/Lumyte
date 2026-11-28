using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Checks the exact declared synchronization contract of render-attachment clears.</summary>
public sealed class ClearScopeReviewTests
{
    /// <summary>A clear requires a complete logical write, stage, access and layout on the same use.</summary>
    /// <param name="invalidCase">The missing or split declaration component.</param>
    /// <returns>The asynchronous completion.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task RejectsIncompleteClearScopeAsync(int invalidCase)
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanTexture texture = frame.Plan.CreateTexture(new() { Width = 4, Height = 4, MipLevels = 2, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        PlanPass clear = frame.Plan.AddPass("clear");
        BarrierScope scope = invalidCase == 1 ? new(PipelineStage.FragmentShader, ResourceAccess.ColorWrite) : new(PipelineStage.ColorOutput, invalidCase >= 3 ? ResourceAccess.ColorWrite : ResourceAccess.ColorRead);
        PlanRange written = invalidCase == 3 ? new(0, 1) : texture.Range;
        texture = clear.Write(texture, written, PlanWriteMode.Discard, scope, invalidCase == 4 ? TextureState.CopyDestination : TextureState.ColorAttachment);
        if (invalidCase is 2 or 4)
        {
            clear.Read(texture, texture.Range, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), TextureState.ColorAttachment);
        }

        Assert.Throws<InvalidOperationException>(() => clear.Clear(texture, default));
    }

    /// <summary>A valid clear contributes its actual color write to the following copy dependency.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task ClearToCopyIncludesColorWritesAsync()
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanTexture texture = frame.Plan.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        PlanPass clear = frame.Plan.AddPass("clear");
        texture = clear.Write(texture, texture.Range, PlanWriteMode.Discard, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), TextureState.ColorAttachment);
        clear.Clear(texture, default);
        PlanBuffer<byte> target = frame.Plan.CreateBuffer<byte>(new() { Count = 64, Usage = BufferUsage.CopyDestination });
        PlanPass copy = frame.Plan.AddPass("copy");
        copy.Read(texture, texture.Range, new(PipelineStage.Copy, ResourceAccess.CopyRead), TextureState.CopySource);
        target = copy.Write(target, target.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        copy.CopyTextureToBuffer(texture, new(0, 0, 0, 0, 4, 4), target, target.Range, 16, 4);
        frame.Plan.Export(target);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        PlanBarrier barrier = Assert.Single(compiled.Barriers, b => b.Before == clear && b.After == copy);
        Assert.Equal(new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), barrier.Source.Scope);
        Assert.Equal(new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead), barrier.Destination.Scope);
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        Assert.Equal(2, compiled.Passes.Count);
    }
}
