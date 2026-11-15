using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks consecutive raw capture and independent historical wire bytes.</summary>
public sealed class ShaderNumericCaptureTests
{
    /// <summary>Numeric ranges share the one exact-sized snapshot allocation.</summary>
    [Fact]
    public void NumericLeavesShareBoundedCpuChunks()
    {
        var value = new NumericArguments(12, 0x7fc12345);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        Assert.True(MemoryMarshal.TryGetArray(snapshot.Data, out ArraySegment<byte> segment));
        Assert.Equal(400, segment.Array!.Length);
        Assert.Equal(400, snapshot.Data.Length);
        Assert.Empty(snapshot.Values);
        for (int i = 0; i < 12; i++)
        {
            Assert.Equal(unchecked(0x7fc12345 + i), BinaryPrimitives.ReadInt32LittleEndian(snapshot.Data.Span[(i * 4)..]));
        }

        Assert.All(snapshot.Data.ToArray().Skip(48), item => Assert.Equal((byte)0, item));
    }

    /// <summary>Repeated raw ranges and copies preserve every historical NaN payload.</summary>
    [Fact]
    public void DuplicateEmissionsKeepIndependentHistoricalBits()
    {
        var value = new NumericArguments(100, 0x7fc12345);
        ShaderValueSnapshot first = IShaderArguments.Capture(in value);
        ShaderValueSnapshot copy = first.Copy();
        value = new NumericArguments(100, unchecked((int)0x80000000));
        ShaderValueSnapshot later = IShaderArguments.Capture(in value);
        Assert.Equal(400, first.Data.Length);
        Assert.Empty(first.Values);
        Assert.Equal(first.Data.ToArray(), copy.Data.ToArray());
        Assert.True(MemoryMarshal.TryGetArray(first.Data, out ArraySegment<byte> original));
        Assert.True(MemoryMarshal.TryGetArray(copy.Data, out ArraySegment<byte> copied));
        Assert.NotSame(original.Array, copied.Array);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(unchecked(0x7fc12345 + i), BinaryPrimitives.ReadInt32LittleEndian(first.Data.Span[(i * 4)..]));
            Assert.Equal(unchecked((int)0x80000000 + i), BinaryPrimitives.ReadInt32LittleEndian(later.Data.Span[(i * 4)..]));
        }
    }

    /// <summary>A handwritten codec runs once and shared helper aliases append in physical order.</summary>
    [Fact]
    public void HandwrittenCodecRunsOnceAndWriterAliasesAppendSafely()
    {
        var calls = new List<string>();
        var reference = new TestReference();
        var value = new AliasedArguments(calls, reference);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        Assert.Equal(new[] { "write" }, calls);
        ShaderValue fixup = Assert.Single(snapshot.Values);
        Assert.Same(reference, fixup.Reference);
        Assert.Equal(typeof(IGraphicsTextureView), fixup.ValueType);
        Assert.Equal(8, fixup.Offset);
        Assert.All(snapshot.Data.ToArray().Skip(8).Take(16), item => Assert.Equal((byte)0, item));
        Assert.Equal(11, BinaryPrimitives.ReadInt32LittleEndian(snapshot.Data.Span));
        Assert.Equal(22, BinaryPrimitives.ReadInt32LittleEndian(snapshot.Data.Span[4..]));
        Assert.Equal(33, BinaryPrimitives.ReadInt32LittleEndian(snapshot.Data.Span[24..]));
        Assert.Equal(44, BinaryPrimitives.ReadInt32LittleEndian(snapshot.Data.Span[28..]));
        Assert.Equal(55, BinaryPrimitives.ReadInt32LittleEndian(snapshot.Data.Span[32..]));
        Assert.Equal(36, snapshot.Data.Length);
    }

    /// <summary>Raw long bytes remain transferable but incompatible uint shader fields fail diagnostics.</summary>
    [Fact]
    public void UnsupportedNumericTypeStillThrows()
    {
        UnsupportedArguments value = default;
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        Assert.Equal(1L, BinaryPrimitives.ReadInt64LittleEndian(snapshot.Data.Span));
        Assert.Empty(snapshot.Values);
        const string Reflection = """
            {"parameters":[{"name":"arguments","type":{"kind":"constantBuffer","elementType":{"kind":"struct","name":"UnsupportedArguments","sizes":[{"kind":"uniform","value":8,"alignment":4}],"fields":[{"name":"Unsupported","type":{"kind":"scalar","scalarType":"uint32"},"binding":{"offset":0,"size":4}}]}}}]}
            """;
        var target = new ShaderTargetData(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", "row-major", [32], Reflection);
        Assert.Throws<ArgumentException>(() => ShaderDataLayout.Root(target, "arguments").ValidateArguments(snapshot, true));
    }

    private static void WriteNumber<TWriter>(ref TWriter writer, int value)
        where TWriter : IShaderValueWriter, allows ref struct
        => writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));

    private readonly record struct NumericArguments(int Count, int Bits) : IShaderArguments
    {
        public static string ShaderTypeName => "NumericArguments";

        public static int TransferSize => 400;

        public static int ReferenceCount => 0;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            for (int i = 0; i < Count; i++)
            {
                float value = BitConverter.Int32BitsToSingle(unchecked(Bits + i));
                writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
            }

            writer.WriteValue(new byte[TransferSize - (Count * 4)]);
        }
    }

    private readonly record struct AliasedArguments(List<string> Calls, TestReference Reference) : IShaderArguments
    {
        public static string ShaderTypeName => "AliasedArguments";

        public static int TransferSize => 36;

        public static int ReferenceCount => 1;

        public static IReadOnlyList<ShaderTransferField> TransferFields =>
            [new("First", 0, 4, typeof(int)), new("Second", 4, 4, typeof(int)), new("Texture", 8, 16, typeof(ShaderReferenceSlot), true), new("Third", 24, 4, typeof(int)), new("Fourth", 28, 4, typeof(int)), new("Fifth", 32, 4, typeof(int))];

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            Calls.Add("write");
            WriteNumber(ref writer, 11);
            WriteNumber(ref writer, 22);
            Append(ref writer, Reference);
            WriteNumber(ref writer, 44);
            WriteNumber(ref writer, 55);
        }

        private static void Append<TWriter>(ref TWriter alias, TestReference reference)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            alias.WriteReference<IGraphicsTextureView>(reference);
            WriteNumber(ref alias, 33);
        }
    }

    private readonly struct UnsupportedArguments : IShaderArguments
    {
        public static string ShaderTypeName => "UnsupportedArguments";

        public static int TransferSize => 8;

        public static int ReferenceCount => 0;

        public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Unsupported", 0, 8, typeof(long))];

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            long value = 1;
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
        }
    }

    private sealed class TestReference : IGpuRef<IGraphicsTextureView>
    {
        public ulong Count => 1;

        public IGpuRef<IGraphicsTextureView> GetElement(ulong index) => index == 0 ? this : throw new ArgumentOutOfRangeException(nameof(index));
    }
}
