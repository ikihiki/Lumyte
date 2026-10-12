namespace Lumyte.Execution.Sample;

/// <summary>An owned voice used to verify thread affinity without a native audio dependency.</summary>
public sealed class AudioVoice
{
    internal AudioVoice(AudioState owner, int clip)
    {
        Owner = owner;
        Clip = clip;
    }

    /// <summary>Gets the clip identity.</summary>
    public int Clip { get; }

    /// <summary>Gets a value indicating whether playback was requested on the owner.</summary>
    public bool Playing { get; internal set; }

    /// <summary>Gets a value indicating whether destruction completed.</summary>
    public bool Destroyed { get; internal set; }

    internal AudioState Owner { get; }
}
