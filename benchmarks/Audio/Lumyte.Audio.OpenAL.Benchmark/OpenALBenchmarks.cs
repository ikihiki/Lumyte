using BenchmarkDotNet.Attributes;
using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.OpenAL.Benchmark;

/// <summary>Real OpenAL Soft null-output workloads; native failures are never suppressed.</summary>
[BenchmarkCategory("Audio", "OpenAL", "Native", "Screening", "Comparison", "Diagnostic")]
public class OpenALBenchmarks
{
    private OpenALDevice _device = null!;
    private IAudioClip _clip = null!;
    private IAudioVoice[] _voices = [];
    private float[] _samples = [];

    /// <summary>Gets or sets interleaved channel count.</summary>
    [Params(1, 2)]
    public int Channels { get; set; } = 1;

    /// <summary>Gets or sets PCM frame count, including a non-pool-sized upload.</summary>
    [Params(501, 16385)]
    public int Frames { get; set; } = 501;

    /// <summary>Gets or sets live voice fanout.</summary>
    [Params(1, 32)]
    public int Voices { get; set; } = 1;

    /// <summary>Refuses any implicit physical output selection before loading native APIs.</summary>
    public static void RequireNullOutput()
    {
        if (Environment.GetEnvironmentVariable("ALSOFT_DRIVERS") != "null")
        {
            throw new InvalidOperationException("Set ALSOFT_DRIVERS=null before starting the process; physical output is prohibited.");
        }
    }

    /// <summary>Creates the native context on the invocation thread.</summary>
    [GlobalSetup]
    public void Setup()
    {
        RequireNullOutput();
        _samples = Enumerable.Range(0, Frames * Channels).Select(i => ((i % 31) - 15) / 8f).ToArray();
        _device = new OpenALDevice(maxVoices: Math.Max(Voices, 64));
        _clip = _device.CreateClip(new(48000, Channels), _samples);
        _voices = Enumerable.Range(0, Voices).Select(_ => _device.CreateVoice(_clip)).ToArray();
    }

    /// <summary>Converts float PCM to native PCM16, uploads and destroys exactly one clip.</summary>
    /// <returns>The actual native clip frame count.</returns>
    [Benchmark]
    public long ClipConvertUploadDispose()
    {
        using IAudioClip clip = _device.CreateClip(new(48000, Channels), _samples);
        return clip.FrameCount;
    }

    /// <summary>Creates and destroys a native source borrowing the retained clip.</summary>
    /// <returns>The created voice's initial state.</returns>
    [Benchmark]
    public AudioPlaybackState VoiceCreateDispose()
    {
        using IAudioVoice voice = _device.CreateVoice(_clip);
        return voice.State;
    }

    /// <summary>Exercises finite playback controls, bus fanout, native refresh and cached snapshots.</summary>
    /// <returns>Number of voices observed stopped at frame zero with the requested cached controls.</returns>
    [Benchmark]
    public int ControlAndRefresh()
    {
        foreach (IAudioVoice voice in _voices)
        {
            voice.Loop = true;
            voice.Gain = 0.25f;
            voice.Play();
            voice.Pause();
            voice.Seek(Frames / 2);
            voice.Stop();
        }

        _device.SetBusGain(AudioBus.Master, 0.5f);
        _device.SetBusGain(AudioBus.Effects, 0.75f);
        _device.Update();
        return _voices.Count(voice => voice.State == AudioPlaybackState.Stopped && voice.FramePosition == 0 && voice.Loop && voice.Gain == 0.25f);
    }

    /// <summary>Reads only cached snapshots without a native update.</summary>
    /// <returns>A consumed state checksum.</returns>
    [Benchmark]
    public long CachedSnapshots()
    {
        long sum = 0;
        foreach (IAudioVoice voice in _voices)
        {
            sum += voice.FramePosition + (int)voice.State + (voice.Loop ? 1 : 0) + (long)(voice.Gain * 100);
        }

        return sum;
    }

    /// <summary>Releases native sources before buffers and context, on their owner thread.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (IAudioVoice voice in _voices)
        {
            voice.Dispose();
        }

        _clip?.Dispose();
        _device?.Dispose();
    }
}
