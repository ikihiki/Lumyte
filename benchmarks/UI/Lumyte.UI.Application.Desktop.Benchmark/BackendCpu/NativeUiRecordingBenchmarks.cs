using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.UI.Application.Desktop.Benchmark.BackendCpu;

/// <summary>Two real-backend CPU host-call cases for a fixed supported 100-rectangle UI scene, excluding Submit/Wait and presentation.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Comparison")]
public class NativeUiRecordingBenchmarks
{
    private NativeUiRecordingFixture _fixture = null!;

    /// <summary>Initializes hardware and validates warm/fresh actual GPU pixels outside timing.</summary>
    /// <returns>Untimed device creation and completed validation.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _fixture = new();
        await _fixture.StartAsync();
        Console.WriteLine($"Backend CPU: fixed supported 100 rectangles, target=128x128, real wgpu; no timed Submit/Wait/Present; actual adapter={_fixture.AdapterIdentity}");
    }

    /// <summary>Measures public retained preparation, native encoding, Finish, command disposal and AbortUnsubmitted with a resident renderer.</summary>
    /// <returns>The actual disposed unsubmitted command-buffer state.</returns>
    [Benchmark]
    public Task<CommandBufferState> WarmPrepareRecordDiscardAsync() => _fixture.WarmRecordAsync();

    /// <summary>Also includes creation/disposal of a fresh renderer, but reuses the actual device, target and UI snapshot.</summary>
    /// <returns>The actual disposed unsubmitted command-buffer state.</returns>
    [Benchmark]
    public Task<CommandBufferState> ColdRendererPrepareRecordDiscardAsync() => _fixture.ColdRendererRecordAsync();

    /// <summary>Validates equivalent warm/fresh real GPU pixels after discarded CPU probes, then releases resources outside timing.</summary>
    /// <returns>Untimed completion/readback and ordered cleanup.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            await _fixture.ValidatePixelsAsync();
        }
        finally
        {
            _fixture.Dispose();
        }
    }
}
