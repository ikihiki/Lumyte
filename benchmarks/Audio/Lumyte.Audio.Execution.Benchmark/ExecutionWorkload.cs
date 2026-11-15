using BenchmarkDotNet.Attributes;
using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution.Benchmark;

/// <summary>Retained execution runtime; each benchmark process connects static audio ports only once.</summary>
public abstract class ExecutionWorkload
{
    private ExecutionRuntime _runtime = null!;
    private AudioModule _audio = null!;
    private AudioDeviceLease _lease = null!;
    private AudioClip _clip;
    private AudioVoice[] _voices = [];
    private CommandBatch _batch = null!;

    /// <summary>Gets or sets explicit owner placement. Run each combination in its own benchmark process.</summary>
    [Params(OwnerPlacement.Host, OwnerPlacement.Dedicated)]
    public OwnerPlacement Placement { get; set; } = OwnerPlacement.Dedicated;

    /// <summary>Gets or sets command fanout.</summary>
    [Params(1, 32)]
    public int Voices { get; set; } = 1;

    /// <summary>Gets the observed backend owner identity.</summary>
    public int OwnerThreadId => _lease.Device.OwnerThreadId;

    /// <summary>Gets the CPU contract device when selected, never a native substitute.</summary>
    public CountingAudioDevice? CounterDevice { get; private set; }

    /// <summary>Connects once, starts once, and retains one lease/clip and finite source set.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _audio = new AudioModule(new Factory(this));
        _runtime = new ExecutionRuntime([_audio]);
        _runtime.PrepareOnCurrentHost(Placement, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Wait(_runtime.StartAsync().AsTask());
        _lease = Wait(_audio.Devices.AcquireAsync());
        _clip = Wait(_lease.Device.CreateClipAsync(new(48000, 2), new float[96000]));
        _voices = Enumerable.Range(0, Voices).Select(_ => Wait(_clip.CreateVoiceAsync())).ToArray();
        _batch = new CommandBatch((Voices * 6) + 2);
    }

    /// <summary>Dispatches individually completed owner controls and an explicit refresh fence.</summary>
    /// <returns>Actual stopped voice snapshot count.</returns>
    public int IndividualControls()
    {
        foreach (AudioVoice voice in _voices)
        {
            Wait(voice.SetLoopAsync(true));
            Wait(voice.SetGainAsync(0.25f));
            Wait(voice.PlayAsync());
            Wait(voice.PauseAsync());
            Wait(voice.SeekAsync(123));
            Wait(voice.StopAsync());
        }

        Wait(_lease.Device.SetBusGainAsync(AudioBus.Effects, 0.5f));
        Wait(_lease.Device.RefreshAsync());
        return Observe();
    }

    /// <summary>Stages the same controls atomically in one owner/group batch, observes completion, and reuses staging.</summary>
    /// <returns>Actual stopped voice snapshot count.</returns>
    public int BatchedControls()
    {
        foreach (AudioVoice voice in _voices)
        {
            voice.AddSetLoopTo(_batch, true);
            voice.AddSetGainTo(_batch, 0.25f);
            voice.AddPlayTo(_batch);
            voice.AddPauseTo(_batch);
            voice.AddSeekTo(_batch, 123);
            voice.AddStopTo(_batch);
        }

        _lease.Device.AddSetBusGainTo(_batch, AudioBus.Effects, 0.5f);
        _lease.Device.AddRefreshTo(_batch);
        CommandEnqueueStatus status = _runtime.Submit(_batch, out CommandRun? run);
        if (status != CommandEnqueueStatus.Accepted)
        {
            throw new InvalidOperationException($"Batch rejected: {status}.");
        }

        using (run!)
        {
            Wait(run!.Completion);
            run.ThrowIfFailed();
        }

        _batch.Reset();
        return Observe();
    }

    /// <summary>Dispatches source/clip creation and ordered destruction while retaining the runtime and lease.</summary>
    /// <returns>Actual PCM frames created.</returns>
    public long ResourceLifecycle()
    {
        AudioClip clip = Wait(_lease.Device.CreateClipAsync(new(48000, 1), new float[501]));
        try
        {
            AudioVoice voice = Wait(clip.CreateVoiceAsync());
            Wait(voice.DestroyAsync());
            return clip.FrameCount;
        }
        finally
        {
            Wait(clip.DestroyClipAsync());
        }
    }

    /// <summary>Releases voices, clip, lease, staging, then the retained runtime.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (AudioVoice voice in _voices)
        {
            Wait(voice.DestroyAsync());
        }

        Wait(_clip.DestroyClipAsync());
        _lease.Dispose();
        _batch.Dispose();
        Wait(_runtime.StopAsync().AsTask());
    }

    /// <summary>Creates the selected device on its real execution owner.</summary>
    /// <returns>The owned backend.</returns>
    protected abstract IAudioDevice CreateBackend();

    private int Observe() => _voices.Count(voice => voice.State == AudioPlaybackState.Stopped && voice.FramePosition == 0 && voice.Loop && voice.Gain == 0.25f);

    private T Wait<T>(ValueTask<T> operation)
    {
        Task<T> task = operation.AsTask();
        Wait(task);
        return task.GetAwaiter().GetResult();
    }

    private void Wait(Task task)
    {
        while (!task.IsCompleted)
        {
            _runtime.PumpHostLifecycle(256, TimeSpan.FromMilliseconds(10));
            _runtime.PumpJobs(256, TimeSpan.FromMilliseconds(10));
            if (Placement == OwnerPlacement.Host && _runtime.Status is ExecutionStatus.Running or ExecutionStatus.Quiescing)
            {
                _runtime.RunHostPhase(_audio.HostPhase, 256, TimeSpan.FromMilliseconds(10));
            }

            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    private sealed class Factory(ExecutionWorkload workload) : IAudioDeviceFactory
    {
        public Task<IAudioDevice> CreateAsync()
        {
            IAudioDevice device = workload.CreateBackend();
            workload.CounterDevice = device as CountingAudioDevice;
            return Task.FromResult(device);
        }
    }
}
