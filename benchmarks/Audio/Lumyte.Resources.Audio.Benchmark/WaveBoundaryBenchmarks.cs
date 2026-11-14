using BenchmarkDotNet.Attributes;

namespace Lumyte.Resources.Audio.Benchmark;

/// <summary>Malformed complete-file rejection is diagnostic, not a decode throughput comparison.</summary>
[BenchmarkCategory("Audio", "CPU", "Decode", "Diagnostic")]
public class WaveBoundaryBenchmarks
{
    private byte[] _wave = null!;

    /// <summary>Gets or sets truncation at RIFF, format, data or odd-padding boundaries.</summary>
    [Params(11, 35, 44, 45)]
    public int SuppliedBytes { get; set; }

    /// <summary>Creates a one-byte PCM file with mandatory odd padding.</summary>
    [GlobalSetup]
    public void Setup() => _wave = SyntheticWave.Create("8", 1, 1);

    /// <summary>Consumes the expected public invalid-data rejection.</summary>
    /// <returns>One only when the decoder rejects the truncated input.</returns>
    [Benchmark]
    public int RejectTruncatedFile()
    {
        try
        {
            WaveAudioDecoder.Decode(_wave.AsSpan(0, SuppliedBytes));
        }
        catch (InvalidDataException)
        {
            return 1;
        }

        throw new InvalidOperationException("Truncated WAVE was accepted.");
    }
}
