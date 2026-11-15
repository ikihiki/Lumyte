using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Renderer;

/// <summary>Independent synthetic-device control, not a graphics baseline to subtract from renderer measurements.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class CommandEnvelopeBenchmarks
{
    private CpuGraphicsDevice _device = null!;

    /// <summary>Creates only the managed test device.</summary>
    [GlobalSetup]
    public void Setup() => _device = new();

    /// <summary>Creates, finalizes, immediately CPU-submits and disposes one empty command buffer and completion tracker.</summary>
    /// <returns>The observed synthetic submission count.</returns>
    [Benchmark]
    public long CreateFinishSimulatedSubmitDispose()
    {
        _device.ResetCounters();
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        commands.Finish();
        using IGraphicsSubmission submission = _device.Queue.Submit([commands]);
        return _device.Counters.Submissions;
    }

    /// <summary>Checks that all command/submission resources were returned before device disposal.</summary>
    [GlobalCleanup]
    public void Cleanup() => _device.Dispose();
}
