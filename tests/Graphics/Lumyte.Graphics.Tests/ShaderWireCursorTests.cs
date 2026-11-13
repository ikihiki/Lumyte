using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Deterministic wire-order, compatibility and ownership regressions.</summary>
public sealed class ShaderWireCursorTests
{
    /// <summary>Generated cursors write raw gaps and encoded slots directly into final storage.</summary>
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

        ShaderDataUpload.CopyFrom<WireCursorValue>([value], buffer, 0, metadata, EncodeSlot, null, CreateMetadata);

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

        // Readonly capture also uses physical cursor order and zeroes reference slots.
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

        ShaderDataUpload.CopyFrom<WireCursorGetterValue>([value], buffer, 0, new IShaderReferenceMetadata?[1], EncodeSlot, null);
        Assert.Equal(17U, MemoryMarshal.Read<uint>(buffer.Bytes));
        Assert.Equal(999U, value.Number);
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

        ShaderDataUpload.CopyFrom<WireCursorOrderedGetterValue>([value], buffer, 0, new IShaderReferenceMetadata?[1], EncodeSlot, null);
        Assert.Equal(new[] { "A", "B" }, getters);
        Assert.Equal(new[] { 8, 32 }, offsets);
        Assert.Equal(17u, MemoryMarshal.Read<uint>(buffer.Bytes));
        Assert.Equal(999u, value.Number);
        Assert.Equal(5, buffer.WriteCount);
    }

    /// <summary>Handwritten codecs invoke the mandatory cursor once in physical reference order.</summary>
    [Fact]
    public void HandwrittenCursorPreservesEmissionOrder()
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

        ShaderDataUpload.CopyFrom<CursorValue>([value], buffer, 0, metadata, EncodeSlot, null);
        Assert.Equal(1, value.Writes);
        Assert.Equal(0, value.DiagnosticCalls);
        Assert.Equal(new[] { "G:8", "E:8", "G:32", "E:32" }, events.Where(item => !item.StartsWith("W:")));
        Assert.Equal(5, buffer.WriteCount);
        Assert.Equal(17U, MemoryMarshal.Read<uint>(buffer.Bytes));
        Assert.Equal(new uint[] { 1, 2 }, metadata[0]!.References.Select(reference => reference.Slot));
    }

    /// <summary>Handwritten cursor rejects malformed slots without diagnostics or metadata publication.</summary>
    /// <param name="first">The first emitted reference offset.</param>
    /// <param name="second">The second emitted reference offset.</param>
    [Theory]
    [InlineData(8, 16)]
    [InlineData(16, 8)]
    [InlineData(8, -1)]
    [InlineData(8, 17)]
    [InlineData(-1, 0)]
    [InlineData(17, 0)]
    [InlineData(int.MaxValue, 0)]
    public void HandwrittenCursorRejectsMalformedSlots(int first, int second)
    {
        var value = new CursorSlotValue(first, second);
        var unvisited = new CursorSlotValue(0, 16);
        var old = new IdentityMetadata([]);
        IShaderReferenceMetadata?[] metadata = [old, old, old, old];
        using var buffer = new MappedBuffer(128, []);
        int encodes = 0;
        int factories = 0;

        byte[] EncodeSlot(ShaderValue slot)
        {
            encodes++;
            return Encode(slot);
        }

        IShaderReferenceMetadata CreateMetadata(IEnumerable<IShaderReference> references)
        {
            factories++;
            return new IdentityMetadata(references);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => ShaderDataUpload.CopyFrom<CursorSlotValue>([value, unvisited], buffer, 1, metadata, EncodeSlot, null, CreateMetadata));
        Assert.Equal(1, value.Writes);
        Assert.Equal(0, value.DiagnosticCalls);
        Assert.Equal(0, unvisited.Writes);
        bool validFirst = first is >= 0 and <= 16;
        Assert.Equal(validFirst ? 1 : 0, encodes);
        Assert.Equal(0, factories);
        Assert.Null(metadata[1]);
        Assert.Null(metadata[2]);
        Assert.Same(old, metadata[0]);
        Assert.Same(old, metadata[3]);
        Assert.Equal(validFirst ? 2 : 0, buffer.WriteCount);
        if (validFirst)
        {
            Assert.Equal(17u, MemoryMarshal.Read<uint>(buffer.Bytes.AsSpan(32)));
            Assert.All(buffer.Bytes.AsSpan(32 + first, 16).ToArray(), item => Assert.Equal((byte)1, item));
        }

        Assert.All(buffer.Bytes[..32], item => Assert.Equal((byte)0xCD, item));
        Assert.All(buffer.Bytes[64..], item => Assert.Equal((byte)0xCD, item));
    }

    /// <summary>Adjacent physically ordered cursor slots remain valid.</summary>
    /// <param name="first">The first emitted reference offset.</param>
    /// <param name="second">The adjacent reference offset.</param>
    [Theory]
    [InlineData(0, 16)]
    public void HandwrittenCursorAcceptsAdjacentSlots(int first, int second)
    {
        var value = new CursorSlotValue(first, second);
        using var buffer = new MappedBuffer(32, []);
        var metadata = new IShaderReferenceMetadata?[1];

        ShaderDataUpload.CopyFrom<CursorSlotValue>([value], buffer, 0, metadata, Encode, null);

        Assert.Equal(1, value.Writes);
        Assert.Equal(0, value.DiagnosticCalls);
        Assert.Equal(2, buffer.WriteCount);
        Assert.All(buffer.Bytes.AsSpan(first, 16).ToArray(), item => Assert.Equal((byte)1, item));
        Assert.All(buffer.Bytes.AsSpan(second, 16).ToArray(), item => Assert.Equal((byte)2, item));
        Assert.Equal(new uint[] { 1, 2 }, metadata[0]!.References.Select(reference => reference.Slot));
    }

    /// <summary>Malformed generated layouts repeatedly fail before getters, encoding or destination writes.</summary>
    [Fact]
    public void GeneratedCustomGetterRejectsOverlappingCursorSlots()
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

        ArgumentException first = Assert.Throws<ArgumentException>(() => ShaderDataUpload.CopyFrom<WireCursorOverlapGetterValue>([value, value], buffer, 1, metadata, EncodeSlot, null));
        ArgumentException repeated = Assert.Throws<ArgumentException>(() => ShaderDataUpload.CopyFrom<WireCursorOverlapGetterValue>([value, value], buffer, 1, metadata, EncodeSlot, null));
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

    /// <summary>Overlapping generated cursor slots cannot publish a replacement root.</summary>
    [Fact]
    public void GeneratedCustomGetterOverlapLeavesSelectedRootUntouched()
    {
        var valid = new WireCursorValue(1) { A = new Reference(1), B = new Reference(2) };
        var invalid = new WireCursorOverlapGetterValue(new Reference(3), new Reference(4));
        var previousStorage = new RootStorage(64);
        var selected = ShaderRootArguments.Create(in valid, _ => previousStorage, encode: Encode);
        using ShaderRootArguments previous = selected;
        byte[] original = previousStorage.Bytes.ToArray();
        IShaderReferenceMetadata originalMetadata = previous.References;
        var candidate = new RootStorage(32);
        int encodes = 0;

        byte[] EncodeSlot(ShaderValue slot)
        {
            encodes++;
            return Encode(slot);
        }

        Assert.Throws<ArgumentException>(() => selected = ShaderRootArguments.Create(in invalid, _ => candidate, encode: EncodeSlot));

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

    /// <summary>Invalid direct-cursor slots are rejected before any corresponding sink operation.</summary>
    /// <param name="first">An optional previously emitted slot.</param>
    /// <param name="invalid">The negative, overflowing, overlapping, aliased or unordered slot.</param>
    [Theory]
    [InlineData(-1, -1)]
    [InlineData(-1, 49)]
    [InlineData(-1, int.MaxValue)]
    [InlineData(8, 8)]
    [InlineData(8, 16)]
    [InlineData(32, 8)]
    public void DirectCursorRejectsInvalidSlotsBeforeSinkWrites(int first, int invalid)
    {
        var events = new List<string>();
        var sink = new TraceSink(events);
        var cursor = new ShaderWireCursor<TraceSink>(new byte[64], ref sink);
        if (first >= 0)
        {
            cursor.WriteReference<uint>(first, null);
        }

        string[] before = events.ToArray();
        bool rejected = false;
        try
        {
            cursor.WriteReference<uint>(invalid, null);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }

        Assert.True(rejected);
        Assert.Equal(before, events);
    }

    /// <summary>Completion copies the tail once and permanently closes the cursor.</summary>
    [Fact]
    public void DirectCursorCompletesExactlyOnce()
    {
        var events = new List<string>();
        var sink = new TraceSink(events);
        var cursor = new ShaderWireCursor<TraceSink>(new byte[64], ref sink);
        cursor.WriteReference<uint>(8, null);
        cursor.Complete();
        Assert.Equal(new[] { "R:0:8", "E:8", "R:24:40" }, events);
        int rejected = 0;
        try
        {
            cursor.Complete();
        }
        catch (InvalidOperationException)
        {
            rejected++;
        }

        try
        {
            cursor.WriteReference<uint>(32, null);
        }
        catch (InvalidOperationException)
        {
            rejected++;
        }

        Assert.Equal(2, rejected);
        Assert.Equal(3, events.Count);
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

        Assert.Same(failure, Record.Exception(() => ShaderDataUpload.CopyFrom<WireCursorValue>([value, value], buffer, 1, metadata, EncodeSlot, null)));
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
        Assert.Throws<ArgumentException>(() => ShaderDataUpload.CopyFrom<WireCursorValue>([value], buffer, 0, metadata, _ => new byte[size], null));
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
        Assert.Throws<ArgumentOutOfRangeException>(() => ShaderDataUpload.CopyFrom<WireCursorValue>([value], buffer, ulong.MaxValue, metadata, _ => throw new InvalidOperationException("Must not encode."), null));
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
        var selected = ShaderRootArguments.Create(in value, _ => previousStorage, encode: Encode);
        using ShaderRootArguments previous = selected;
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

        Assert.Same(failure, Record.Exception(() => selected = ShaderRootArguments.Create(in value, _ => candidate, encode: EncodeSlot)));
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

    private readonly struct TraceSink(List<string> events) : IShaderWireSink
    {
        public void WriteRaw(ReadOnlySpan<byte> bytes, int offset) => events.Add($"R:{offset}:{bytes.Length}");

        public void WriteReference<T>(int offset, IGpuRef<T>? value) => events.Add($"E:{offset}");
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct CursorWire
    {
        [FieldOffset(0)]
        public uint Number;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct CursorSlotWire
    {
        [FieldOffset(0)]
        public uint Number;
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

    private sealed class CursorValue(List<string> events) : IShaderData
    {
        public static string ShaderTypeName => throw new InvalidOperationException("No type diagnostics.");

        public static int TransferSize => 64;

        public static IReadOnlyList<ShaderTransferField> TransferFields => throw new InvalidOperationException("No field diagnostics.");

        public int Writes { get; private set; }

        public int DiagnosticCalls { get; private set; }

        public IReadOnlyList<ShaderTransferField> GetTransferFields()
        {
            DiagnosticCalls++;
            throw new InvalidOperationException("No instance diagnostics.");
        }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            Writes++;
            var wire = new CursorWire { Number = 17 };
            var emitter = new References(events);
            ShaderWire.Write(in wire, ref emitter, ref writer);
        }

        private readonly struct References(List<string> events) : IShaderReferenceEmitter
        {
            public void Emit<TCursor>(ref TCursor cursor)
                where TCursor : IShaderReferenceCursor, allows ref struct
            {
                events.Add("G:8");
                cursor.WriteReference<uint>(8, new Reference(1));
                events.Add("G:32");
                cursor.WriteReference<uint>(32, new Reference(2));
            }
        }
    }

    private sealed class CursorSlotValue(int first, int second) : IShaderData
    {
        public static string ShaderTypeName => throw new InvalidOperationException("No type diagnostics.");

        public static int TransferSize => 32;

        public static IReadOnlyList<ShaderTransferField> TransferFields => throw new InvalidOperationException("No field diagnostics.");

        public int Writes { get; private set; }

        public int DiagnosticCalls { get; private set; }

        public IReadOnlyList<ShaderTransferField> GetTransferFields()
        {
            DiagnosticCalls++;
            throw new InvalidOperationException("No instance diagnostics.");
        }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            Writes++;
            var wire = new CursorSlotWire { Number = 17 };
            var emitter = new References(first, second);
            ShaderWire.Write(in wire, ref emitter, ref writer);
        }

        private readonly struct References(int first, int second) : IShaderReferenceEmitter
        {
            public void Emit<TCursor>(ref TCursor cursor)
                where TCursor : IShaderReferenceCursor, allows ref struct
            {
                cursor.WriteReference<uint>(first, new Reference(1));
                cursor.WriteReference<uint>(second, new Reference(2));
            }
        }
    }

    private sealed class RootStorage(int size) : IShaderRootStorage
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
