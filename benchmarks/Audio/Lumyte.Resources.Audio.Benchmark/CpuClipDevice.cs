using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;

namespace Lumyte.Resources.Audio.Benchmark;

/// <summary>Strict owner-confined PCM-copy control; not native audio and never renders output.</summary>
public sealed class CpuClipDevice : IAudioDevice
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _disposed;
    private bool _invalidated;
    private int _live;

    /// <summary>Gets completed clip creations.</summary>
    public int Created { get; private set; }

    /// <summary>Gets completed clip releases.</summary>
    public int Released { get; private set; }

    /// <summary>Gets checksum of the most recently copied clip.</summary>
    public double LastChecksum { get; private set; }

    /// <inheritdoc/>
    public AudioOutputState OutputState
    {
        get
        {
            CheckOwner();
            return _invalidated ? AudioOutputState.Unavailable : AudioOutputState.Running;
        }
    }

    /// <inheritdoc/>
    public void Invalidate()
    {
        CheckOwner();
        _invalidated = true;
    }

    /// <inheritdoc/>
    public void Update() => CheckAvailable();

    /// <inheritdoc/>
    public ValueTask ResumeOutputAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckAvailable();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public IAudioClip CreateClip(AudioFormat format, ReadOnlySpan<float> samples)
    {
        CheckAvailable();
        if (format.SampleRate <= 0 || format.Channels is not (1 or 2) || samples.IsEmpty || samples.Length % format.Channels != 0)
        {
            throw new ArgumentException("Invalid PCM shape.");
        }

        foreach (float sample in samples)
        {
            if (!float.IsFinite(sample))
            {
                throw new ArgumentException("Nonfinite sample.");
            }
        }

        float[] copy = samples.ToArray();
        LastChecksum = SyntheticWave.Checksum(copy);
        Created++;
        _live++;
        return new Clip(this, format, copy);
    }

    /// <inheritdoc/>
    public IAudioVoice CreateVoice(IAudioClip clip, AudioBus bus = AudioBus.Effects)
        => throw new NotSupportedException("This CPU storage control has no voice or output implementation.");

    /// <inheritdoc/>
    public void SetBusGain(AudioBus bus, float gain)
        => throw new NotSupportedException("This CPU storage control has no mixer.");

    /// <inheritdoc/>
    public void Dispose()
    {
        CheckOwner();
        if (_live != 0)
        {
            throw new InvalidOperationException("Clip storage remains live.");
        }

        _disposed = true;
    }

    private void CheckOwner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_owner != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Operation outside the actual audio owner.");
        }
    }

    private void CheckAvailable()
    {
        CheckOwner();
        if (_invalidated)
        {
            throw new AudioDeviceUnavailableException();
        }
    }

    internal sealed class Factory : IAudioDeviceFactory
    {
        internal CpuClipDevice Device { get; private set; } = null!;

        public Task<IAudioDevice> CreateAsync()
        {
            Device = new CpuClipDevice();
            return Task.FromResult<IAudioDevice>(Device);
        }
    }

    private sealed class Clip(CpuClipDevice device, AudioFormat format, float[] storage) : IAudioClip
    {
        private float[]? _samples = storage;

        public IAudioDevice Device => device;

        public AudioFormat Format => format;

        public long FrameCount { get; } = storage.Length / format.Channels;

        public void Dispose()
        {
            device.CheckOwner();
            ObjectDisposedException.ThrowIf(_samples is null, this);
            _samples = null;
            device._live--;
            device.Released++;
        }
    }
}
