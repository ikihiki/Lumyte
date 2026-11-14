using Lumyte.Audio.Abstractions;
using Lumyte.Resources;
using Lumyte.Resources.Audio;
using Lumyte.Resources.Audio.Benchmark;
using Xunit;

namespace Lumyte.Audio.Benchmark.Tests;

/// <summary>Exact PCM and bounded ownership assertions; no timing or GC thresholds.</summary>
public sealed class DecodeAndResourceTests
{
    /// <summary>Every supported codec and channel layout yields independently known sample values.</summary>
    [Fact]
    public void SupportedWaveMatrixMatchesExactOracle()
    {
        foreach (string encoding in new[] { "8", "16", "24", "32", "F32" })
        {
            foreach (int channels in new[] { 1, 2 })
            {
                foreach (int frames in new[] { 1, 257, 16384 })
                {
                    byte[] wave = SyntheticWave.Create(encoding, channels, frames);
                    Assert.Equal(wave, SyntheticWave.Create(encoding, channels, frames));
                    AudioPcmData pcm = WaveAudioDecoder.Decode(wave);
                    Assert.Equal(new AudioFormat(48000, channels), pcm.Format);
                    Assert.Equal(frames * channels, pcm.Samples.Length);
                    double checksum = 0;
                    for (int i = 0; i < pcm.Samples.Length; i++)
                    {
                        float expected = ((i % 7) - 3) / 4f;
                        Assert.Equal(expected, pcm.Samples.Span[i]);
                        checksum += expected * ((i % 13) + 1);
                    }

                    var benchmark = new WaveDecodeBenchmarks { Encoding = encoding, Channels = channels, Frames = frames };
                    benchmark.Setup();
                    Assert.Equal(checksum, benchmark.DecodeAndChecksum());
                }
            }
        }
    }

    /// <summary>Truncated RIFF, format, data and odd padding all reject.</summary>
    [Fact]
    public void TruncationDiagnosticsRejectAndValidOddBoundaryDecodes()
    {
        byte[] wave = SyntheticWave.Create("8", 1, 1);
        Assert.Equal(46, wave.Length);
        Assert.Equal(-0.75f, WaveAudioDecoder.Decode(wave).Samples.Span[0]);
        foreach (int length in new[] { 11, 35, 44, 45 })
        {
            Assert.Throws<InvalidDataException>(() => WaveAudioDecoder.Decode(wave.AsSpan(0, length)));
            var benchmark = new WaveBoundaryBenchmarks { SuppliedBytes = length };
            benchmark.Setup();
            Assert.Equal(1, benchmark.RejectTruncatedFile());
        }
    }

    /// <summary>Fresh stream reads match for seekable and nonseekable partial sources.</summary>
    /// <returns>The completed source checks.</returns>
    [Fact]
    public async Task SourceShapesAndCancellationAsync()
    {
        byte[] wave = SyntheticWave.Create("24", 2, 257);
        foreach (bool partial in new[] { false, true })
        {
            var source = new WaveSource(wave, partial);
            await using ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(new ResourceId("wave"));
            Assert.Equal(partial ? null : (long?)wave.Length, snapshot.Length);
            for (int repeat = 0; repeat < 2; repeat++)
            {
                await using Stream stream = await snapshot.OpenReadAsync();
                Assert.Equal(!partial, stream.CanSeek);
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy);
                Assert.Equal(wave, copy.ToArray());
            }

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await snapshot.OpenReadAsync(cancellationToken: cancellation.Token));
            Assert.Equal(2, source.Opens);
        }
    }

    /// <summary>Actual resource runtime cold operations reopen; warm and pin paths do not.</summary>
    /// <returns>The completed CPU scheduler checks.</returns>
    [Fact]
    public async Task ScheduledDecodeColdAndWarmOwnershipAsync()
    {
        foreach (bool partial in new[] { false, true })
        {
            var benchmark = new DecodeResourceBenchmarks { Frames = 257, PartialReads = partial };
            benchmark.Setup();
            try
            {
                Assert.Equal(1, benchmark.Source.Opens);
                Assert.Equal(514, await benchmark.WarmRetainAndPinAsync());
                Assert.Equal(514, benchmark.ResidentPin());
                Assert.Equal(1, benchmark.Source.Opens);
                double expected = SyntheticWave.Checksum(WaveAudioDecoder.Decode(SyntheticWave.Create("16", 2, 257)).Samples.Span);
                Assert.Equal(expected, await benchmark.ColdRetainDecodeAndTrimAsync());
                Assert.Equal(expected, await benchmark.ColdRetainDecodeAndTrimAsync());
                Assert.Equal(3, benchmark.Source.Opens);
            }
            finally
            {
                benchmark.Cleanup();
            }
        }
    }

    /// <summary>Only one owner-registering fixture: real DI pipeline copies, publishes and releases clips.</summary>
    /// <returns>The completed owner pipeline check.</returns>
    [ResourcePipelineFact]
    public async Task DiClipPipelineHasBalancedColdAndWarmOwnershipAsync()
    {
        var benchmark = new AudioPipelineBenchmarks { Frames = 257 };
        benchmark.Setup();
        try
        {
            Assert.Equal(1, benchmark.Device.Created);
            Assert.Equal(257, await benchmark.WarmClipRetainPinAsync());
            Assert.Equal(257, benchmark.ResidentClipPin());
            Assert.Equal(1, benchmark.Source.Opens);
            Assert.Equal(1, benchmark.Device.Created);
            Assert.Equal(257, await benchmark.ColdDecodeCreateReleaseAsync());
            Assert.Equal(257, await benchmark.ColdDecodeCreateReleaseAsync());
            Assert.Equal(3, benchmark.Source.Opens);
            Assert.Equal(3, benchmark.Device.Created);
            Assert.Equal(2, benchmark.Device.Released);
            double expected = 0;
            for (int i = 0; i < 514; i++)
            {
                expected += (((i % 7) - 3) / 4f) * ((i % 13) + 1);
            }

            Assert.Equal(expected, benchmark.Device.LastChecksum);
        }
        finally
        {
            benchmark.Cleanup();
        }

        Assert.Equal(benchmark.Device.Created, benchmark.Device.Released);
    }

    /// <summary>The CPU control rejects invalid shape, nonfinite input, premature device release and foreign owners.</summary>
    [Fact]
    public void CpuStorageControlEnforcesContract()
    {
        var device = new CpuClipDevice();
        Assert.Throws<ArgumentException>(() => device.CreateClip(new(0, 1), new float[] { 0 }));
        Assert.Throws<ArgumentException>(() => device.CreateClip(new(48000, 2), new float[] { 0 }));
        Assert.Throws<ArgumentException>(() => device.CreateClip(new(48000, 1), new float[] { float.NaN }));
        float[] samples = { -0.75f, 0, 0.5f };
        IAudioClip clip = device.CreateClip(new(48000, 1), samples);
        Array.Fill(samples, float.NaN);
        Assert.Equal(3, clip.FrameCount);
        Assert.Equal(0.75, device.LastChecksum);
        Assert.Throws<InvalidOperationException>(device.Dispose);
        Exception? foreignError = null;
        var foreignOwner = new Thread(() =>
        {
            try
            {
                device.Update();
            }
            catch (Exception error)
            {
                foreignError = error;
            }
        });
        foreignOwner.Start();
        foreignOwner.Join();
        Assert.IsType<InvalidOperationException>(foreignError);
        clip.Dispose();
        Assert.Throws<ObjectDisposedException>(clip.Dispose);
        device.Invalidate();
        Assert.Equal(AudioOutputState.Unavailable, device.OutputState);
        Assert.Throws<AudioDeviceUnavailableException>(() => device.CreateClip(new(48000, 1), new float[] { 0 }));
        device.Dispose();
        Assert.Equal(device.Created, device.Released);
    }

    /// <summary>Requires a separate filtered process because audio command ports are process-global.</summary>
    public sealed class ResourcePipelineFactAttribute : FactAttribute
    {
        /// <summary>Initializes a new instance of the <see cref="ResourcePipelineFactAttribute"/> class.</summary>
        public ResourcePipelineFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("LUMYTE_BENCHMARK_RESOURCE_PIPELINE") != "1")
            {
                Skip = "Set LUMYTE_BENCHMARK_RESOURCE_PIPELINE=1 and filter to DiClipPipelineHasBalancedColdAndWarmOwnershipAsync in a separate process.";
            }
        }
    }
}
