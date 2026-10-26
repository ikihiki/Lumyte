using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class TestProgramPipeline(GraphicsPipelineDesc desc) : IGraphicsPipeline
{
    public GraphicsPipelineDesc Desc { get; } = desc;

    internal bool Disposed { get; private set; }

    internal bool FailOnce { get; set; }

    public void Dispose()
    {
        if (FailOnce)
        {
            FailOnce = false;
            throw new InvalidOperationException("Injected pipeline cleanup failure.");
        }

        Assert.False(Assert.IsType<TestShader>(Desc.VertexShader).Disposed);
        if (Desc.FragmentShader is { } fragment)
        {
            Assert.False(Assert.IsType<TestShader>(fragment).Disposed);
        }

        Disposed = true;
    }
}
