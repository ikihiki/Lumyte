namespace Lumyte.Graphics.Portable.Shaders.Tests.Programs;

public sealed class PortableShaderProgramTests
{
    [Fact]
    public void DisposeReleasesOwnedObjectsOnceAndKeepsBorrowedBackendAlive()
    {
        var backend = new TestBackend();
        PortableShaderProgram program = new PortableShaderLoader(backend).Load(
            PortableShaderLoaderTests.Package(groups: [new([]), new([])]));

        program.Dispose();
        program.Dispose();

        Assert.Equal(new TestBackend.Release[] { new("layout", 1), new("layout", 0), new("module", 0) }, backend.Releases);
        Assert.False(backend.Disposed);
    }

    [Fact]
    public void DisposeAttemptsEveryReleaseWhenOneFailsAndNeverRetries()
    {
        var backend = new TestBackend { FailingLayoutRelease = 1 };
        PortableShaderProgram program = new PortableShaderLoader(backend).Load(
            PortableShaderLoaderTests.Package(groups: [new([]), new([])]));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => program.Dispose());
        program.Dispose();

        Assert.Same(backend.LayoutReleaseError, error);
        Assert.Equal(new TestBackend.Release[] { new("layout", 1), new("layout", 0), new("module", 0) }, backend.Releases);
    }

    [Fact]
    public void SeparateLoadsOwnDistinctDeviceObjects()
    {
        var backend = new TestBackend();
        var loader = new PortableShaderLoader(backend);
        PortableShaderPackage package = PortableShaderLoaderTests.Package(groups: [new([])]);
        using PortableShaderProgram first = loader.Load(package);
        using PortableShaderProgram second = loader.Load(package);

        first.Dispose();

        Assert.NotSame(first.EntryPoints[0].Module, second.EntryPoints[0].Module);
        Assert.NotSame(first.BindingLayouts[0], second.BindingLayouts[0]);
        Assert.Equal(new TestBackend.Release[] { new("layout", 0), new("module", 0) }, backend.Releases);
    }
}
