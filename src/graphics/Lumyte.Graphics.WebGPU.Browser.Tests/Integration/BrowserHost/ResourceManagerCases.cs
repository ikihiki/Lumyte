using Lumyte.Graphics.Tests;

public static partial class BrowserCases
{
    private static async Task<object> ResourceManagerPackageAsync()
    {
        using var fixture = await BrowserCaseFixture.CreateAsync();
        var result = await ResourceManagerGpuCases.PackageAsync(fixture.Backend);
        return new { data = result.Data.Select(item => (int)item).ToArray(), pixel = result.Pixel.Select(item => (int)item).ToArray(), released = result.Released };
    }

    private static async Task<object> ResourceManagerComputeAsync()
    {
        using var fixture = await BrowserCaseFixture.CreateAsync();
        var result = await ResourceManagerGpuCases.ComputeAsync(fixture.Backend);
        return new { actual = result.Actual, complete = result.Complete };
    }
}
