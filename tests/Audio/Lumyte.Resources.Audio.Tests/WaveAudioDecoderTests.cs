using System.Buffers.Binary;
using Xunit;

namespace Lumyte.Resources.Audio.Tests;

/// <summary>Validates public decoding against RIFF fixtures and malformed external input.</summary>
public sealed class WaveAudioDecoderTests
{
    /// <summary>Integer and float encodings produce the expected signed interleaved PCM.</summary>
    /// <param name="bits">The encoded depth.</param>
    /// <param name="codec">The WAVE format tag.</param>
    [Theory]
    [InlineData(8, 1)]
    [InlineData(16, 1)]
    [InlineData(24, 1)]
    [InlineData(32, 1)]
    [InlineData(32, 3)]
    public void DecodesPcmAndFloat(int bits, int codec)
    {
        byte[] bytes = CreateWave(bits, codec);
        AudioPcmData pcm = WaveAudioDecoder.Decode(bytes);
        Assert.Equal(48000, pcm.Format.SampleRate);
        Assert.Equal(2, pcm.Format.Channels);
        Assert.Equal(new float[] { -1, 0, 0.5f, 0 }, pcm.Samples.ToArray());
    }

    /// <summary>Metadata and odd chunk padding do not change decoded samples.</summary>
    [Fact]
    public void SkipsUnknownPaddedChunks()
    {
        byte[] original = CreateWave(16, 1);
        byte[] bytes = new byte[original.Length + 10];
        original.AsSpan(0, 12).CopyTo(bytes);
        "JUNK"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 1);
        bytes[20] = 42;
        original.AsSpan(12).CopyTo(bytes.AsSpan(22));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length - 8);
        Assert.Equal(WaveAudioDecoder.Decode(original).Samples.ToArray(), WaveAudioDecoder.Decode(bytes).Samples.ToArray());
    }

    /// <summary>Truncation cannot expose partial PCM or overflow a chunk offset.</summary>
    /// <param name="length">The supplied file length.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(20)]
    [InlineData(44)]
    public void RejectsTruncatedFiles(int length)
    {
        byte[] bytes = CreateWave(16, 1)[..length];
        Assert.Throws<InvalidDataException>(() => WaveAudioDecoder.Decode(bytes));
    }

    /// <summary>Unsigned external chunk sizes are checked before slicing.</summary>
    [Fact]
    public void RejectsOverflowingChunk()
    {
        byte[] bytes = CreateWave(16, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => WaveAudioDecoder.Decode(bytes));
    }

    /// <summary>Contradictory format metadata is rejected.</summary>
    [Fact]
    public void RejectsInvalidAlignment()
    {
        byte[] bytes = CreateWave(16, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), 0);
        Assert.Throws<InvalidDataException>(() => WaveAudioDecoder.Decode(bytes));
    }

    /// <summary>Compressed formats require a separate decoder rather than being interpreted as PCM.</summary>
    [Fact]
    public void RejectsUnsupportedCodec()
    {
        byte[] bytes = CreateWave(16, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 2);
        Assert.Throws<NotSupportedException>(() => WaveAudioDecoder.Decode(bytes));
    }

    /// <summary>Nonfinite source samples never reach the backend.</summary>
    [Fact]
    public void RejectsNonfiniteSamples()
    {
        byte[] bytes = CreateWave(32, 3);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(44), float.NaN);
        Assert.Throws<InvalidDataException>(() => WaveAudioDecoder.Decode(bytes));
    }

    private static byte[] CreateWave(int bits, int codec)
    {
        int width = bits / 8;
        byte[] bytes = new byte[44 + (width * 4)];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length - 8);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), (ushort)codec);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 48000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)(48000 * 2 * width));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), (ushort)(2 * width));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), (ushort)bits);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), (uint)(4 * width));
        if (bits == 8)
        {
            new byte[] { 0, 128, 192, 128 }.CopyTo(bytes, 44);
        }
        else if (bits == 16)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44), short.MinValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(48), 16384);
        }
        else if (bits == 24)
        {
            bytes[46] = 128;
            bytes[52] = 64;
        }
        else if (codec == 3)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(44), -1);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(52), 0.5f);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(44), int.MinValue);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 1073741824);
        }

        return bytes;
    }
}
