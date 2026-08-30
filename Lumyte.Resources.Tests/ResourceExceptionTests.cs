namespace Lumyte.Resources.Tests;

public sealed class ResourceExceptionTests
{
    [Fact]
    public void FailureKindsHaveDistinctExceptionTypes()
    {
        ResourceException[] exceptions =
        [
            new AssetResolutionException("resolution failed"),
            new AssetNotFoundException("asset not found"),
            new AssetSourceException("source failed"),
            new ResourceLoaderNotFoundException("loader not found"),
            new ResourceNotFoundException("resource not found"),
            new ResourceDependencyCycleException("dependency cycle"),
            new ResourceLoadException("load failed")
        ];

        Assert.Collection(
            exceptions,
            exception => Assert.IsType<AssetResolutionException>(exception),
            exception => Assert.IsType<AssetNotFoundException>(exception),
            exception => Assert.IsType<AssetSourceException>(exception),
            exception => Assert.IsType<ResourceLoaderNotFoundException>(exception),
            exception => Assert.IsType<ResourceNotFoundException>(exception),
            exception => Assert.IsType<ResourceDependencyCycleException>(exception),
            exception => Assert.IsType<ResourceLoadException>(exception));
    }

    [Fact]
    public void PreservesMessageAndInnerException()
    {
        InvalidOperationException innerException = new("source failed");

        ResourceLoadException exception = new(
            "Unable to read the asset source.",
            innerException);

        Assert.Equal("Unable to read the asset source.", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void RequiresMessage()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new ResourceLoadException(""));

        Assert.Equal("message", exception.ParamName);
    }
}
