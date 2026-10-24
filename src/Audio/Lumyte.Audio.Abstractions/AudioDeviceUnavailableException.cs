namespace Lumyte.Audio.Abstractions;

/// <summary>Indicates that an audio device generation cannot perform normal work.</summary>
public sealed class AudioDeviceUnavailableException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="AudioDeviceUnavailableException"/> class.</summary>
    public AudioDeviceUnavailableException()
        : base("The audio device is unavailable; acquire a recovered device generation.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AudioDeviceUnavailableException"/> class with a failure description.</summary>
    /// <param name="message">The unavailable device description.</param>
    public AudioDeviceUnavailableException(string message)
        : base(message)
    {
    }
}
