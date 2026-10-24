using Lumyte.Audio.Abstractions;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal sealed class TestVoice(TestDevice device, TestClip clip) : IAudioVoice
{
    public AudioPlaybackState State { get; private set; }

    public float Gain { get; set; } = 1;

    public bool Loop { get; set; }

    internal TestDevice Device => device;

    internal TestClip Clip => clip;

    public void Play()
    {
        device.VerifyAvailable();
        State = AudioPlaybackState.Playing;
    }

    public void Pause()
    {
        device.VerifyAvailable();
        State = AudioPlaybackState.Paused;
    }

    public void Stop()
    {
        device.VerifyAvailable();
        State = AudioPlaybackState.Stopped;
    }

    public void Dispose()
    {
        device.VerifyVoiceRelease();
        clip.Borrowers--;
        device.ReturnVoice();
    }
}
