using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Resources.Audio.Sample;

internal readonly struct DecodeHandler : ICommandHandler<DecodeRequest, AudioPcmData>
{
    internal static int LastThreadId { get; private set; }

    public AudioPcmData Execute(in DecodeRequest command, in WorkContext context)
    {
        context.VerifyAccess();
        LastThreadId = Environment.CurrentManagedThreadId;
        if (command.Bytes.Length != sizeof(float) || !float.IsFinite(BitConverter.ToSingle(command.Bytes)))
        {
            throw new InvalidDataException("Expected one finite PCM sample.");
        }

        float[] samples = new float[48000];
        Array.Fill(samples, BitConverter.ToSingle(command.Bytes));
        return new(new AudioFormat(48000, 1), samples);
    }
}
