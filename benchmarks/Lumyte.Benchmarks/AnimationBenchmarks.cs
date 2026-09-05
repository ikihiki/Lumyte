using BenchmarkDotNet.Attributes;
using Lumyte.Animation;
using Lumyte.Core.Time;

using static Lumyte.Animation.AnimationKit;

namespace Lumyte.Benchmarks;

[MemoryDiagnoser]
public class AnimationBenchmarks
{
    private static readonly Duration s_sampleTime = Duration.FromSeconds(0.5);
    private AnimationClip clip = null!;
    private CrossfadeTimeline crossfade = null!;
    private AnimationSampleBuffer buffer = null!;
    private ManualClock clock = null!;
    private AnimationPlayer player = null!;
    private float appliedValue;

    [Params(1, 8, 32)]
    public int TrackCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        AnimationTrack[] tracks =
        [
            .. Enumerable.Range(0, TrackCount).Select(index =>
                (AnimationTrack)Track(
                    Channel<float>($"Value{index}"),
                    Interpolators.Float)[
                        Keyframe(Duration.Zero, 0f),
                        Keyframe(Duration.FromSeconds(1), 1f)
                    ]),
        ];
        clip = Clip("Benchmark")[tracks];
        buffer = new(clip);
        clip.SampleInto(s_sampleTime, buffer);
        var target = new AnimationTarget();
        var blend = new AnimationBlend();
        foreach (AnimationTrack track in tracks)
        {
            var channel = (AnimationChannel<float>)track.UntypedChannel;
            target.Bind(channel, Capture);
            blend.Use(channel, Interpolators.Float);
        }

        crossfade = new(
            clip.Sample(Duration.Zero),
            clip,
            Duration.FromSeconds(1),
            blend);
        clock = new();
        player = new(clock);
        player.Play(
            clip,
            target,
            new PlaybackOptions { LoopMode = PlaybackLoopMode.Repeat });
    }

    [Benchmark(Baseline = true)]
    public AnimationSample SampleClip() => clip.Sample(s_sampleTime);

    [Benchmark]
    public AnimationSampleBuffer SampleClipIntoBuffer()
    {
        clip.SampleInto(s_sampleTime, buffer);
        return buffer;
    }

    [Benchmark]
    public AnimationSampleBuffer SampleCrossfadeIntoBuffer()
    {
        crossfade.SampleInto(s_sampleTime, buffer);
        return buffer;
    }

    [Benchmark]
    public float UpdatePlayback()
    {
        clock.Advance(Duration.FromTicks(1));
        player.Update();
        return appliedValue;
    }

    [GlobalCleanup]
    public void Cleanup() => player.Clear();

    private void Capture(float value) => appliedValue = value;
}
