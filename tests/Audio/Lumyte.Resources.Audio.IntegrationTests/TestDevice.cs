using Lumyte.Audio.Abstractions;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal sealed class TestDevice : IAudioDevice
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private int _clips;
    private int _voices;
    private int _output = (int)AudioOutputState.Running;
    private bool _disposed;
    private int _voiceReleaseFailures;
    private int _clipReleaseFailures;
    private int _clipCreationFailures;
    private int _voiceCreationFailures;
    private int _loseDuringNextClip;
    private int _createdClips;
    private int _returnedClips;

    public AudioOutputState OutputState
    {
        get
        {
            Verify();
            return (AudioOutputState)Volatile.Read(ref _output);
        }
    }

    internal bool IsDisposed => _disposed;

    internal int CreatedClips => Volatile.Read(ref _createdClips);

    internal int ReturnedClips => Volatile.Read(ref _returnedClips);

    public void Update() => VerifyAvailable();

    public ValueTask ResumeOutputAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VerifyAvailable();
        SetOutput(AudioOutputState.Running);
        return ValueTask.CompletedTask;
    }

    public IAudioClip CreateClip(AudioFormat format, ReadOnlySpan<float> samples)
    {
        VerifyAvailable();
        if (Interlocked.Exchange(ref _clipCreationFailures, 0) != 0)
        {
            throw new InvalidOperationException("Injected clip creation failure.");
        }

        _clips++;
        Interlocked.Increment(ref _createdClips);
        var clip = new TestClip(this, format, samples.Length / format.Channels);
        if (Interlocked.Exchange(ref _loseDuringNextClip, 0) != 0)
        {
            SetOutput(AudioOutputState.Unavailable);
        }

        return clip;
    }

    public IAudioVoice CreateVoice(IAudioClip clip, AudioBus bus = AudioBus.Effects)
    {
        VerifyAvailable();
        if (Interlocked.Exchange(ref _voiceCreationFailures, 0) != 0)
        {
            throw new InvalidOperationException("Injected voice creation failure.");
        }

        var original = (TestClip)clip;
        if (!ReferenceEquals(original.Device, this))
        {
            throw new InvalidOperationException("Clip belongs to another device generation.");
        }

        original.Borrowers++;
        _voices++;
        return new TestVoice(this, original);
    }

    public void SetBusGain(AudioBus bus, float gain) => VerifyAvailable();

    public void Dispose()
    {
        Verify();
        if (_clips != 0 || _voices != 0)
        {
            throw new InvalidOperationException("Resources must release voices and clips before the audio device.");
        }

        _disposed = true;
    }

    internal void Verify()
    {
        if (_owner != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Backend operation ran outside its owner.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal void VerifyAvailable()
    {
        Verify();
        if ((AudioOutputState)Volatile.Read(ref _output) == AudioOutputState.Unavailable)
        {
            throw new AudioDeviceUnavailableException();
        }
    }

    internal void SetOutput(AudioOutputState state) => Volatile.Write(ref _output, (int)state);

    internal void LoseDuringNextClip() => Volatile.Write(ref _loseDuringNextClip, 1);

    internal void FailNextVoiceRelease() => Interlocked.Increment(ref _voiceReleaseFailures);

    internal void FailNextClipRelease() => Interlocked.Increment(ref _clipReleaseFailures);

    internal void FailNextClipCreation() => Volatile.Write(ref _clipCreationFailures, 1);

    internal void FailNextVoiceCreation() => Volatile.Write(ref _voiceCreationFailures, 1);

    internal void VerifyClipRelease()
    {
        Verify();
        if (Volatile.Read(ref _clipReleaseFailures) > 0)
        {
            Interlocked.Decrement(ref _clipReleaseFailures);
            throw new InvalidOperationException("Injected clip release failure.");
        }
    }

    internal void VerifyVoiceRelease()
    {
        Verify();
        if (Volatile.Read(ref _voiceReleaseFailures) > 0)
        {
            Interlocked.Decrement(ref _voiceReleaseFailures);
            throw new InvalidOperationException("Injected voice release failure.");
        }
    }

    internal void ReturnClip()
    {
        _clips--;
        Interlocked.Increment(ref _returnedClips);
    }

    internal void ReturnVoice() => _voices--;
}
