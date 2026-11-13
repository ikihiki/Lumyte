using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks caller-owned shader packing storage without native devices or allocation budgets.</summary>
public sealed class ShaderScratchPackingTests
{
    /// <summary>Numeric bytes and nonzero padding are copied exactly into the caller's sliced range.</summary>
    [Fact]
    public void NumericPackingCopiesEveryByteIntoCallerStorage()
    {
        byte[] source = WireBytes(24);
        var snapshot = new ShaderValueSnapshot("arguments", "NumericWire", source, [], []);
        byte[] original = source.ToArray();
        byte[] outer = Enumerable.Repeat((byte)0xCD, 34).ToArray();
        int calls = 0;

        ShaderDataLayout.PackRawInto(snapshot, outer.AsSpan(5, 24), _ =>
        {
            calls++;
            return new byte[16];
        });

        Assert.Equal(0, calls);
        Assert.Equal(original, outer[5..29]);
        Assert.Equal(original, source);
        Assert.All(outer[..5], value => Assert.Equal((byte)0xCD, value));
        Assert.All(outer[29..], value => Assert.Equal((byte)0xCD, value));
    }

    /// <summary>Opaque reference fixups replace only their slots, preserving numeric bytes, padding and source ownership.</summary>
    [Fact]
    public void ReferencePackingPreservesSnapshotAndOuterGuards()
    {
        byte[] source = WireBytes(24);
        object texture = new();
        var slot = new ShaderValue(typeof(IGraphicsTextureView), texture, 4);
        var snapshot = new ShaderValueSnapshot("arguments", "OpaqueWire", source, [slot], []);
        byte[] original = source.ToArray();
        byte[] encoded = Enumerable.Repeat((byte)0xE7, 16).ToArray();
        byte[] expected = original.ToArray();
        encoded.CopyTo(expected, 4);
        byte[] outer = Enumerable.Repeat((byte)0xCD, 34).ToArray();
        int calls = 0;

        ShaderDataLayout.PackRawInto(snapshot, outer.AsSpan(5, 24), value =>
        {
            calls++;
            Assert.Same(slot, value);
            Assert.Same(texture, value.Reference);
            Assert.Equal(typeof(IGraphicsTextureView), value.ValueType);
            return encoded;
        });

        Assert.Equal(1, calls);
        Assert.Equal(expected, outer[5..29]);
        Assert.Equal(original, source);
        Assert.Equal(original, snapshot.Data.ToArray());
        Assert.Same(slot, Assert.Single(snapshot.Values));
        Assert.All(outer[..5], value => Assert.Equal((byte)0xCD, value));
        Assert.All(outer[29..], value => Assert.Equal((byte)0xCD, value));
        outer[9] = 0;
        Assert.Equal((byte)0xE7, encoded[0]);
        Assert.Equal(original, source);
    }

    /// <summary>Each original reference is encoded once in snapshot order rather than sorted wire-offset order.</summary>
    [Fact]
    public void ReferenceEncoderReceivesOriginalSlotsOnceInDeclaredOrder()
    {
        byte[] source = WireBytes(40);
        var first = new ShaderValue(typeof(IGraphicsTextureView), new object(), 20);
        var second = new ShaderValue(typeof(IGraphicsTextureView), new object(), 4);
        var snapshot = new ShaderValueSnapshot("arguments", "TwoReferences", source, [first, second], []);
        var seen = new List<ShaderValue>();
        byte[] destination = new byte[40];

        ShaderDataLayout.PackRawInto(snapshot, destination, value =>
        {
            seen.Add(value);
            return Enumerable.Repeat((byte)seen.Count, 16).ToArray();
        });

        Assert.Collection(seen, value => Assert.Same(first, value), value => Assert.Same(second, value));
        Assert.Same(first.Reference, seen[0].Reference);
        Assert.Same(second.Reference, seen[1].Reference);
        Assert.All(destination[20..36], value => Assert.Equal((byte)1, value));
        Assert.All(destination[4..20], value => Assert.Equal((byte)2, value));
        Assert.Equal(source[..4], destination[..4]);
        Assert.Equal(source[36..], destination[36..]);
        Assert.Equal(WireBytes(40), source);
    }

    /// <summary>Both undersized and oversized destinations fail before encoding or writing any bytes.</summary>
    /// <param name="length">The invalid destination length relative to the 24-byte snapshot.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(25)]
    public void InvalidDestinationSizeDoesNotWriteOrEncode(int length)
    {
        byte[] source = WireBytes(24);
        ShaderValueSnapshot snapshot = TextureSnapshot(source);
        byte[] destination = Enumerable.Repeat((byte)0xCD, length).ToArray();
        byte[] before = destination.ToArray();
        int calls = 0;

        Assert.Throws<ArgumentException>(() => ShaderDataLayout.PackRawInto(snapshot, destination, _ =>
        {
            calls++;
            return new byte[16];
        }));

        Assert.Equal(0, calls);
        Assert.Equal(before, destination);
        Assert.Equal(WireBytes(24), source);
    }

    /// <summary>Exact and partial source aliases are rejected before the snapshot or surrounding storage changes.</summary>
    /// <param name="destinationStart">The start of an exact-size destination overlapping the source at offset eight.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(16)]
    public void AliasedDestinationDoesNotWriteOrEncode(int destinationStart)
    {
        byte[] backing = WireBytes(48);
        ShaderValueSnapshot snapshot = TextureSnapshot(backing.AsMemory(8, 24));
        byte[] before = backing.ToArray();
        int calls = 0;

        Assert.Throws<ArgumentException>(() => ShaderDataLayout.PackRawInto(snapshot, backing.AsSpan(destinationStart, 24), _ =>
        {
            calls++;
            return new byte[16];
        }));

        Assert.Equal(0, calls);
        Assert.Equal(before, backing);
        Assert.Equal(before[8..32], snapshot.Data.ToArray());
    }

    /// <summary>All host-unsafe slots are validated before even an earlier valid reference can be encoded or copied.</summary>
    /// <param name="invalidOffset">A negative, duplicate, overflowing or out-of-range reference offset.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(24)]
    [InlineData(int.MaxValue)]
    public void MalformedReferenceSlotsDoNotWriteOrEncode(int invalidOffset)
    {
        byte[] source = WireBytes(24);
        ShaderValueSnapshot snapshot = TextureSnapshot(source);
        snapshot = snapshot with { Values = [snapshot.Values[0], new(typeof(IGraphicsTextureView), new object(), invalidOffset)] };
        byte[] destination = Enumerable.Repeat((byte)0xCD, 24).ToArray();
        byte[] before = destination.ToArray();
        int calls = 0;

        Assert.Throws<ArgumentException>(() => ShaderDataLayout.PackRawInto(snapshot, destination, _ =>
        {
            calls++;
            return new byte[16];
        }));

        Assert.Equal(0, calls);
        Assert.Equal(before, destination);
        Assert.Equal(WireBytes(24), source);
    }

    /// <summary>Non-ABI encodings fail without modifying the retained snapshot; scratch contents need not roll back.</summary>
    /// <param name="encodedLength">A reference encoding length other than the required sixteen bytes.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void InvalidReferenceEncodingLeavesSnapshotUnchanged(int encodedLength)
    {
        byte[] source = WireBytes(24);
        ShaderValueSnapshot snapshot = TextureSnapshot(source);
        ShaderValue slot = snapshot.Values[0];
        byte[] outer = Enumerable.Repeat((byte)0xCD, 34).ToArray();
        int calls = 0;

        Assert.Throws<ArgumentException>(() => ShaderDataLayout.PackRawInto(snapshot, outer.AsSpan(5, 24), value =>
        {
            calls++;
            Assert.Same(slot, value);
            return new byte[encodedLength];
        }));

        Assert.Equal(1, calls);
        Assert.Equal(WireBytes(24), source);
        Assert.Equal(WireBytes(24), snapshot.Data.ToArray());
        Assert.Same(slot, Assert.Single(snapshot.Values));
        Assert.All(outer[..5], value => Assert.Equal((byte)0xCD, value));
        Assert.All(outer[29..], value => Assert.Equal((byte)0xCD, value));
    }

    /// <summary>The existing array API still returns independently mutable packs with the same wire fixups.</summary>
    [Fact]
    public void ArrayPackingReturnsSeparateMutableResultsMatchingScratch()
    {
        byte[] source = WireBytes(24);
        ShaderValueSnapshot snapshot = TextureSnapshot(source);
        byte[] encoded = Enumerable.Repeat((byte)0xE7, 16).ToArray();
        byte[] expected = source.ToArray();
        encoded.CopyTo(expected, 4);
        byte[] first = ShaderDataLayout.PackRaw(snapshot, _ => encoded);
        byte[] second = ShaderDataLayout.PackRaw(snapshot, _ => encoded);
        byte[] scratch = new byte[24];
        ShaderDataLayout.PackRawInto(snapshot, scratch, _ => encoded);

        Assert.NotSame(source, first);
        Assert.NotSame(first, second);
        Assert.Equal(expected, first);
        Assert.Equal(expected, second);
        Assert.Equal(expected, scratch);
        first[0] ^= 0xFF;
        first[4] ^= 0xFF;
        Assert.NotEqual(expected[0], first[0]);
        Assert.NotEqual(expected[4], first[4]);
        Assert.Equal(expected, second);
        Assert.Equal(expected, scratch);
        Assert.Equal(WireBytes(24), snapshot.Data.ToArray());
        Assert.All(encoded, value => Assert.Equal((byte)0xE7, value));
    }

    private static ShaderValueSnapshot TextureSnapshot(ReadOnlyMemory<byte> source)
        => new("arguments", "OpaqueWire", source, [new(typeof(IGraphicsTextureView), new object(), 4)], []);

    private static byte[] WireBytes(int length)
        => Enumerable.Range(0, length).Select(index => (byte)(index + 1)).ToArray();
}
