using System.Buffers.Binary;

namespace Lumyte.Resources.Audio.Benchmark;

/// <summary>Deterministic finite WAVE input; no files, network, codec packages or random state.</summary>
public static class SyntheticWave
{
    /// <summary>Creates PCM8/16/24/32 or IEEE float32 with a seven-sample exact binary pattern.</summary>
    /// <param name="encoding">PCM bit width, or F32.</param>
    /// <param name="channels">One or two interleaved channels.</param>
    /// <param name="frames">Positive bounded frame count.</param>
    /// <returns>A complete padded RIFF WAVE file.</returns>
    public static byte[] Create(string encoding, int channels, int frames)
    {
        int bits = encoding == "F32" ? 32 : int.Parse(encoding, System.Globalization.CultureInfo.InvariantCulture);
        int width = bits / 8;
        int length = checked(frames * channels * width);
        byte[] bytes = new byte[44 + length + (length & 1)];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), (short)(encoding == "F32" ? 3 : 1));
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 48000);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 48000 * channels * width);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), (short)(channels * width));
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), (short)bits);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), length);
        for (int i = 0; i < frames * channels; i++)
        {
            Span<byte> target = bytes.AsSpan(44 + (i * width), width);
            int numerator = (i % 7) - 3;
            long integer = (long)numerator * (1L << (bits - 3));
            switch (bits)
            {
                case 8:
                    target[0] = (byte)(128 + integer);
                    break;
                case 16:
                    BinaryPrimitives.WriteInt16LittleEndian(target, (short)integer);
                    break;
                case 24:
                    target[0] = (byte)integer;
                    target[1] = (byte)(integer >> 8);
                    target[2] = (byte)(integer >> 16);
                    break;
                default:
                    if (encoding == "F32")
                    {
                        BinaryPrimitives.WriteSingleLittleEndian(target, ExpectedSample(i));
                    }
                    else
                    {
                        BinaryPrimitives.WriteInt32LittleEndian(target, (int)integer);
                    }

                    break;
            }
        }

        return bytes;
    }

    /// <summary>Independent exact normalized sample oracle.</summary>
    /// <param name="index">Interleaved sample index.</param>
    /// <returns>The sample value.</returns>
    public static float ExpectedSample(int index) => ((index % 7) - 3) / 4f;

    /// <summary>Consumes every decoded sample without additional allocation.</summary>
    /// <param name="samples">Decoded interleaved samples.</param>
    /// <returns>Position-weighted checksum.</returns>
    public static double Checksum(ReadOnlySpan<float> samples)
    {
        double result = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            result += samples[i] * ((i % 13) + 1);
        }

        return result;
    }
}
