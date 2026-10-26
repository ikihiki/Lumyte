namespace Lumyte.Audio.Abstractions;

/// <summary>Backend-owned playback instance borrowing a clip.</summary>
public interface IAudioVoice : IDisposable
{
    /// <summary>Gets the snapshot captured by the most recent device Update; readable from any thread.</summary>
    AudioPlaybackState State { get; }

    /// <summary>Gets the PCM frame position captured by the most recent device Update; readable from any thread.</summary>
    /// <remarks>Natural completion reports the clip's FrameCount. Stop resets the position to zero.</remarks>
    long FramePosition { get; }

    /// <summary>Gets or sets a finite gain in [0, 1], initially one. The getter reads cached state from any thread; the setter requires the owner.</summary>
    float Gain { get; set; }

    /// <summary>Gets or sets a value indicating whether playback loops, initially false. The getter reads cached state from any thread; the setter requires the owner.</summary>
    bool Loop { get; set; }

    /// <summary>Starts at the selected frame or resumes a paused voice; restarts completed playback from zero and is idempotent when playing.</summary>
    void Play();

    /// <summary>Pauses only a playing voice.</summary>
    void Pause();

    /// <summary>Stops playback; the next Play starts from the beginning.</summary>
    void Stop();

    /// <summary>Moves to a PCM frame in [0, FrameCount] on the owner thread, preserving playing, paused, or stopped state.</summary>
    /// <param name="framePosition">The absolute PCM frame position, independent of channel count.</param>
    /// <remarks>
    /// Seeking completed playback to an interior frame stops it until Play. For looping playback, FrameCount selects zero.
    /// Otherwise the endpoint completes playing playback; paused or stopped playback stays at the endpoint until Play completes it.
    /// The new state and position become observable at the next device Update.
    /// </remarks>
    void Seek(long framePosition);
}
