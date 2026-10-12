namespace Lumyte.Audio.Abstractions;

/// <summary>Backend-owned PCM storage. Disposal is rejected while a voice borrows it.</summary>
public interface IAudioClip : IDisposable
{
    /// <summary>Gets the input PCM format.</summary>
    AudioFormat Format { get; }

    /// <summary>Gets the number of PCM frames.</summary>
    long FrameCount { get; }
}
