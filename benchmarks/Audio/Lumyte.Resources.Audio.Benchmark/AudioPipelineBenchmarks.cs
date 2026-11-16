using BenchmarkDotNet.Attributes;
using Lumyte.Audio.Execution;
using Lumyte.Execution;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.Audio.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Audio.Benchmark;

/// <summary>Production DI, manager, decode and clip stages using strict managed PCM storage, not OpenAL.</summary>
[BenchmarkCategory("Audio", "CPU", "CPUControl", "Resources", "ClipStage", "DependencyInjection", "Screening", "Comparison", "Diagnostic")]
public class AudioPipelineBenchmarks
{
    private readonly ResourceKey<AudioClip> _warmKey = new("wave/warm-clip");
    private readonly ResourceKey<AudioClip> _coldKey = new("wave/cold-clip");
    private ServiceProvider _provider = null!;
    private ExecutionRuntime _execution = null!;
    private ResourceManager _manager = null!;
    private ResourceReference<AudioClip> _resident = null!;
    private CpuClipDevice.Factory _factory = null!;

    /// <summary>Gets or sets bounded stereo PCM16 frame count.</summary>
    [Params(257, 16384)]
    public int Frames { get; set; }

    /// <summary>Gets the controlled device counters; read only after command completion.</summary>
    public CpuClipDevice Device => _factory.Device;

    /// <summary>Gets the source open counters.</summary>
    public WaveSource Source { get; private set; } = null!;

    /// <summary>Resolves real DI registrations and starts one retained audio owner per benchmark case.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _factory = new CpuClipDevice.Factory();
        var audio = new AudioModule(_factory);
        Source = new WaveSource(SyntheticWave.Create("16", 2, Frames), false);
        var services = new ServiceCollection();
        services.AddSingleton(audio);
        services.AddSingleton(Source);
        services.AddMemoryManagement();
        services.AddAudioResources();
        services.AddResources(builder => builder.UseSource<RegisteredWaveSource>().AddAudioPipeline());
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _execution = _provider.GetRequiredService<ExecutionRuntime>();
        _manager = _provider.GetRequiredService<ResourceManager>();
        _execution.PrepareOnCurrentHost(OwnerPlacement.Dedicated, CpuExecutionMode.ThreadPool, 1, new ExecutionOptions());
        _execution.StartAsync().AsTask().GetAwaiter().GetResult();
        _resident = _manager.RetainAsync(_warmKey).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Releases clips on the owner before shutting down the owner and DI container.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _resident.Dispose();
        _manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _execution.StopAsync().AsTask().GetAwaiter().GetResult();
        _provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Cold finite-key load includes decode, owner PCM copy, publish, pin, release and trim.</summary>
    /// <returns>Actual clip frame count.</returns>
    [Benchmark]
    public async Task<long> ColdDecodeCreateReleaseAsync()
    {
        long frames;
        using (ResourceReference<AudioClip> reference = await _manager.RetainAsync(_coldKey))
        using (ResourceLease<AudioClip> lease = reference.Pin())
        {
            frames = lease.Value.FrameCount;
        }

        await _manager.TrimAsync();
        return frames;
    }

    /// <summary>Warm ownership-only acquisition; no source, decode or clip creation.</summary>
    /// <returns>Actual clip frame count.</returns>
    [Benchmark]
    public async Task<long> WarmClipRetainPinAsync()
    {
        using ResourceReference<AudioClip> reference = await _manager.RetainAsync(_warmKey);
        using ResourceLease<AudioClip> lease = reference.Pin();
        return lease.Value.FrameCount;
    }

    /// <summary>Pins one already retained clip revision.</summary>
    /// <returns>Actual clip frame count.</returns>
    [Benchmark]
    public long ResidentClipPin()
    {
        using ResourceLease<AudioClip> lease = _resident.Pin();
        return lease.Value.FrameCount;
    }

    /// <summary>Public DI consumer adapter delegates to the configured immutable source.</summary>
    /// <param name="source">Shared observable source.</param>
    public sealed class RegisteredWaveSource(WaveSource source) : IResourceSource
    {
        /// <inheritdoc/>
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => source.OpenSnapshotAsync(id, cancellationToken);

        /// <inheritdoc/>
        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => source.GetRevisionAsync(id, cancellationToken);
    }
}
