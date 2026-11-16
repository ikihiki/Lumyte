using BenchmarkDotNet.Attributes;
using Lumyte.UI.Application.Desktop.Benchmark.HostCompletedFrame;

namespace Lumyte.UI.Application.Desktop.Benchmark;

/// <summary>Actual frame completion, readback and PNG encoding, separately from ordinary frame costs.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Diagnostic")]
public class ScreenshotBenchmarks
{
    private DesktopFrameFixture _fixture = null!;

    /// <summary>Starts and validates the supported small scene outside measurement.</summary>
    /// <returns>The completed setup.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _fixture = new(100);
        await _fixture.StartAsync();
    }

    /// <summary>Requests fresh composed GPU pixels and consumes the encoded image.</summary>
    /// <returns>The real PNG bytes; Browser also includes host transport and decoding.</returns>
    [Benchmark]
    public Task<byte[]> CaptureComposedPngAsync() => _fixture.CaptureAsync();

    /// <summary>Retires only owned application resources.</summary>
    /// <returns>The completed teardown.</returns>
    [GlobalCleanup]
    public ValueTask CleanupAsync() => _fixture.DisposeAsync();
}
