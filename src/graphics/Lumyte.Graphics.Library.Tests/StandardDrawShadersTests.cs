using Lumyte.Graphics;

namespace Lumyte.Graphics.Library.Tests;

public sealed class StandardDrawShadersTests
{
    [Fact]
    public void EmbeddedPackageContainsEveryBackendStage()
    {
        GpuShaderPackage package = StandardDrawShaders.Load();

        Assert.Equal(6, package.Entries.Count);
    }
}
