namespace Lumyte.Audio.Abstractions;

/// <summary>Describes interleaved mono or stereo PCM.</summary>
/// <param name="SampleRate">Positive sample rate in Hz.</param>
/// <param name="Channels">One or two channels.</param>
public readonly record struct AudioFormat(int SampleRate, int Channels);
