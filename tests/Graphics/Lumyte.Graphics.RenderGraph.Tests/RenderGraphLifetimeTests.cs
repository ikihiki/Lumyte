namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Verifies external ownership without referencing a resource manager.</summary>
public sealed class RenderGraphLifetimeTests
{
    /// <summary>Retained ownership survives submission and failed disposal attempts.</summary>
    [Fact]
    public void RetainedOwnershipWaitsForGpuCompletion()
    {
        var lifetime = new Lifetime();
        using CompiledRenderGraph plan = new RenderGraph().Compile();
        plan.Retain(lifetime);
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Finish();
        commands.Submit();
        Assert.Throws<InvalidOperationException>(() => plan.Dispose());
        Assert.False(lifetime.Disposed);
        commands.Complete();
        plan.Dispose();
        Assert.True(lifetime.Disposed);
    }

    /// <summary>Invalid ownership is rejected before recording callbacks.</summary>
    [Fact]
    public void ExpiredOwnershipRejectsRecordingButCanBeReleased()
    {
        var lifetime = new Lifetime();
        using CompiledRenderGraph plan = new RenderGraph().Compile();
        plan.Retain(lifetime);
        lifetime.Valid = false;
        using var commands = new RecordingCommands();
        Assert.Throws<InvalidOperationException>(() => plan.Record(commands));
        Assert.Empty(commands.Events);
        plan.Dispose();
        Assert.True(lifetime.Disposed);
    }

    private sealed class Lifetime : IRenderGraphLifetime
    {
        internal bool Valid { get; set; } = true;

        internal bool Disposed { get; private set; }

        public void Validate()
        {
            if (!Valid)
            {
                throw new InvalidOperationException("Expired ownership.");
            }
        }

        public void Dispose() => Disposed = true;
    }
}
