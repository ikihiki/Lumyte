using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Validation;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Byte oracle for actual backend CopyFrom, without a shared encoder callback.</summary>
public sealed class NativeShaderWriterAbiTests
{
    private const string Source = """
        #include "lumyte.slang"
        struct NativeAbiValue
        {
            uint Number;
            uint Padding;
            GpuSamplerRef Sampler;
            GpuTextureRef Texture;
            GpuRWBufferRef<uint> Writable;
            GpuBufferRef<uint> First;
            GpuBufferRef<uint> Second;
        };
        struct Root { GpuBufferRef<NativeAbiValue> Values; };
        ConstantBuffer<Root> arguments;
        [shader("compute")]
        [numthreads(1, 1, 1)]
        void main() { NativeAbiValue value = arguments.Values.Load(0); value.Writable.Store(0, value.Number); }
        """;

    private delegate Span<byte> MappedUpload(ulong offset, int count);

    private delegate void WriteNative(object owner, IGpuRef<uint>? reference, Span<byte> destination, int size, bool recover);

    /// <summary>WGSL stores stable slots, relative subranges, exact counts, and reserved zeros.</summary>
    /// <returns>The native checks with validation disabled and enabled.</returns>
    [Trait("Execution", "Gpu")]
    [GpuFact]
    public async Task WgpuNativeBytesAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.Wgsl);
        foreach (bool validation in new[] { false, true })
        {
            using var raw = WgpuDevice.Create();
            using ValidationDevice? layer = validation ? new(raw) : null;
            IGraphicDevice device = layer ?? (IGraphicDevice)raw;

            await CheckAsync(device, artifact, ShaderTarget.Wgsl, validation);
        }
    }

    /// <summary>Vulkan stores actual native addresses plus subranges and exact counts.</summary>
    /// <returns>The native checks with validation disabled and enabled.</returns>
    [Trait("Execution", "Gpu")]
    [GpuFact]
    public async Task VulkanNativeBytesAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.SpirV);
        foreach (bool validation in new[] { false, true })
        {
            using var raw = VulkanDevice.Create();
            using ValidationDevice? layer = validation ? new(raw) : null;
            IGraphicDevice device = layer ?? (IGraphicDevice)raw;

            await CheckAsync(device, artifact, ShaderTarget.SpirV, validation);
            await CheckAddressLifetimeAsync(raw);
        }
    }

    private static WriteNative NativeAdapter(IGraphicDevice owner)
    {
        string backend = owner is WgpuDevice ? "Wgpu" : "Vulkan";
        Type writer = owner.GetType().Assembly.GetType($"Lumyte.Graphics.{backend}.{backend}ShaderValueWriter", true)!;
        var method = new DynamicMethod("WriteNativeReference", typeof(void), [typeof(object), typeof(IGpuRef<uint>), typeof(Span<byte>), typeof(int), typeof(bool)], typeof(NativeShaderWriterAbiTests).Module, true);
        ILGenerator il = method.GetILGenerator();
        LocalBuilder local = il.DeclareLocal(writer);
        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, owner.GetType());
        il.Emit(OpCodes.Ldarg_3);
        if (backend == "Wgpu")
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, Assert.Single(writer.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)));
        MethodInfo write = writer.GetMethod("WriteReference")!.MakeGenericMethod(typeof(uint));
        Label normal = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Brfalse, normal);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, write);
        il.BeginCatchBlock(typeof(ArgumentException));
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();
        il.MarkLabel(normal);
        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, write);
        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Call, writer.GetMethod("Complete", BindingFlags.Instance | BindingFlags.NonPublic)!);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<WriteNative>();
    }

    private static void CheckDirectReference(IGraphicDevice device, IGpuRef<uint> reference, byte[] expected)
    {
        WriteNative write = NativeAdapter(device);
        foreach (bool recover in new[] { false, true })
        {
            byte[] destination = Enumerable.Repeat((byte)0xCD, 24).ToArray();
            write(device, reference, destination.AsSpan(4, 16), 16, recover);
            Assert.Equal(expected, destination[4..20]);
            Assert.All(destination[..4], item => Assert.Equal((byte)0xCD, item));
            Assert.All(destination[20..], item => Assert.Equal((byte)0xCD, item));
        }

        byte[] shortDestination = Enumerable.Repeat((byte)0xA7, 24).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => write(device, reference, shortDestination.AsSpan(4, 15), 15, false));
        Assert.All(shortDestination, item => Assert.Equal((byte)0xA7, item));
        Assert.Throws<ArgumentException>(() => write(device, null, shortDestination.AsSpan(4, 16), 16, false));
        Assert.All(shortDestination, item => Assert.Equal((byte)0xA7, item));
    }

    private static async Task CheckAddressLifetimeAsync(VulkanDevice device)
    {
        object? previous = null;
        for (int generation = 0; generation < 2; generation++)
        {
            using IGraphicsBuffer<uint> buffer = device.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.ShaderRead, Memory = MemoryPreference.Upload });
            Assert.NotSame(previous, buffer);
            previous = buffer;
            Type contract = buffer.GetType().GetInterfaces().Single(type => type.Name == "IVulkanShaderBuffer");
            PropertyInfo address = contract.GetProperty("ShaderAddress")!;
            ulong expected = Address(device, buffer);
            Assert.NotEqual(0UL, expected);
            Assert.Equal(expected, address.GetValue(buffer));
            Assert.Equal(expected, address.GetValue(buffer));
            FieldInfo cached = buffer.GetType().GetField("_shaderAddress", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Equal(expected, cached.GetValue(buffer));
            await buffer.MapAsync();
            try
            {
                TargetInvocationException mapped = Assert.Throws<TargetInvocationException>(() => address.GetValue(buffer));
                Assert.IsType<InvalidOperationException>(mapped.InnerException);
            }
            finally
            {
                buffer.Unmap();
            }

            Assert.Equal(expected, address.GetValue(buffer));
            buffer.Dispose();
            TargetInvocationException disposed = Assert.Throws<TargetInvocationException>(() => address.GetValue(buffer));
            Assert.IsType<ObjectDisposedException>(disposed.InnerException);

            // Drivers may reuse addresses. Each new owner must match its own independent native query.
        }
    }

    private static Task<ShaderArtifact> CompileAsync(ShaderTarget target)
        => new SlangShaderCompiler().CompileAsync(new() { Source = Source, Target = target, Stage = ShaderStage.Compute });

    private static async Task CheckAsync(IGraphicDevice device, ShaderArtifact artifact, ShaderTarget target, bool validation)
    {
        using IGraphicsShaderDataBuffer<NativeAbiValue> upload = device.CreateShaderDataBuffer<NativeAbiValue>(artifact, 1, MemoryPreference.Upload);
        using IGraphicsBuffer<uint> numbers = device.CreateBuffer<uint>(new() { Count = 12, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite });
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new());
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 8, SamplerCapacity = 8, TextureCapacity = 8 });
        IGpuRef<uint> writable = table.WriteBuffer(3, numbers.SliceElements(2, 3), ShaderBufferAccess.ReadWrite);
        IGpuRef<uint> first = table.WriteBuffer(4, numbers.SliceElements(4, 4)).GetElement(1);
        IGpuRef<uint> second = table.WriteBuffer(6, numbers.SliceElements(8, 2));
        var value = new NativeAbiValue { Number = 0x12345678, Sampler = table.WriteSampler(2, sampler), Texture = table.WriteTexture(5, view), Writable = writable, First = first, Second = second };
        Assert.Equal(88UL, upload.ShaderElementStrideInBytes);
        await upload.MapAsync();
        object backendUpload = NativeIdentity(upload);
        object storage = backendUpload.GetType().GetProperty("Storage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backendUpload)!;
        MappedUpload mapped = storage.GetType().GetMethod("GetMappedUploadSpan", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<MappedUpload>(storage);
        mapped(0, 88).Fill(0xCD);
        upload.CopyFrom([value]);
        byte[] firstBytes = mapped(0, 88).ToArray();
        IShaderReferenceMetadata? firstRevision = QueryDependencies((IShaderDataSource)upload, validation);
        IShaderReference[] captured = firstRevision?.References.ToArray() ?? [];
        mapped(0, 88).Fill(0xA7);
        upload.CopyFrom([value]); // Repeated physical references reuse the same allocation address.
        IShaderReferenceMetadata? nextRevision = QueryDependencies((IShaderDataSource)upload, validation);
        if (validation)
        {
            Assert.Equal(5, firstRevision!.References.Count);
            Assert.Equal(captured, firstRevision.References.ToArray());
            Assert.NotSame(firstRevision, nextRevision);
            Assert.All(captured, reference => Assert.Contains(reference, nextRevision!.References));
        }

        // Inspect the actual mapped backend allocation without adopting its raw buffer
        // into the validation domain or recording commands with mixed identities.
        byte[] actual = mapped(0, 88).ToArray();
        upload.Unmap();

        byte[] expected = new byte[88];
        BinaryPrimitives.WriteUInt32LittleEndian(expected, 0x12345678);
        Slot(expected.AsSpan(8, 16), 2, 0, 1);
        Slot(expected.AsSpan(24, 16), 5, 0, 1);
        if (target == ShaderTarget.Wgsl)
        {
            Slot(expected.AsSpan(40, 16), 3, 0, 3);
            Slot(expected.AsSpan(56, 16), 4, 4, 1);
            Slot(expected.AsSpan(72, 16), 6, 0, 2);
        }
        else
        {
            ulong address = Address(device, numbers);
            AddressSlot(expected.AsSpan(40, 16), address + 8, 3);
            AddressSlot(expected.AsSpan(56, 16), address + 20, 1);
            AddressSlot(expected.AsSpan(72, 16), address + 32, 2);
        }

        if (!validation)
        {
            CheckDirectReference(device, first, expected[56..72]);
        }

        Assert.Equal(expected, firstBytes);
        Assert.Equal(expected, actual);
        await CheckMalformedAsync(device, artifact, value, validation);
    }

    private static async Task CheckMalformedAsync(IGraphicDevice device, ShaderArtifact artifact, NativeAbiValue value, bool validation)
    {
        using IGraphicsShaderDataBuffer<ManualBadAbiValue> upload = device.CreateShaderDataBuffer<ManualBadAbiValue>(artifact, 5, MemoryPreference.Upload);
        IShaderDataSource metadata = Assert.IsAssignableFrom<IShaderDataSource>(upload);
        var valid = new ManualBadAbiValue(value);
        await upload.MapAsync();
        try
        {
            foreach (int mode in new[] { 1, 2 })
            {
                upload.CopyFrom([valid, valid, valid, valid, valid]);
                IShaderReferenceMetadata? before = QueryDependencies(metadata, validation);
                IShaderReference[] captured = before?.References.ToArray() ?? [];
                var malformed = new ManualBadAbiValue(value, mode);
                if (mode == 1)
                {
                    Assert.Throws<InvalidOperationException>(() => upload.CopyFrom([valid, malformed, valid], 1));
                }
                else
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => upload.CopyFrom([valid, malformed, valid], 1));
                }

                AssertUnknownDependencies(metadata, validation);
                upload.CopyFrom([valid], 1);
                AssertUnknownDependencies(metadata, validation);
                if (validation)
                {
                    Assert.Equal(captured, before!.References.ToArray());
                }

                upload.CopyFrom([valid, valid, valid, valid, valid]);
                QueryDependencies(metadata, validation);
            }
        }
        finally
        {
            upload.Unmap();
        }
    }

    private static IShaderReferenceMetadata? QueryDependencies(IShaderDataSource source, bool validation)
    {
        Assert.Equal(validation, source.TryGetDependencies(out IShaderReferenceMetadata? dependencies));
        if (validation)
        {
            Assert.NotNull(dependencies);
        }
        else
        {
            Assert.Null(dependencies);
        }

        return dependencies;
    }

    private static void AssertUnknownDependencies(IShaderDataSource source, bool validation)
    {
        if (validation)
        {
            Assert.Throws<InvalidOperationException>(() => source.TryGetDependencies(out _));
        }
        else
        {
            QueryDependencies(source, validation);
        }
    }

    private static void Slot(Span<byte> bytes, uint slot, uint offset, uint count)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, slot);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], offset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], count);
    }

    private static void AddressSlot(Span<byte> bytes, ulong address, uint count)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, address);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], count);
    }

    private static object NativeIdentity(object value)
    {
        if (value is ValidationDevice device)
        {
            return typeof(ValidationDevice).GetProperty("Backend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;
        }

        for (Type? type = value.GetType(); type != null; type = type.BaseType)
        {
            if (type.GetProperty("Raw", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } raw)
            {
                return raw.GetValue(value)!;
            }
        }

        return value;
    }

    private static unsafe ulong Address(IGraphicDevice device, IGraphicsBuffer<uint> buffer)
    {
        device = (IGraphicDevice)NativeIdentity(device);
        buffer = (IGraphicsBuffer<uint>)NativeIdentity(buffer);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var api = (Silk.NET.Vulkan.Vk)device.GetType().GetProperty("Api", flags)!.GetValue(device)!;
        var nativeDevice = (Silk.NET.Vulkan.Device)device.GetType().GetProperty("NativeDevice", flags)!.GetValue(device)!;

        // Query the actual native handle independently of the cached ShaderAddress property.
        var nativeBuffer = (Silk.NET.Vulkan.Buffer)buffer.GetType().GetProperty("Native", flags)!.GetValue(buffer)!;
        var info = new Silk.NET.Vulkan.BufferDeviceAddressInfo { SType = Silk.NET.Vulkan.StructureType.BufferDeviceAddressInfo, Buffer = nativeBuffer };
        return api.GetBufferDeviceAddress(nativeDevice, &info);
    }
}
