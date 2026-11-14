using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Deterministic wire-order, compatibility and ownership regressions.</summary>
public sealed class ShaderFlatWriterTests
{
    /// <summary>Generated writers write raw gaps and encoded slots directly into final storage.</summary>
    [Fact]
    public void GeneratedUploadInterleavesPhysicalSlotsAndPreservesEveryPaddingByte()
    {
        var a = new Reference(0xA1);
        var b = new Reference(0xB2);
        var value = new WireCursorValue(0x31) { A = a, B = b };
        byte[] original = value.RawBytes();
        var events = new List<string>();
        using var buffer = new MappedBuffer(64, events);
        var metadata = new IShaderReferenceMetadata?[1];
        int factories = 0;

        byte[] EncodeSlot(ShaderValue slot)
        {
            events.Add($"E:{slot.Offset}");
            return Encode(slot);
        }

        IShaderReferenceMetadata CreateMetadata(IEnumerable<IShaderReference> references)
        {
            factories++;
            return new IdentityMetadata(references);
        }

        TestShaderDataUpload.CopyFrom<WireCursorValue>([value], buffer, 0, metadata, EncodeSlot, null, CreateMetadata);

        byte[] expected = original.ToArray();
        Array.Fill(expected, (byte)0xA1, 8, 16);
        Array.Fill(expected, (byte)0xB2, 32, 16);
        Assert.Equal(expected, buffer.Bytes);
        string[] expectedEvents =
        [
            Copy(0, original[..8]), "E:8", Copy(8, expected[8..24]),
            Copy(24, original[24..32]), "E:32", Copy(32, expected[32..48]),
            Copy(48, original[48..]),
        ];
        Assert.Equal(expectedEvents, events);
        Assert.Equal(5, buffer.WriteCount);
        Assert.Equal(1, factories);
        Assert.Equal(new IShaderReference[] { a, b }, Assert.IsType<IdentityMetadata>(metadata[0]).References);
        Assert.Equal(original, value.RawBytes());

        // Readonly capture also uses physical sequential order and zeroes reference slots.
        ShaderValueSnapshot captured = IShaderArguments.Capture(in value);
        byte[] diagnostic = original.ToArray();
        Array.Clear(diagnostic, 8, 16);
        Array.Clear(diagnostic, 32, 16);
        Assert.Equal(diagnostic, captured.Data.ToArray());
        Assert.Equal(new[] { 8, 32 }, captured.Values.Select(slot => slot.Offset));
        Assert.Same(a, captured.Values[0].Reference);
        Assert.Same(b, captured.Values[1].Reference);
        Assert.Empty(captured.Fields);
    }

    /// <summary>A custom reference getter retains bulk-copy-before-getter semantics.</summary>
    [Fact]
    public void GeneratedCustomGetterKeepsOldNumericBytes()
    {
        var value = new WireCursorGetterValue { Number = 17, Reference = new Reference(3) };
        var events = new List<string>();
        using var buffer = new MappedBuffer(32, events);

        byte[] EncodeSlot(ShaderValue slot)
        {
            events.Add($"E:{slot.Offset}");
            return Encode(slot);
        }

        TestShaderDataUpload.CopyFrom<WireCursorGetterValue>([value], buffer, 0, new IShaderReferenceMetadata?[1], EncodeSlot, null);
        Assert.Equal(17U, MemoryMarshal.Read<uint>(buffer.Bytes));
        Assert.Equal(17U, value.Number);
        Assert.Equal(1, value.GetterCalls);
        Assert.Equal(3, buffer.WriteCount);
        Assert.StartsWith("W:0:8:", events[0]);
        Assert.Equal("E:8", events[1]);
    }

    /// <summary>The first generated call orders custom getters physically, once, over a frozen numeric wire.</summary>
    [Fact]
    public void GeneratedCustomGettersUsePhysicalOrderExactlyOnce()
    {
        var getters = new List<string>();
        var value = new WireCursorOrderedGetterValue(new Reference(1), new Reference(2), getters) { Number = 17 };
        using var buffer = new MappedBuffer(64, []);
        var offsets = new List<int>();
        byte[] EncodeSlot(ShaderValue slot)
        {
            offsets.Add(slot.Offset);
            return Encode(slot);
        }

        TestShaderDataUpload.CopyFrom<WireCursorOrderedGetterValue>([value], buffer, 0, new IShaderReferenceMetadata?[1], EncodeSlot, null);
        Assert.Equal(new[] { "A", "B" }, getters);
        Assert.Equal(new[] { 8, 32 }, offsets);
        Assert.Equal(17u, MemoryMarshal.Read<uint>(buffer.Bytes));
        Assert.Equal(17u, value.Number);
        Assert.Equal(5, buffer.WriteCount);
    }

    /// <summary>Handwritten codecs invoke the mandatory writer directly in physical reference order.</summary>
    [Fact]
    public void HandwrittenWriterPreservesEmissionOrder()
    {
        var events = new List<string>();
        var value = new CursorValue(events);
        using var buffer = new MappedBuffer(64, events);
        var metadata = new IShaderReferenceMetadata?[1];

        byte[] EncodeSlot(ShaderValue slot)
        {
            events.Add($"E:{slot.Offset}");
            return Encode(slot);
        }

        TestShaderDataUpload.CopyFrom<CursorValue>([value], buffer, 0, metadata, EncodeSlot, null);
        Assert.Equal(1, value.Writes);
        Assert.Equal(0, value.DiagnosticCalls);
        Assert.Equal(new[] { "G:8", "E:8", "G:32", "E:32" }, events.Where(item => !item.StartsWith("W:")));
        Assert.Equal(5, buffer.WriteCount);
        Assert.Equal(17U, MemoryMarshal.Read<uint>(buffer.Bytes));
        Assert.Equal(new uint[] { 1, 2 }, metadata[0]!.References.Select(reference => reference.Slot));
    }

    /// <summary>Capacity and incomplete totals fail without metadata publication or unsafe writes.</summary>
    /// <param name="prefix">Raw bytes preceding reference writes.</param>
    /// <param name="references">The number of sequential reference values.</param>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(17, 1)]
    [InlineData(33, 0)]
    [InlineData(0, 1)]
    public void HandwrittenWriterRejectsIncorrectTotals(int prefix, int references)
    {
        var value = new CursorSlotValue(prefix, references);
        var unvisited = new CursorSlotValue(0, 2);
        var old = new IdentityMetadata([]);
        IShaderReferenceMetadata?[] metadata = [old, old, old, old];
        using var buffer = new MappedBuffer(128, []);
        Exception? failure = Record.Exception(() => TestShaderDataUpload.CopyFrom<CursorSlotValue>([value, unvisited], buffer, 1, metadata, Encode, null));
        if (prefix == 0)
        {
            Assert.IsType<InvalidOperationException>(failure);
        }
        else
        {
            Assert.IsType<ArgumentOutOfRangeException>(failure);
        }

        Assert.Equal(1, value.Writes);
        Assert.Equal(0, value.DiagnosticCalls);
        Assert.Equal(0, unvisited.Writes);
        Assert.Null(metadata[1]);
        Assert.Null(metadata[2]);
        Assert.Same(old, metadata[0]);
        Assert.Same(old, metadata[3]);
        Assert.All(buffer.Bytes[..32], item => Assert.Equal((byte)0xCD, item));
        Assert.All(buffer.Bytes[64..], item => Assert.Equal((byte)0xCD, item));
    }

    /// <summary>Each reference advances the implicit position by exactly sixteen bytes.</summary>
    [Fact]
    public void HandwrittenWriterAcceptsAdjacentSlots()
    {
        var value = new CursorSlotValue(0, 2);
        using var buffer = new MappedBuffer(32, []);
        var metadata = new IShaderReferenceMetadata?[1];
        TestShaderDataUpload.CopyFrom<CursorSlotValue>([value], buffer, 0, metadata, Encode, null);
        Assert.Equal(1, value.Writes);
        Assert.Equal(0, value.DiagnosticCalls);
        Assert.Equal(2, buffer.WriteCount);
        Assert.All(buffer.Bytes[..16], item => Assert.Equal((byte)1, item));
        Assert.All(buffer.Bytes[16..], item => Assert.Equal((byte)2, item));
        Assert.Equal(new uint[] { 1, 2 }, metadata[0]!.References.Select(reference => reference.Slot));
    }

    /// <summary>Malformed generated layouts repeatedly fail before getters, encoding or destination writes.</summary>
    [Fact]
    public void GeneratedCustomGetterRejectsOverlappingReferenceSlots()
    {
        var value = new WireCursorOverlapGetterValue(new Reference(1), new Reference(2));
        var old = new IdentityMetadata([]);
        IShaderReferenceMetadata?[] metadata = [old, old, old, old];
        using var buffer = new MappedBuffer(128, []);
        int encodes = 0;

        byte[] EncodeSlot(ShaderValue slot)
        {
            encodes++;
            return Encode(slot);
        }

        ArgumentException first = Assert.Throws<ArgumentException>(() => TestShaderDataUpload.CopyFrom<WireCursorOverlapGetterValue>([value, value], buffer, 1, metadata, EncodeSlot, null));
        ArgumentException repeated = Assert.Throws<ArgumentException>(() => TestShaderDataUpload.CopyFrom<WireCursorOverlapGetterValue>([value, value], buffer, 1, metadata, EncodeSlot, null));
        ArgumentException captured = Assert.Throws<ArgumentException>(() => IShaderArguments.Capture(in value));
        ArgumentException capturedAgain = Assert.Throws<ArgumentException>(() => IShaderArguments.Capture(in value));
        Assert.Equal(first.Message, repeated.Message);
        Assert.Equal(first.Message, captured.Message);
        Assert.Equal(first.Message, capturedAgain.Message);

        Assert.Equal(0, value.AGetterCalls);
        Assert.Equal(0, value.BGetterCalls);
        Assert.Equal(0, encodes);
        Assert.Equal(0, buffer.WriteCount);
        Assert.All(buffer.Bytes, item => Assert.Equal((byte)0xCD, item));
        Assert.Null(metadata[1]);
        Assert.Null(metadata[2]);
        Assert.Same(old, metadata[0]);
        Assert.Same(old, metadata[3]);
        Assert.All(buffer.Bytes[..32], item => Assert.Equal((byte)0xCD, item));
        Assert.All(buffer.Bytes[64..], item => Assert.Equal((byte)0xCD, item));
    }

    /// <summary>Overlapping generated reference slots cannot publish a replacement root.</summary>
    [Fact]
    public void GeneratedCustomGetterOverlapLeavesSelectedRootUntouched()
    {
        var valid = new WireCursorValue(1) { A = new Reference(1), B = new Reference(2) };
        var invalid = new WireCursorOverlapGetterValue(new Reference(3), new Reference(4));
        var previousStorage = new RootStorage(64);
        TestRootOwner selected = TestShaderTransfer.Create(in valid, _ => previousStorage, encode: Encode);
        using TestRootOwner previous = selected;
        byte[] original = previousStorage.Bytes.ToArray();
        IShaderReferenceMetadata originalMetadata = previous.References;
        var candidate = new RootStorage(32);
        int encodes = 0;

        byte[] EncodeSlot(ShaderValue slot)
        {
            encodes++;
            return Encode(slot);
        }

        Assert.Throws<ArgumentException>(() => selected = TestShaderTransfer.Create(in invalid, _ => candidate, encode: EncodeSlot));

        Assert.Equal(0, invalid.AGetterCalls);
        Assert.Equal(0, invalid.BGetterCalls);
        Assert.Equal(0, encodes);
        Assert.Same(previous, selected);
        Assert.Same(originalMetadata, selected.References);
        Assert.Equal(original, previousStorage.Bytes);
        Assert.False(previousStorage.Disposed);
        Assert.True(candidate.Disposed);
        Assert.Equal(0, candidate.WriteCount);
        Assert.All(candidate.Bytes, item => Assert.Equal((byte)0, item));
    }

    /// <summary>A failed generated upload invalidates only its requested metadata range.</summary>
    [Fact]
    public void GeneratedFailureAllowsPartialBytesButPreservesOutsideMetadata()
    {
        var old = new IdentityMetadata([]);
        IShaderReferenceMetadata?[] metadata = [old, old, old, old];
        using var buffer = new MappedBuffer(256, []);
        var value = new WireCursorValue(1) { A = new Reference(1), B = new Reference(2) };
        var failure = new InvalidOperationException("encoding failed");
        int calls = 0;

        byte[] EncodeSlot(ShaderValue slot)
        {
            if (++calls == 3)
            {
                throw failure;
            }

            return Encode(slot);
        }

        Assert.Same(failure, Record.Exception(() => TestShaderDataUpload.CopyFrom<WireCursorValue>([value, value], buffer, 1, metadata, EncodeSlot, null)));
        Assert.Equal(3, calls);
        Assert.Null(metadata[1]);
        Assert.Null(metadata[2]);
        Assert.Same(old, metadata[0]);
        Assert.Same(old, metadata[3]);
        Assert.All(buffer.Bytes[..64], item => Assert.Equal((byte)0xCD, item));
        Assert.All(buffer.Bytes[192..], item => Assert.Equal((byte)0xCD, item));
        Assert.Equal(6, buffer.WriteCount); // Complete first element, then the second element's raw prefix.
        Assert.Equal(value.RawBytes()[..8], buffer.Bytes[128..136]);
    }

    /// <summary>Generated inline references still require exactly sixteen encoded bytes.</summary>
    /// <param name="size">The invalid encoded slot size.</param>
    [Theory]
    [InlineData(15)]
    [InlineData(17)]
    public void GeneratedCursorRequiresMandatorySlotAbi(int size)
    {
        var value = new WireCursorValue(1) { A = new Reference(1), B = new Reference(2) };
        using var buffer = new MappedBuffer(64, []);
        IShaderReferenceMetadata?[] metadata = [new IdentityMetadata([])];
        Assert.Throws<ArgumentException>(() => TestShaderDataUpload.CopyFrom<WireCursorValue>([value], buffer, 0, metadata, _ => new byte[size], null));
        Assert.Null(metadata[0]);
        Assert.Equal(1, buffer.WriteCount); // Raw prefix only; invalid encoded bytes never reach storage.
    }

    /// <summary>Invalid host ranges preserve old metadata and never start generated writes.</summary>
    [Fact]
    public void InvalidGeneratedUploadRangePreservesExistingState()
    {
        var old = new IdentityMetadata([]);
        IShaderReferenceMetadata?[] metadata = [old];
        using var buffer = new MappedBuffer(64, []);
        var value = new WireCursorValue(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => TestShaderDataUpload.CopyFrom<WireCursorValue>([value], buffer, ulong.MaxValue, metadata, _ => throw new InvalidOperationException("Must not encode."), null));
        Assert.Same(old, metadata[0]);
        Assert.Equal(0, buffer.WriteCount);
        Assert.All(buffer.Bytes, item => Assert.Equal((byte)0xCD, item));
    }

    /// <summary>A failed root replacement disposes only its unpublished allocation.</summary>
    [Fact]
    public void FailedGeneratedRootLeavesPreviousGenerationUntouched()
    {
        var value = new WireCursorValue(1) { A = new Reference(1), B = new Reference(2) };
        var previousStorage = new RootStorage(64);
        TestRootOwner selected = TestShaderTransfer.Create(in value, _ => previousStorage, encode: Encode);
        using TestRootOwner previous = selected;
        byte[] original = previousStorage.Bytes.ToArray();
        IShaderReferenceMetadata originalMetadata = previous.References;
        var candidate = new RootStorage(64);
        var failure = new InvalidOperationException("Root encoder failed.");
        int calls = 0;

        byte[] EncodeSlot(ShaderValue slot)
        {
            if (++calls == 2)
            {
                throw failure;
            }

            return Encode(slot);
        }

        Assert.Same(failure, Record.Exception(() => selected = TestShaderTransfer.Create(in value, _ => candidate, encode: EncodeSlot)));
        Assert.Same(previous, selected);
        Assert.Same(originalMetadata, selected.References);
        Assert.Equal(original, previousStorage.Bytes);
        Assert.False(previousStorage.Disposed);
        Assert.True(candidate.Disposed);
        Assert.Equal(2, calls);
        Assert.Equal(3, candidate.WriteCount); // Prefix, first reference and the gap before the failed second encode.
        Assert.Equal(value.RawBytes()[..8], candidate.Bytes[..8]);
    }

    /// <summary>The public metadata contract carries identities, not transfer positions or numeric trees.</summary>
    [Fact]
    public void MetadataContractAndNativeOwnersRetainOnlyIdentityLists()
    {
        PropertyInfo property = Assert.Single(typeof(IShaderReferenceMetadata).GetProperties());
        Assert.Equal(typeof(IReadOnlyList<IShaderReference>), property.PropertyType);
        Assert.Equal("References", property.Name);
        foreach (string backend in new[] { "Vulkan", "Wgpu" })
        {
            var assembly = Assembly.Load($"Lumyte.Graphics.{backend}");
            Type owner = assembly.GetType($"Lumyte.Graphics.{backend}.{backend}ReferenceMetadata", throwOnError: true)!;
            Assert.False(owner.IsPublic);
            FieldInfo field = Assert.Single(owner.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
            Assert.Equal(typeof(IReadOnlyList<IShaderReference>), field.FieldType);
            Assert.DoesNotContain(owner.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), item => item.FieldType == typeof(ShaderValue[]));
        }
    }

    private static byte[] Encode(ShaderValue value) => Enumerable.Repeat((byte)Assert.IsType<Reference>(value.Reference).Slot, 16).ToArray();

    private static string Copy(int offset, byte[] bytes) => $"W:{offset}:{bytes.Length}:{Convert.ToHexString(bytes)}";

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct CursorWire
    {
        [FieldOffset(0)]
        public uint Number;
    }

    private struct CursorValue(List<string> events) : IShaderArguments
    {
        private readonly System.Runtime.CompilerServices.StrongBox<int> _counter = new();

        public static string ShaderTypeName => throw new InvalidOperationException("No type diagnostics.");

        public static int TransferSize => 64;

        public static IReadOnlyList<ShaderTransferField> TransferFields => throw new InvalidOperationException("No field diagnostics.");

        public int Writes => _counter.Value;

        public int DiagnosticCalls { get; private set; }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            _counter.Value++;
            var wire = new CursorWire { Number = 17 };
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1));
            writer.WriteValue(bytes[..8]);
            events.Add("G:8");
            writer.WriteReference<uint>(new Reference(1));
            writer.WriteValue(bytes[24..32]);
            events.Add("G:32");
            writer.WriteReference<uint>(new Reference(2));
            writer.WriteValue(bytes[48..]);
        }
    }

    private struct CursorSlotValue(int first, int second) : IShaderArguments
    {
        private readonly System.Runtime.CompilerServices.StrongBox<int> _counter = new();

        public static string ShaderTypeName => throw new InvalidOperationException("No type diagnostics.");

        public static int TransferSize => 32;

        public static IReadOnlyList<ShaderTransferField> TransferFields => throw new InvalidOperationException("No field diagnostics.");

        public int Writes => _counter.Value;

        public int DiagnosticCalls { get; private set; }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            _counter.Value++;
            if (first > 0)
            {
                writer.WriteValue(new byte[first]);
            }

            for (int index = 0; index < second; index++)
            {
                writer.WriteReference<uint>(new Reference((uint)index + 1));
            }
        }
    }

    private sealed class IdentityMetadata(IEnumerable<IShaderReference> references) : IShaderReferenceMetadata
    {
        public IReadOnlyList<IShaderReference> References { get; } = Array.AsReadOnly(references.ToArray());
    }

    private sealed class Reference(uint slot) : IGpuRef<uint>, IShaderReference
    {
        public object Table { get; } = new();

        public object Resource { get; } = new();

        public ShaderBufferAccess BufferAccess => default;

        public uint Slot => slot;

        public ulong Count => 1;

        public ulong OffsetInBytes => 0;

        public ulong SizeInBytes => 4;

        public ulong RegistrationOffsetInBytes => 0;

        public ulong RegistrationSizeInBytes => 4;

        public IGpuRef<uint> GetElement(ulong index) => throw new NotSupportedException();
    }

    private sealed class RootStorage(int size) : ITestShaderRootStorage
    {
        public int Size => size;

        public byte[] Bytes { get; } = new byte[size];

        public bool Disposed { get; private set; }

        public int WriteCount { get; private set; }

        public void Write(ReadOnlySpan<byte> bytes, int offset)
        {
            bytes.CopyTo(Bytes.AsSpan(offset, bytes.Length));
            WriteCount++;
        }

        public void Seal()
        {
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class MappedBuffer(int size, List<string> events) : IGraphicsBuffer<byte>
    {
        public byte[] Bytes { get; } = Enumerable.Repeat((byte)0xCD, size).ToArray();

        public int WriteCount { get; private set; }

        public ulong SizeInBytes => (ulong)Bytes.Length;

        public BufferLayout<byte> Layout => new(1, 1, 1, 1);

        public ulong Count => SizeInBytes;

        public BufferUsage Usage => BufferUsage.CopySource;

        public MemoryPreference Memory => MemoryPreference.Upload;

        public bool IsMapped => true;

        public void ValidateByteRange(ulong offset, ulong length)
        {
            if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
        }

        public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
        {
            ValidateByteRange(offset, length);
            Assert.Equal((ulong)source.Length, length);
            source.CopyTo(Bytes.AsSpan(checked((int)offset), checked((int)length)));
            WriteCount++;
            events.Add(Copy(checked((int)offset), source.ToArray()));
        }

        public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => throw new NotSupportedException();

        public ValueTask MapAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Unmap() => throw new NotSupportedException();

        public BufferSlice<byte> SliceElements(ulong offset, ulong count) => throw new NotSupportedException();

        public void CopyFrom(ReadOnlySpan<byte> source) => throw new NotSupportedException();

        public void CopyTo(Span<byte> destination) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
