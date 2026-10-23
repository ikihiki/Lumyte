using System.Buffers.Binary;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal static class TestWave
{
    internal static byte[] Create(float value)
    {
        byte[] bytes = new byte[44 + (48000 * sizeof(float))];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length - 8);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 48000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 192000);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 32);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 48000 * sizeof(float));
        for (int i = 44; i < bytes.Length; i += sizeof(float))
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i), value);
        }

        return bytes;
    }
}
