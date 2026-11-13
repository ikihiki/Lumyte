using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks direct bulk argument serialization without a codec registry.</summary>
public sealed class ShaderArgumentsTests
{
    /// <summary>Handwritten unmanaged values capture independent raw bytes.</summary>
    [Fact]
    public void HandwrittenImplementationRequiresNoRegistration()
    {
        var value = new ManualArguments(17);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        value = new(29);

        Assert.Equal("arguments", snapshot.RootParameter);
        Assert.Equal("ManualArguments", snapshot.ShaderTypeName);
        Assert.Empty(snapshot.Values);
        Assert.Equal(17u, BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Data.Span));
        Assert.Equal(29u, value.Value);
    }

    /// <summary>Checks static dispatch of a custom root name.</summary>
    [Fact]
    public void StaticRootParameterOverridesDefault()
    {
        var value = new CustomRootArguments(23);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);

        Assert.Equal("scene", snapshot.RootParameter);
        Assert.Equal("SceneArguments", snapshot.ShaderTypeName);
        Assert.Empty(snapshot.Values);
        Assert.Equal(23u, BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Data.Span));
    }

    /// <summary>Generated root and data implementations expose bytes and diagnostic fields separately.</summary>
    [Fact]
    public void GeneratedRootAndShaderDataImplementTheSameContract()
    {
        var camera = new BindingCameraArguments(Matrix4x4.Identity);
        var data = new BindingVector(new Vector3(11, 12, 13));
        ShaderValueSnapshot root = IShaderArguments.Capture(in camera);
        ShaderValueSnapshot element = IShaderArguments.Capture(in data);

        Assert.Equal("BindingCameraArguments", root.ShaderTypeName);
        Assert.Empty(root.Values);
        Assert.Equal("ViewProjection", Assert.Single(root.Fields).Path);
        Assert.Equal(64, root.Data.Length);
        Assert.Equal("BindingVector", element.ShaderTypeName);
        Assert.Empty(element.Values);
        Assert.Equal("Value", Assert.Single(element.Fields).Path);
        Assert.Equal(12, element.Data.Length);
    }

    /// <summary>Nested numeric values and matrices require exactly one writer call.</summary>
    [Fact]
    public void NestedStructAndMatrixAreOneBulkWrite()
    {
        var value = new BulkNestedArguments
        {
            Header = 7,
            Nested = new() { Position = new(11, 12, 13), Index = 19 },
            Transform = Matrix4x4.CreateTranslation(21, 22, 23),
        };
        CountingWriter writer = default;
        Write(in value, ref writer);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);

        Assert.Equal(1, writer.Count);
        Assert.Equal(0, writer.ReferenceCount);
        Assert.Equal(Unsafe.SizeOf<BulkNestedArguments>(), writer.Bytes);
        Assert.Equal(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)).ToArray(), snapshot.Data.ToArray());
        Assert.Empty(snapshot.Values);
        Assert.Contains(snapshot.Fields, field => field.Path == "Nested.Position" && field.Offset == 4 && field.Size == 12);
        Assert.Contains(snapshot.Fields, field => field.Path == "Transform" && field.Offset == 20 && field.Size == 64);
    }

    /// <summary>Copies deliberately initialized C# padding rather than reconstructing numeric fields.</summary>
    [Fact]
    public void ActualPaddingBytesArePreserved()
    {
        PaddedBulkArguments value = default;
        Span<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1));
        bytes.Fill(0xA5);
        value.Tag = 3;
        value.Value = 0x10203040;
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);

        Assert.Equal(8, snapshot.Data.Length);
        Assert.Equal(new byte[] { 3, 0xA5, 0xA5, 0xA5, 0x40, 0x30, 0x20, 0x10 }, snapshot.Data.ToArray());
        Assert.Equal(4, Assert.Single(snapshot.Fields, field => field.Path == "Value").Offset);
    }

    /// <summary>Private numeric storage forwards mutable properties without aliasing older snapshots.</summary>
    [Fact]
    public void MutableClassSnapshotsOwnNumericBytesAndReferenceIdentities()
    {
        var first = new TestReference();
        var second = new TestReference();
        var value = new MutableBulkArguments { Number = 17, Transform = Matrix4x4.Identity, Resource = first };
        ShaderValueSnapshot before = IShaderArguments.Capture(in value);
        value.Number = 29;
        value.Transform = Matrix4x4.CreateScale(2);
        value.Resource = second;
        ShaderValueSnapshot after = IShaderArguments.Capture(in value);
        CountingWriter writer = default;
        Write(in value, ref writer);

        Assert.Equal(1, writer.Count);
        Assert.Equal(1, writer.ReferenceCount);
        Assert.Equal(17u, BinaryPrimitives.ReadUInt32LittleEndian(before.Data.Span));
        Assert.Equal(29u, BinaryPrimitives.ReadUInt32LittleEndian(after.Data.Span));
        Assert.Equal(Matrix4x4.Identity, MemoryMarshal.Read<Matrix4x4>(before.Data.Span[4..]));
        Assert.Equal(Matrix4x4.CreateScale(2), MemoryMarshal.Read<Matrix4x4>(after.Data.Span[4..]));
        Assert.Same(first, Assert.Single(before.Values).Reference);
        Assert.Same(second, Assert.Single(after.Values).Reference);
        Assert.Equal(68, before.Values[0].Offset);
        Assert.Equal(84, before.Data.Length);
        Assert.Equal(42u, value.Counter);
        Assert.DoesNotContain(before.Fields, field => field.Path == nameof(value.Counter));
        Assert.Contains(before.Fields, field => field.Path == "Number" && field.Offset == 0 && field.ValueType == typeof(uint));
        Assert.Contains(before.Fields, field => field.Path == "Resource" && field.Offset == 68 && field.Size == 16 && field.IsReference);
        Assert.Empty(typeof(MutableBulkArguments).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
        Assert.DoesNotContain(before.Fields, field => field.Path.StartsWith("_data", StringComparison.Ordinal));
    }

    private static void Write<T>(in T value, ref CountingWriter writer)
        where T : IShaderArguments => value.Write(ref writer);

    private readonly record struct ManualArguments(uint Value) : IShaderArguments
    {
        public static string ShaderTypeName => "ManualArguments";

        public static int TransferSize => sizeof(uint);

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => writer.WriteBytes(in this);
    }

    private readonly record struct CustomRootArguments(uint Value) : IShaderArguments
    {
        public static string RootParameter => "scene";

        public static string ShaderTypeName => "SceneArguments";

        public static int TransferSize => sizeof(uint);

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => writer.WriteBytes(in this);
    }

    private struct CountingWriter : IShaderValueWriter
    {
        public int Count { get; private set; }

        public int Bytes { get; private set; }

        public int ReferenceCount { get; private set; }

        public void WriteBytes<T>(in T value)
            where T : unmanaged
        {
            Count++;
            Bytes += Unsafe.SizeOf<T>();
        }

        public void WriteReference<T>(string path, int offset, IGpuRef<T>? value) => ReferenceCount++;
    }

    private sealed class TestReference : IGpuRef<uint>
    {
        public ulong Count => 1;

        public IGpuRef<uint> GetElement(ulong index) => index == 0 ? this : throw new ArgumentOutOfRangeException(nameof(index));
    }
}
