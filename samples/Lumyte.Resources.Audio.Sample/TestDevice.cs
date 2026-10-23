using Lumyte.Audio.Abstractions;

namespace Lumyte.Resources.Audio.Sample;

internal sealed class TestDevice : IAudioDevice
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private int _clips;
    private int _voices;

    public AudioOutputState OutputState => AudioOutputState.Running;

    public void Update() => Verify();

    public ValueTask ResumeOutputAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public IAudioClip CreateClip(AudioFormat format, ReadOnlySpan<float> samples)
    {
        Verify();
        _clips++;
        return new TestClip(this, format, samples.Length / format.Channels);
    }

    public IAudioVoice CreateVoice(IAudioClip clip, AudioBus bus = AudioBus.Effects)
    {
        Verify();
        var original = (TestClip)clip;
        original.Borrowers++;
        _voices++;
        return new TestVoice(this, original);
    }

    public void SetBusGain(AudioBus bus, float gain) => Verify();

    public void Dispose()
    {
        Verify();
        if (_clips != 0 || _voices != 0)
        {
            throw new InvalidOperationException("Resources must release voices and clips before the audio device.");
        }
    }

    internal void Verify()
    {
        if (_owner != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Backend operation ran outside its owner.");
        }
    }

    internal void ReturnClip() => _clips--;

    internal void ReturnVoice() => _voices--;
}
