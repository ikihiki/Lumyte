namespace Lumyte.Resources.Audio;

/// <summary>Retains an encoded WAVE buffer until its CPU decode completes.</summary>
/// <param name="Bytes">The immutable encoded bytes.</param>
public readonly record struct AudioDecodeRequest(ReadOnlyMemory<byte> Bytes);
