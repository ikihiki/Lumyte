using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Checks Mesh wrapper boundaries without claiming native execution.</summary>
public sealed class MeshValidationTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>All conventional draws reject a selected Mesh program before forwarding.</summary>
    [Fact]
    public void ConventionalDrawsRejectMeshSelection()
    {
        using var device = new ValidationDevice(new TypedBackend());
        var raw = new ExecutionReviewCommand();
        using IGraphicsCommandBuffer command = EncoderExecutionReviewTests.Command(device, raw);
        IRenderEncoder render = command.BeginRenderPass(new());
        render.GetType().GetField("_meshPipeline", Instance)!.SetValue(render, new MeshProgram());
        Assert.Throws<InvalidOperationException>(() => render.Draw(0));
        Assert.Throws<InvalidOperationException>(() => render.DrawIndexed(0));
        Assert.Throws<InvalidOperationException>(() => render.DrawIndirect(default));
        Assert.Throws<InvalidOperationException>(() => render.DrawIndexedIndirect(default));
        Assert.Equal(0, raw.Render.Draws);
        render.End();
    }

    /// <summary>Mesh execution cannot use a conventional or missing program.</summary>
    [Fact]
    public void MeshDrawRequiresMeshSelection()
    {
        using var device = new ValidationDevice(new TypedBackend());
        using IGraphicsCommandBuffer command = EncoderExecutionReviewTests.Command(device, new());
        IRenderEncoder render = command.BeginRenderPass(new());
        Assert.Throws<InvalidOperationException>(() => render.DrawMeshTasks(1));
        render.End();
    }

    /// <summary>Aggregate flags alone may include unavailable stages; other combinations are explicit.</summary>
    [Fact]
    public void WrapperPreservesExactAggregateSemantics()
    {
        using var device = new ValidationDevice(new RawBackend());
        Type stages = typeof(ValidationDevice).Assembly.GetType("Lumyte.Graphics.Validation.ValidationStages", true)!;
        MethodInfo normalize = stages.GetMethod("Normalize", BindingFlags.Static | BindingFlags.NonPublic, [typeof(ValidationDevice), typeof(PipelineStage)])!;
        PipelineStage Normalize(PipelineStage value) => (PipelineStage)normalize.Invoke(null, [device, value])!;
        PipelineStage legacy = PipelineStage.AllGraphics & ~(PipelineStage.MeshShader | PipelineStage.TaskShader);
        Assert.Equal(legacy, Normalize(PipelineStage.AllGraphics));
        Assert.Equal(legacy, Normalize(legacy));
        TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => Normalize(PipelineStage.AllGraphics | PipelineStage.Host));
        Assert.IsType<NotSupportedException>(error.InnerException);
        error = Assert.Throws<TargetInvocationException>(() => Normalize(PipelineStage.MeshShader));
        Assert.IsType<NotSupportedException>(error.InnerException);
    }

    private sealed class MeshProgram : IMeshPipeline
    {
        public MeshPipelineDesc Desc => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
