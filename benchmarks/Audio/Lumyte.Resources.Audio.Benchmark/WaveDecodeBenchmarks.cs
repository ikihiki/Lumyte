using BenchmarkDotNet.Attributes;

namespace Lumyte.Resources.Audio.Benchmark;

/// <summary>Complete in-memory decode and checksum; input generation is outside measurement.</summary>
[BenchmarkCategory("Audio", "CPU", "Decode", "Screening", "Comparison", "Diagnostic")]
public class WaveDecodeBenchmarks
{
    private byte[] _wave = null!;

    /// <summary>Gets or sets the production-supported encoding.</summary>
    [Params("8", "16", "24", "32", "F32")]
    public string Encoding { get; set; } = "16";

    /// <summary>Gets or sets interleaved channel count.</summary>
    [Params(1, 2)]
    public int Channels { get; set; }

    /// <summary>Gets or sets finite frame count, including an odd padding boundary.</summary>
    [Params(1, 257, 16384)]
    public int Frames { get; set; }

    /// <summary>Generates the immutable encoded file.</summary>
    [GlobalSetup]
    public void Setup() => _wave = SyntheticWave.Create(Encoding, Channels, Frames);

    /// <summary>Allocates actual decoded PCM and consumes every sample.</summary>
    /// <returns>Position-weighted sample checksum.</returns>
    [Benchmark]
    public double DecodeAndChecksum() => SyntheticWave.Checksum(WaveAudioDecoder.Decode(_wave).Samples.Span);
}
