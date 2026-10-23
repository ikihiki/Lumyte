using Lumyte.Execution;

namespace Lumyte.Resources.Audio;

/// <summary>Decodes a complete WAVE buffer as finite CPU work.</summary>
public readonly struct AudioDecodeHandler : ICommandHandler<AudioDecodeRequest, AudioPcmData>
{
    /// <inheritdoc/>
    public AudioPcmData Execute(in AudioDecodeRequest command, in WorkContext context)
    {
        context.VerifyAccess();
        return WaveAudioDecoder.Decode(command.Bytes.Span);
    }
}
