using System.Text.Json;

namespace Lumyte.Graphics.WebGPU.Browser.Tests;

[Collection("BrowserGpu")]
[Trait("Category", "WebGpuBrowserConformance")]
public sealed class WebGpuBrowserConformanceTests(BrowserGpuFixture fixture)
{
    [Fact]
    public void RuntimeImportCanRetryWithAValidUrlAfterAnInitialFailure()
    {
        Assert.True(fixture.RuntimeImportRetry.GetProperty("failed").GetBoolean());
        Assert.True(fixture.RuntimeImportRetry.GetProperty("direct").GetBoolean(), fixture.RuntimeImportRetry.GetProperty("retryError").GetString());
    }

    [Fact]
    public async Task UnrepresentableBufferSizeIsRejectedWithoutPoisoningTheDevice()
    {
        JsonElement result = await fixture.RunAsync("ExactBufferSize");

        Assert.Equal("description", result.GetProperty("parameter").GetString());
        Assert.Equal([0u, 0u, 0u, 0u], UInts(result, "actual"));
    }

    [Fact]
    public async Task SynchronousWebIdlFailureDoesNotIssueATimelinePoint()
    {
        JsonElement result = await fixture.RunAsync("SynchronousEncodingFailure");

        Assert.Equal("Encode", result.GetProperty("operation").GetString());
        Assert.Contains("Runtime", Strings(result, "kinds"));
        Assert.True(result.GetProperty("unissued").GetBoolean());
        Assert.True(result.GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task BackendRunsInsideBrowserWebAssemblyWithDirectRootInputs()
    {
        JsonElement result = await fixture.RunAsync("Device");

        Assert.True(result.GetProperty("browser").GetBoolean());
        Assert.True(result.GetProperty("direct").GetBoolean());
        Assert.True(result.GetProperty("immediateSize").GetUInt32() >= 32);
    }

    [Fact]
    public async Task ComputeSnapshotsEightRootBytesAndRetainsThemAcrossPipelineRebinding()
    {
        JsonElement result = await fixture.RunAsync("ComputeEight");

        Assert.Equal([40u, 74u], UInts(result, "actual"));
    }

    [Fact]
    public async Task ComputeReceivesMixedRootFieldsAtTheirExplicitOffsets()
    {
        JsonElement result = await fixture.RunAsync("ComputeMixed");

        Assert.Equal([8.5f, -6f], result.GetProperty("actual").EnumerateArray().Select(item => item.GetSingle()));
        Assert.Equal(32, result.GetProperty("rootSize").GetInt32());
    }

    [Fact]
    public async Task IndexedRasterUsesSignedBaseVertexAndCopiesTheRenderedTexture()
    {
        JsonElement result = await fixture.RunAsync("IndexedRaster");

        Assert.Equal([255u, 0u, 0u, 255u], UInts(result, "color"));
    }

    [Fact]
    public async Task TextureCopiesPreservePixelsAcrossPaddedRows()
    {
        JsonElement result = await fixture.RunAsync("TextureUpload");

        Assert.Equal([255u, 0u, 0u, 255u, 0u, 255u, 0u, 255u, 0u, 0u, 255u, 255u, 255u, 255u, 255u, 255u], UInts(result, "pixels"));
    }

    [Fact]
    public async Task MappingFlushesWritesAndRevokesMemoryAfterUnmap()
    {
        JsonElement result = await fixture.RunAsync("MappingLease");

        Assert.Equal([1u, 2u, 3u, 4u, 5u, 6u, 7u, 8u], UInts(result, "actual"));
        Assert.Equal("ObjectDisposedException", result.GetProperty("expired").GetString());
        Assert.Equal("InvalidOperationException", result.GetProperty("writable").GetString());
    }

    [Fact]
    public async Task MappingReportsTheBrowsersResourceValidationDiagnostics()
    {
        JsonElement result = await fixture.RunAsync("InvalidMapping");

        Assert.True(result.GetProperty("failed").GetBoolean());
        Assert.Contains("Validation", Strings(result, "kinds"));
    }

    [Fact]
    public async Task RejectedSecondMappingPreservesTheExistingWritableLease()
    {
        JsonElement result = await fixture.RunAsync("ConcurrentMapping");

        Assert.Equal(Enumerable.Repeat(27u, 8), UInts(result, "actual"));
    }

    [Fact]
    public async Task PartialMapWritesPreserveAllUnmodifiedBufferBytes()
    {
        JsonElement result = await fixture.RunAsync("PartialMapping");
        uint[] expected = Enumerable.Range(0, 24).Select(item => (uint)item).ToArray();
        expected[11] = 99;

        Assert.Equal(expected, UInts(result, "actual"));
    }

    [Theory]
    [InlineData("InvalidModule")]
    [InlineData("InvalidEntry")]
    public async Task ShaderFailuresAreReportedAfterSubmittedGpuUseEnds(string scenario)
    {
        JsonElement result = await fixture.RunAsync(scenario);

        Assert.True(result.GetProperty("failed").GetBoolean());
        Assert.True(result.GetProperty("complete").GetBoolean());
        Assert.Equal(1UL, result.GetProperty("value").GetUInt64());
        Assert.Contains("Validation", Strings(result, "kinds"));
    }

    [Fact]
    public async Task InvalidCommandsRejectTheirBatchWhileLaterIndependentWorkSucceeds()
    {
        JsonElement result = await fixture.RunAsync("InvalidBatch");

        Assert.Equal([0u, 0u], UInts(result, "actual"));
        Assert.Contains("Validation", Strings(result, "diagnostics"));
        Assert.True(result.GetProperty("laterComplete").GetBoolean());
    }

    [Fact]
    public async Task CpuTimelineRetainsExactValuesAboveJavaScriptsIntegerPrecision()
    {
        JsonElement result = await fixture.RunAsync("Timeline");

        Assert.True(result.GetProperty("initialComplete").GetBoolean());
        Assert.True(result.GetProperty("firstComplete").GetBoolean());
        Assert.True(result.GetProperty("lastComplete").GetBoolean());
        Assert.Equal("ArgumentOutOfRangeException", result.GetProperty("gap").GetString());
        Assert.Equal("InvalidOperationException", result.GetProperty("reused").GetString());
    }

    [Fact]
    public async Task CancellingOneWaitDoesNotCancelGpuWorkOrOtherWaits()
    {
        JsonElement result = await fixture.RunAsync("Cancellation");

        Assert.True(result.GetProperty("cancelled").GetBoolean());
        Assert.True(result.GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task ComputeProducedArgumentsDriveIndirectDispatch()
    {
        JsonElement result = await fixture.RunAsync("IndirectCompute");

        Assert.Equal([37u, 38u], UInts(result, "actual"));
    }

    [Fact]
    public async Task ComputeSnapshotsDynamicUniformOffsets()
    {
        JsonElement result = await fixture.RunAsync("DynamicBindings");

        Assert.Equal(73u, result.GetProperty("actual").GetUInt32());
    }

    [Fact]
    public async Task RasterSamplesAnImmutableTextureAndSamplerBinding()
    {
        JsonElement result = await fixture.RunAsync("SampledTexture");

        Assert.Equal([64u, 128u, 192u, 255u], UInts(result, "color"));
    }

    private static uint[] UInts(JsonElement result, string property) => result.GetProperty(property).EnumerateArray().Select(item => item.GetUInt32()).ToArray();
    private static string[] Strings(JsonElement result, string property) => result.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
