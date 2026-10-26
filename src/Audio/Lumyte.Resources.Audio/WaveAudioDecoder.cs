using System.Buffers.Binary;

namespace Lumyte.Resources.Audio;

/// <summary>Decodes mono or stereo RIFF WAVE PCM (8/16/24/32 bit) and IEEE float (32 bit).</summary>
public static class WaveAudioDecoder
{
    /// <summary>Decodes a complete little-endian RIFF WAVE file into owned interleaved samples.</summary>
    /// <param name="bytes">The encoded file.</param>
    /// <returns>Completed PCM suitable for an audio clip.</returns>
    /// <exception cref="InvalidDataException">The header, chunk bounds or samples are invalid.</exception>
    /// <exception cref="NotSupportedException">The codec, bit depth or channel layout is unsupported.</exception>
    public static AudioPcmData Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Expected a RIFF WAVE file.");
        }

        long end = 8L + BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4));
        if (end < 12 || end > bytes.Length)
        {
            throw new InvalidDataException("The RIFF length exceeds the supplied file.");
        }

        ReadOnlySpan<byte> format = default;
        ReadOnlySpan<byte> data = default;
        bool hasData = false;
        for (long offset = 12; offset < end;)
        {
            if (end - offset < 8)
            {
                throw new InvalidDataException("The WAVE chunk header is truncated.");
            }

            int start = checked((int)offset);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(start + 4, 4));
            long next = offset + 8 + length + (length & 1);
            if (next > end)
            {
                throw new InvalidDataException("The WAVE chunk or its padding is truncated.");
            }

            ReadOnlySpan<byte> id = bytes.Slice(start, 4);
            if (id.SequenceEqual("fmt "u8))
            {
                if (!format.IsEmpty)
                {
                    throw new InvalidDataException("Duplicate WAVE format chunk.");
                }

                format = bytes.Slice(start + 8, checked((int)length));
                if (format.Length < 16)
                {
                    throw new InvalidDataException("The WAVE format chunk is truncated.");
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (hasData)
                {
                    throw new InvalidDataException("Duplicate WAVE data chunk.");
                }

                data = bytes.Slice(start + 8, checked((int)length));
                hasData = true;
            }

            offset = next;
        }

        if (format.IsEmpty || !hasData || data.IsEmpty)
        {
            throw new InvalidDataException("WAVE requires format and nonempty data chunks.");
        }

        ushort codec = BinaryPrimitives.ReadUInt16LittleEndian(format);
        ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
        uint byteRate = BinaryPrimitives.ReadUInt32LittleEndian(format[8..]);
        ushort alignment = BinaryPrimitives.ReadUInt16LittleEndian(format[12..]);
        ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
        if (channels is not (1 or 2) || (codec == 1 ? bits is not (8 or 16 or 24 or 32) : codec != 3 || bits != 32))
        {
            throw new NotSupportedException("Supported WAVE formats are mono/stereo PCM8/16/24/32 and float32.");
        }

        int width = bits / 8;
        if (rate == 0 || rate > int.MaxValue || alignment != channels * width || byteRate != (ulong)rate * alignment || data.Length % alignment != 0)
        {
            throw new InvalidDataException("The WAVE rate, alignment or data length is invalid.");
        }

        float[] samples = new float[data.Length / width];
        for (int i = 0; i < samples.Length; i++)
        {
            ReadOnlySpan<byte> sample = data.Slice(i * width, width);
            float value = bits switch
            {
                8 => (sample[0] - 128) / 128f,
                16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
                24 => ((sample[0] | (sample[1] << 8) | (sample[2] << 16)) << 8 >> 8) / 8388608f,
                _ when codec == 3 => BinaryPrimitives.ReadSingleLittleEndian(sample),
                _ => (float)(BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648.0),
            };
            if (!float.IsFinite(value))
            {
                throw new InvalidDataException("WAVE contains a nonfinite sample.");
            }

            samples[i] = value;
        }

        return new(new AudioFormat((int)rate, channels), samples);
    }
}
