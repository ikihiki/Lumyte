using BenchmarkDotNet.Attributes;

namespace Lumyte.Memory.Benchmark;

/// <summary>Finite in-chunk waves with untimed fresh-chunk reset, never repeated cursor exhaustion.</summary>
[BenchmarkCategory("Core", "Memory", "Allocation", "Screening", "Comparison", "Diagnostic")]
public class ChunkHitBenchmarks
{
    private readonly MemoryPoolId _pool = new("chunk-wave");
    private readonly IMemoryBuffer<byte>[] _wave = new IMemoryBuffer<byte>[32];
    private MemoryManager _manager = null!;
    private IMemoryScope _scope = null!;
    private IMemoryBuffer<byte> _anchor = null!;

    /// <summary>Creates a fresh anchor and chunk before each single-invocation iteration.</summary>
    [IterationSetup]
    public void Setup()
    {
        _manager = new MemoryManager();
        _scope = _manager.CreateScope("finite-wave");
        _anchor = _scope.RentAsync<byte>(_pool, 256, MemoryPurpose.Build).GetAwaiter().GetResult();
    }

    /// <summary>Rents 8192 bytes within the anchored chunk's remaining 65280 bytes.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public int HitWave()
    {
        for (int i = 0; i < _wave.Length; i++)
        {
            _wave[i] = _scope.RentAsync<byte>(_pool, 256, MemoryPurpose.Build).GetAwaiter().GetResult();
            _wave[i].Memory.Span[0] = (byte)i;
        }

        return _wave.Length;
    }

    /// <summary>Returns the wave and anchor, then retires all owners outside measurement.</summary>
    [IterationCleanup]
    public void Cleanup()
    {
        foreach (IMemoryBuffer<byte>? buffer in _wave)
        {
            buffer?.Dispose();
        }

        _anchor.Dispose();
        _scope.DisposeAsync().GetAwaiter().GetResult();
        _manager.DisposeAsync().GetAwaiter().GetResult();
    }

    internal void VerifyWave()
    {
        if (_manager.GetSnapshot(_pool).CommittedBytes != 65536)
        {
            throw new InvalidOperationException("Hit wave allocated more than the anchored chunk.");
        }

        for (int index = 0; index < _wave.Length; index++)
        {
            if (_wave[index].Capacity != 256 || _wave[index].Memory.Span[0] != index)
            {
                throw new InvalidOperationException("Wave slices overlap or have incorrect capacity.");
            }
        }
    }
}
