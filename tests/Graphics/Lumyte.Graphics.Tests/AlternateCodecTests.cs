using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises the public alternate-codec upload seam without a GPU.</summary>
public sealed class AlternateCodecTests
{
    /// <summary>Alternate transfers are synchronous snapshots with identical upload guardrails.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task AlternateUploadOwnsBytesAndRejectsInvalidWritesAsync()
    {
        using var device = new CpuGraphicsDevice();
        var artifact = ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", [1], "{}");
        using IGraphicsShaderDataBuffer<Numeric> buffer = device.CreateShaderDataBuffer<Numeric>(artifact, 3, MemoryPreference.Upload);
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom<Alternate>([new(5)]));
        await buffer.MapAsync();
        Alternate[] values = [new(17)];
        buffer.CopyFrom<Alternate>(values, 1);
        values[0] = new(99);
        byte[] bytes = Assert.IsType<byte[]>(buffer.GetType().GetProperty("Bytes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(buffer));
        Assert.Equal(new byte[] { 0, 0, 0, 0, 17, 0, 0, 0, 0, 0, 0, 0 }, bytes);
        Assert.Throws<ArgumentException>(() => buffer.CopyFrom<WrongName>([]));
        Assert.Throws<ArgumentException>(() => buffer.CopyFrom<WrongSize>([]));
        Assert.Throws<ArgumentException>(() => buffer.CopyFrom<WrongCount>([]));
        Assert.Throws<ArgumentException>(() => buffer.CopyFrom<WrongRoot>([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.CopyFrom<Alternate>(values, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.CopyFrom<Alternate>([], 4));
        buffer.CopyFrom<Alternate>([], 3);
        Assert.Equal(17u, BitConverter.ToUInt32(bytes, 4));
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom<Alternate>([new(8, 3)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.CopyFrom<Alternate>([new(8, 5)]));
        buffer.Unmap();
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom<Alternate>([]));
    }

    /// <summary>Exercises the actual Vulkan codec and mapped-span boundary without creating a GPU device.</summary>
    [Fact]
    public unsafe void NativeAlternateCodecRetainsBoundsMappingAndSnapshot()
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        Assembly assembly = typeof(Lumyte.Graphics.Vulkan.VulkanDevice).Assembly;
        Type storageType = assembly.GetType("Lumyte.Graphics.Vulkan.VulkanBuffer`1", true)!.MakeGenericType(typeof(byte));
        object storage = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(storageType);
        Type bufferType = assembly.GetType("Lumyte.Graphics.Vulkan.VulkanShaderDataBuffer`1", true)!.MakeGenericType(typeof(Numeric));
        var buffer = (IGraphicsShaderDataBuffer<Numeric>)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(bufferType);
        void SetStorage(string name, object value) => storageType.GetField(name, Hidden)!.SetValue(storage, value);
        void SetBuffer(string name, object value) => bufferType.GetField(name, Hidden)!.SetValue(buffer, value);
        SetStorage("<Memory>k__BackingField", MemoryPreference.Upload);
        SetStorage("<SizeInBytes>k__BackingField", 12UL);
        SetStorage("_mapped", true);
        SetBuffer("<Memory>k__BackingField", MemoryPreference.Upload);
        SetBuffer("<Count>k__BackingField", 3UL);
        SetBuffer("<Storage>k__BackingField", storage);
        byte[] bytes = new byte[12];
        fixed (byte* pointer = bytes)
        {
            SetStorage("_pointer", Pointer.Box(pointer, typeof(byte*)));
            Alternate[] source = [new(17)];
            buffer.CopyFrom<Alternate>(source, 1);
            source[0] = new(99);
            Assert.Equal(17u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, 0));
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, 8));
            Assert.Throws<ArgumentException>(() => buffer.CopyFrom<WrongSize>([]));
            Assert.Equal(17u, BitConverter.ToUInt32(bytes, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.CopyFrom<Alternate>(source, 3));
            buffer.CopyFrom<Alternate>([], 3);
            SetStorage("_pending", true);
            Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom<Alternate>(source));
            SetStorage("_pending", false);
            SetStorage("_mapped", false);
            Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom<Alternate>(source));
            SetStorage("_mapped", true);
            Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom<Alternate>([new(1, 3)]));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.CopyFrom<Alternate>([new(1, 5)]));
        }
    }

    private readonly struct Numeric : IShaderArguments
    {
        public static string ShaderTypeName => "Numeric";

        public static int TransferSize => 4;

        public static int ReferenceCount => 0;

        public static IReadOnlyList<ShaderTransferField> TransferFields => throw new InvalidOperationException("Optional diagnostics must not be consulted.");

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => writer.WriteValue(new byte[4]);
    }

    private readonly struct Alternate(uint value, int bytes = 4) : IShaderArguments
    {
        public static string ShaderTypeName => Numeric.ShaderTypeName;

        public static int TransferSize => Numeric.TransferSize;

        public static int ReferenceCount => Numeric.ReferenceCount;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            byte[] data = new byte[bytes];
            BitConverter.GetBytes(value).AsSpan(0, Math.Min(bytes, 4)).CopyTo(data);
            writer.WriteValue(data);
        }
    }

    private readonly struct WrongName : IShaderArguments
    {
        public static string ShaderTypeName => "Wrong";

        public static int TransferSize => 4;

        public static int ReferenceCount => 0;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => throw new InvalidOperationException();
    }

    private readonly struct WrongSize : IShaderArguments
    {
        public static string ShaderTypeName => "Numeric";

        public static int TransferSize => 8;

        public static int ReferenceCount => 0;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => throw new InvalidOperationException();
    }

    private readonly struct WrongCount : IShaderArguments
    {
        public static string ShaderTypeName => "Numeric";

        public static int TransferSize => 4;

        public static int ReferenceCount => 1;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => throw new InvalidOperationException();
    }

    private readonly struct WrongRoot : IShaderArguments
    {
        public static string RootParameter => "other";

        public static string ShaderTypeName => "Numeric";

        public static int TransferSize => 4;

        public static int ReferenceCount => 0;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => throw new InvalidOperationException();
    }
}
