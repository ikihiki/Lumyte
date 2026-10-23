using Lumyte.Audio.Abstractions;

namespace Lumyte.Resources.Audio.Sample;

internal sealed class TestVoice(TestDevice device, TestClip clip) : IAudioVoice
{
    public AudioPlaybackState State { get; private set; }

    public float Gain { get; set; } = 1;

    public bool Loop { get; set; }

    public void Play()
    {
        device.Verify();
        State = AudioPlaybackState.Playing;
    }

    public void Pause()
    {
        device.Verify();
        State = AudioPlaybackState.Paused;
    }

    public void Stop()
    {
        device.Verify();
        State = AudioPlaybackState.Stopped;
    }

    public void Dispose()
    {
        device.Verify();
        clip.Borrowers--;
        device.ReturnVoice();
    }
}
