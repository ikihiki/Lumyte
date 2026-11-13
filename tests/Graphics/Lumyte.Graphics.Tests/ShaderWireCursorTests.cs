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

        // Explicit capture remains bulk/raw and declaration ordered, not cursor ordered.
        ShaderValueSnapshot captured = IShaderArguments.Capture(in value);
        Assert.Equal(original, captured.Data.ToArray());
        Assert.Equal(new[] { 32, 8 }, captured.Values.Select(slot => slot.Offset));
        Assert.Same(b, captured.Values[0].Reference);
        Assert.Same(a, captured.Values[1].Reference);
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
        Assert.Equal(2, buffer.WriteCount);
        Assert.StartsWith("W:0:32:", events[0]);
        Assert.Equal("E:8", events[1]);
    }

    /// <summary>Handwritten codecs run once and retain reference evaluation order even before bytes.</summary>
    /// <param name="referencesFirst">Whether the codec emits references before its raw bytes.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandwrittenFallbackPreservesEmissionOrder(bool referencesFirst)
    {
        var events = new List<string>();
        var value = new LegacyValue(events, referencesFirst);
        using var buffer = new MappedBuffer(64, events);
        var metadata = new IShaderReferenceMetadata?[1];

        byte[] EncodeSlot(ShaderValue slot)
        {
            events.Add($"E:{slot.Offset}");
            return Encode(slot);
        }

        ShaderDataUpload.CopyFrom<LegacyValue>([value], buffer, 0, metadata, EncodeSlot, null);
        Assert.Equal(1, value.Writes);
        Assert.Equal(0, value.DiagnosticCalls);
        Assert.Equal(new[] { "G:32", "G:8", "E:32", "E:8" }, events.Where(item => !item.StartsWith("W:")));
        Assert.Equal(3, buffer.WriteCount);
        Assert.StartsWith("W:0:64:", events[referencesFirst ? 2 : 0]);
        Assert.Equal("E:32", events[3]);
        Assert.StartsWith("W:32:16:", events[4]);
        Assert.Equal("E:8", events[5]);
        Assert.StartsWith("W:8:16:", events[6]);
        Assert.Equal(17U, MemoryMarshal.Read<uint>(buffer.Bytes));
        Assert.Equal(new uint[] { 2, 1 }, metadata[0]!.References.Select(reference => reference.Slot));
    }

    /// <summary>Handwritten fallback rejects intersecting slots without diagnostics or metadata publication.</summary>
    /// <param name="first">The first emitted reference offset.</param>
    /// <param name="second">The intersecting reference offset.</param>
    [Theory]
    [InlineData(8, 16)]
    [InlineData(16, 8)]
    public void HandwrittenFallbackRejectsOverlappingSlots(int first, int second)
    {
        var value = new LegacySlotValue(first, second);
        var unvisited = new LegacySlotValue(0, 16);
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

        Assert.Throws<ArgumentException>(() => ShaderDataUpload.CopyFrom<LegacySlotValue>([value, unvisited], buffer, 1, metadata, EncodeSlot, null, CreateMetadata));
        Assert.Equal(1, value.Writes);
        Assert.Equal(0, value.DiagnosticCalls);
        Assert.Equal(0, unvisited.Writes);
        Assert.Equal(0, encodes);
        Assert.Equal(0, factories);
        Assert.Null(metadata[1]);
        Assert.Null(metadata[2]);
        Assert.Same(old, metadata[0]);
        Assert.Same(old, metadata[3]);
        Assert.Equal(1, buffer.WriteCount);
        Assert.All(buffer.Bytes[..32], item => Assert.Equal((byte)0xCD, item));
        Assert.All(buffer.Bytes[64..], item => Assert.Equal((byte)0xCD, item));
    }

    /// <summary>Adjacent fallback slots remain valid in either emission order.</summary>
    /// <param name="first">The first emitted reference offset.</param>
    /// <param name="second">The adjacent reference offset.</param>
    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    public void HandwrittenFallbackAcceptsAdjacentSlots(int first, int second)
    {
        var value = new LegacySlotValue(first, second);
        using var buffer = new MappedBuffer(32, []);
        var metadata = new IShaderReferenceMetadata?[1];

        ShaderDataUpload.CopyFrom<LegacySlotValue>([value], buffer, 0, metadata, Encode, null);

        Assert.Equal(1, value.Writes);
        Assert.Equal(0, value.DiagnosticCalls);
        Assert.Equal(3, buffer.WriteCount);
        Assert.All(buffer.Bytes.AsSpan(first, 16).ToArray(), item => Assert.Equal((byte)1, item));
        Assert.All(buffer.Bytes.AsSpan(second, 16).ToArray(), item => Assert.Equal((byte)2, item));
        Assert.Equal(new uint[] { 1, 2 }, metadata[0]!.References.Select(reference => reference.Slot));
    }

    /// <summary>Custom generated getters use fallback validation before encoding overlapping slots.</summary>
    [Fact]
    public void GeneratedCustomGetterRejectsOverlappingFallbackSlots()
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

        Assert.Throws<ArgumentException>(() => ShaderDataUpload.CopyFrom<WireCursorOverlapGetterValue>([value, value], buffer, 1, metadata, EncodeSlot, null));

        Assert.Equal(1, value.AGetterCalls);
        Assert.Equal(1, value.BGetterCalls);
        Assert.Equal(0, encodes);
        Assert.Equal(1, buffer.WriteCount);
        Assert.Null(metadata[1]);
        Assert.Null(metadata[2]);
        Assert.Same(old, metadata[0]);
        Assert.Same(old, metadata[3]);
        Assert.All(buffer.Bytes[..32], item => Assert.Equal((byte)0xCD, item));
        Assert.All(buffer.Bytes[64..], item => Assert.Equal((byte)0xCD, item));
    }

    /// <summary>Overlapping generated fallback slots cannot publish a replacement root.</summary>
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

        Assert.Equal(1, invalid.AGetterCalls);
        Assert.Equal(1, invalid.BGetterCalls);
        Assert.Equal(0, encodes);
        Assert.Same(previous, selected);
        Assert.Same(originalMetadata, selected.References);
        Assert.Equal(original, previousStorage.Bytes);
        Assert.False(previousStorage.Disposed);
        Assert.True(candidate.Disposed);
        Assert.Equal(1, candidate.WriteCount);
    }

    /// <summary>Overlapping snapshot fixups dispose the unpublished allocation before changing selection.</summary>
    /// <param name="first">The first captured reference offset.</param>
    /// <param name="second">The intersecting reference offset.</param>
    [Theory]
    [InlineData(8, 16)]
    [InlineData(16, 8)]
    public void SnapshotOverlapLeavesSelectedRootUntouched(int first, int second)
    {
        var a = new Reference(1);
        var b = new Reference(2);
        var valid = new ShaderValueSnapshot("arguments", "SnapshotWire", new byte[32], [new(typeof(uint), a, 0), new(typeof(uint), b, 16)], []);
        ShaderValueSnapshot invalid = valid with { Values = new ShaderValue[] { new(typeof(uint), a, first), new(typeof(uint), b, second) } };
        var previousStorage = new RootStorage(32);
        var selected = ShaderRootArguments.CreateSnapshot(valid, _ => previousStorage, encode: Encode);
        using ShaderRootArguments previous = selected;
        byte[] original = previousStorage.Bytes.ToArray();
        IShaderReferenceMetadata originalMetadata = previous.References;
        var candidate = new RootStorage(32);
        int encodes = 0;
        int factories = 0;
        int diagnostics = 0;
        invalid = invalid with { DiagnosticFields = DiagnosticFields };

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

        IReadOnlyList<ShaderTransferField> DiagnosticFields()
        {
            diagnostics++;
            throw new InvalidOperationException("No snapshot diagnostics.");
        }

        Assert.Throws<ArgumentException>(() => ShaderDataLayout.ValidateSnapshot(invalid));
        Assert.Throws<ArgumentException>(() => selected = ShaderRootArguments.CreateSnapshot(invalid, _ => candidate, encode: EncodeSlot, metadataFactory: CreateMetadata));

        Assert.Same(previous, selected);
        Assert.Same(originalMetadata, selected.References);
        Assert.Equal(new IShaderReference[] { a, b }, selected.References.References);
        Assert.Equal(original, previousStorage.Bytes);
        Assert.False(previousStorage.Disposed);
        Assert.True(candidate.Disposed);
        Assert.Equal(0, candidate.WriteCount);
        Assert.Equal(0, encodes);
        Assert.Equal(0, factories);
        Assert.Equal(0, diagnostics);
    }

    /// <summary>Adjacent snapshot fixups remain valid in either declaration order.</summary>
    /// <param name="first">The first captured reference offset.</param>
    /// <param name="second">The adjacent reference offset.</param>
    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    public void SnapshotAcceptsAdjacentSlots(int first, int second)
    {
        var a = new Reference(1);
        var b = new Reference(2);
        var snapshot = new ShaderValueSnapshot("arguments", "SnapshotWire", new byte[32], [new(typeof(uint), a, first), new(typeof(uint), b, second)], []);
        var storage = new RootStorage(32);

        ShaderDataLayout.ValidateSnapshot(snapshot);
        using var root = ShaderRootArguments.CreateSnapshot(snapshot, _ => storage, encode: Encode);

        Assert.Equal(3, storage.WriteCount);
        Assert.All(storage.Bytes.AsSpan(first, 16).ToArray(), item => Assert.Equal((byte)1, item));
        Assert.All(storage.Bytes.AsSpan(second, 16).ToArray(), item => Assert.Equal((byte)2, item));
        Assert.Equal(new IShaderReference[] { a, b }, root.References.References);
        Assert.False(storage.Disposed);
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
        Assert.True(buffer.WriteCount > 0);
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
        Assert.True(candidate.WriteCount > 0);
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
    private struct LegacyWire
    {
        [FieldOffset(0)]
        public uint Number;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct LegacySlotWire
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

    private sealed class LegacyValue(List<string> events, bool referencesFirst) : IShaderData
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
            var wire = new LegacyWire { Number = 17 };
            if (!referencesFirst)
            {
                writer.WriteBytes(in wire);
            }

            events.Add("G:32");
            writer.WriteReference<uint>(32, new Reference(2));
            events.Add("G:8");
            writer.WriteReference<uint>(8, new Reference(1));
            if (referencesFirst)
            {
                writer.WriteBytes(in wire);
            }
        }
    }

    private sealed class LegacySlotValue(int first, int second) : IShaderData
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
            var wire = new LegacySlotWire { Number = 17 };
            writer.WriteBytes(in wire);
            writer.WriteReference<uint>(first, new Reference(1));
            writer.WriteReference<uint>(second, new Reference(2));
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
