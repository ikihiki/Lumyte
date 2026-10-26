using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class TestComputePipeline(ComputePipelineDesc desc) : IGraphicsComputePipeline
{
    public ComputePipelineDesc Desc { get; } = desc;

    internal bool Disposed { get; private set; }

    public void Dispose()
    {
        Assert.False(Assert.IsType<TestShader>(Desc.ComputeShader).Disposed);
        Disposed = true;
    }
}
