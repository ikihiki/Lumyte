using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution.Benchmark;
using Lumyte.Audio.OpenAL;
using Lumyte.Audio.OpenAL.Benchmark;
using Lumyte.Execution;
using Xunit;

namespace Lumyte.Audio.Benchmark.Tests;

/// <summary>Correctness oracles for the exact benchmark bodies, not timing assertions.</summary>
[Collection("Audio backend serial")]
public sealed class BackendAndExecutionTests
{
    /// <summary>One runtime per process: use environment selection for isolated placement/native coverage.</summary>
    [Fact]
    public void RetainedRuntimeExecutesFiniteWavesAndOrderedCleanup()
    {
        bool native = Environment.GetEnvironmentVariable("LUMYTE_BENCHMARK_EXECUTION_NATIVE") == "1";
        OwnerPlacement placement = Environment.GetEnvironmentVariable("LUMYTE_BENCHMARK_OWNER") == "Host" ? OwnerPlacement.Host : OwnerPlacement.Dedicated;
        ExecutionWorkload fixture = native ? new NativeExecutionBenchmarks() : new CpuExecutionBenchmarks();
        fixture.Placement = placement;
        fixture.Voices = 8;
        int caller = Environment.CurrentManagedThreadId;
        fixture.Setup();
        CountingAudioDevice? counter = fixture.CounterDevice;
        try
        {
            Assert.Equal(placement == OwnerPlacement.Host, fixture.OwnerThreadId == caller);
            for (int wave = 0; wave < 3; wave++)
            {
                int before = counter?.Controls ?? 0;
                Assert.Equal(8, fixture.IndividualControls());
                Assert.Equal(8, fixture.BatchedControls());
                Assert.Equal(501, fixture.ResourceLifecycle());
                if (counter is not null)
                {
                    Assert.Equal(98, counter.Controls - before);
                    Assert.Equal(1, counter.LiveClips);
                    Assert.Equal(8, counter.LiveVoices);
                }
            }
        }
        finally
        {
            fixture.Cleanup();
        }

        if (counter is not null)
        {
            Assert.True(counter.IsDisposed);
            Assert.Equal(0, counter.LiveClips);
            Assert.Equal(0, counter.LiveVoices);
        }
    }

    /// <summary>Native clips retain public format/frame metadata and independent state for odd pooled frame counts.</summary>
    [BenchmarkOpenALFact]
    public void NativeClipUploadAndControlBodiesHaveRealStateOracles()
    {
        OpenALBenchmarks.RequireNullOutput();
        foreach (int channels in new[] { 1, 2 })
        {
            foreach (int frames in new[] { 501, 16385 })
            {
                using (var device = new OpenALDevice())
                {
                    float[] input = Enumerable.Range(0, frames * channels).Select(i => (i % 7) - 3f).ToArray();
                    using IAudioClip clip = device.CreateClip(new(48000, channels), input);
                    Array.Fill(input, float.NaN);
                    Assert.Equal(frames, clip.FrameCount);
                    Assert.Equal(new AudioFormat(48000, channels), clip.Format);
                    using IAudioVoice voice = device.CreateVoice(clip);
                    voice.Seek(frames / 2);
                    device.Update();
                    Assert.Equal(frames / 2, voice.FramePosition);
                    Assert.Equal(AudioPlaybackState.Stopped, voice.State);
                    Assert.Throws<InvalidOperationException>(clip.Dispose);
                }

                var fixture = new OpenALBenchmarks { Channels = channels, Frames = frames, Voices = 8 };
                fixture.Setup();
                try
                {
                    Assert.Equal(frames, fixture.ClipConvertUploadDispose());
                    Assert.Equal(AudioPlaybackState.Stopped, fixture.VoiceCreateDispose());
                    Assert.Equal(8, fixture.ControlAndRefresh());
                    Assert.Equal(208, fixture.CachedSnapshots());
                }
                finally
                {
                    fixture.Cleanup();
                }
            }
        }
    }

    /// <summary>Serializes native contexts and generated static endpoint ownership within this test assembly.</summary>
    [CollectionDefinition("Audio backend serial", DisableParallelization = true)]
    public sealed class AudioBackendCollection
    {
    }

    /// <summary>Native checks are explicit opt-in and still require externally selected null output.</summary>
    public sealed class BenchmarkOpenALFactAttribute : FactAttribute
    {
        /// <summary>Initializes a new instance of the <see cref="BenchmarkOpenALFactAttribute"/> class.</summary>
        public BenchmarkOpenALFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("LUMYTE_BENCHMARK_OPENAL") != "1")
            {
                Skip = "Set LUMYTE_BENCHMARK_OPENAL=1 and ALSOFT_DRIVERS=null before process startup.";
            }
        }
    }
}
