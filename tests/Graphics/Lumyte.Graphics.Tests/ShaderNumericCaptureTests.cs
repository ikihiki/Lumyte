using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks append-only logical numeric capture without native wire storage.</summary>
public sealed class ShaderNumericCaptureTests
{
    /// <summary>Checks that twelve numeric leaves no longer require twelve payload arrays.</summary>
    [Fact]
    public void NumericLeavesShareBoundedCpuChunks()
    {
        var value = new NumericArguments(12, 0x7fc12345);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        var arrays = new HashSet<byte[]>();
        foreach (ShaderValue member in snapshot.Values)
        {
            Assert.True(MemoryMarshal.TryGetArray(member.Data, out ArraySegment<byte> segment));
            arrays.Add(segment.Array!);
            Assert.InRange(segment.Array!.Length, 4, 128);
        }

        Assert.Equal(12, snapshot.Values.Count);
        Assert.Equal(2, arrays.Count);
    }

    /// <summary>Checks duplicate paths, chunk rollover and exact historic NaN payloads.</summary>
    [Fact]
    public void DuplicateEmissionsKeepIndependentHistoricalBits()
    {
        var value = new NumericArguments(100, 0x7fc12345);
        ShaderValueSnapshot first = IShaderArguments.Capture(in value);
        ShaderValueSnapshot copy = first.Copy();
        value = new NumericArguments(100, unchecked((int)0x80000000));
        ShaderValueSnapshot later = IShaderArguments.Capture(in value);

        for (int i = 0; i < first.Values.Count; i++)
        {
            Assert.Equal("Repeated", first.Values[i].Path);
            Assert.Equal(typeof(float), first.Values[i].ValueType);
            Assert.Equal(unchecked(0x7fc12345 + i), BinaryPrimitives.ReadInt32LittleEndian(first.Values[i].Data.Span));
            Assert.Equal(first.Values[i].Data.ToArray(), copy.Values[i].Data.ToArray());
            Assert.Equal(unchecked((int)0x80000000 + i), BinaryPrimitives.ReadInt32LittleEndian(later.Values[i].Data.Span));
        }
    }

    /// <summary>Checks one invocation, shared writer aliases and exact opaque identity.</summary>
    [Fact]
    public void HandwrittenCodecRunsOnceAndWriterAliasesAppendSafely()
    {
        var calls = new List<string>();
        var reference = new TestReference();
        var value = new AliasedArguments(calls, reference);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);

        Assert.Equal(new[] { "write" }, calls);
        Assert.Equal(new[] { "First", "Second", "Texture", "Third", "Fourth", "Fifth" }, snapshot.Values.Select(member => member.Path));
        Assert.Equal(11, BinaryPrimitives.ReadInt32LittleEndian(snapshot.Values[0].Data.Span));
        Assert.Equal(22, BinaryPrimitives.ReadInt32LittleEndian(snapshot.Values[1].Data.Span));
        Assert.Same(reference, snapshot.Values[2].Reference);
        Assert.True(snapshot.Values[2].Data.IsEmpty);
        Assert.Equal(33, BinaryPrimitives.ReadInt32LittleEndian(snapshot.Values[3].Data.Span));
        Assert.Equal(44, BinaryPrimitives.ReadInt32LittleEndian(snapshot.Values[4].Data.Span));
        Assert.Equal(55, BinaryPrimitives.ReadInt32LittleEndian(snapshot.Values[5].Data.Span));
        Assert.True(MemoryMarshal.TryGetArray(snapshot.Values[3].Data, out ArraySegment<byte> storage));
        Assert.Equal(22, BinaryPrimitives.ReadInt32LittleEndian(storage.Array!.AsSpan(0)));
        Assert.Equal(33, BinaryPrimitives.ReadInt32LittleEndian(storage.Array.AsSpan(4)));
        Assert.Equal(44, BinaryPrimitives.ReadInt32LittleEndian(storage.Array.AsSpan(8)));
        Assert.Equal(55, BinaryPrimitives.ReadInt32LittleEndian(storage.Array.AsSpan(12)));
        Assert.All(storage.Array!.Skip(16), item => Assert.Equal((byte)0, item));
    }

    /// <summary>Checks that capture still rejects unsupported unmanaged members.</summary>
    [Fact]
    public void UnsupportedNumericTypeStillThrows()
    {
        UnsupportedArguments value = default;
        Assert.Throws<NotSupportedException>(() => IShaderArguments.Capture(in value));
    }

    private readonly record struct NumericArguments(int Count, int Bits) : IShaderArguments
    {
        public static string ShaderTypeName => "NumericArguments";

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            for (int i = 0; i < Count; i++)
            {
                float value = BitConverter.Int32BitsToSingle(unchecked(Bits + i));
                writer.WriteValue("Repeated", value);
            }
        }
    }

    private readonly record struct AliasedArguments(List<string> Calls, TestReference Reference) : IShaderArguments
    {
        public static string ShaderTypeName => "AliasedArguments";

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            Calls.Add("write");
            writer.WriteValue("First", 11);
            writer.WriteValue("Second", 22);
            TWriter alias = writer;
            alias.WriteReference<IGraphicsTextureView>("Texture", Reference);
            alias.WriteValue("Third", 33);
            writer.WriteValue("Fourth", 44);
            alias.WriteValue("Fifth", 55);
        }
    }

    private readonly struct UnsupportedArguments : IShaderArguments
    {
        public static string ShaderTypeName => "UnsupportedArguments";

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => writer.WriteValue("Unsupported", 1L);
    }

    private sealed class TestReference : IGpuRef<IGraphicsTextureView>
    {
        public ulong Count => 1;

        public IGpuRef<IGraphicsTextureView> GetElement(ulong index) => index == 0 ? this : throw new ArgumentOutOfRangeException(nameof(index));
    }
}
