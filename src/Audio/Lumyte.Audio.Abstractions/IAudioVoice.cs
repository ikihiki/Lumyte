namespace Lumyte.Audio.Abstractions;

/// <summary>Backend-owned playback instance borrowing a clip.</summary>
public interface IAudioVoice : IDisposable
{
    /// <summary>Gets the state captured by the most recent device Update.</summary>
    AudioPlaybackState State { get; }

    /// <summary>Gets or sets a finite gain in [0, 1], initially one.</summary>
    float Gain { get; set; }

    /// <summary>Gets or sets a value indicating whether playback loops, initially false.</summary>
    bool Loop { get; set; }

    /// <summary>Starts from the beginning or resumes a paused voice; idempotent when playing.</summary>
    void Play();

    /// <summary>Pauses only a playing voice.</summary>
    void Pause();

    /// <summary>Stops playback; the next Play starts from the beginning.</summary>
    void Stop();
}
