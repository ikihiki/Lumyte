using System.Buffers.Binary;
using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
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

    /// <summary>WGSL stores stable slots, relative subranges, exact counts, and reserved zeros.</summary>
    /// <returns>The native checks with validation disabled and enabled.</returns>
    [GpuFact]
    public async Task WgpuNativeBytesAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.Wgsl);
        foreach (bool validation in new[] { false, true })
        {
            using var device = WgpuDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await CheckAsync(device, artifact, ShaderTarget.Wgsl);
        }
    }

    /// <summary>Vulkan stores actual native addresses plus subranges and exact counts.</summary>
    /// <returns>The native checks with validation disabled and enabled.</returns>
    [GpuFact]
    public async Task VulkanNativeBytesAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.SpirV);
        foreach (bool validation in new[] { false, true })
        {
            using var device = VulkanDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await CheckAsync(device, artifact, ShaderTarget.SpirV);
            await CheckAddressLifetimeAsync(device);
        }
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

    private static async Task CheckAsync(IGraphicDevice device, ShaderArtifact artifact, ShaderTarget target)
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
        upload.CopyFrom([value]);
        upload.CopyFrom([value]); // Repeated physical references reuse the same allocation address.
        upload.Unmap();

        // Diagnostic reflection exposes only the existing raw storage, never a new production API.
        IGraphicsBuffer<byte> raw = Assert.IsAssignableFrom<IGraphicsBuffer<byte>>(upload.GetType().GetProperty("Storage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(upload));
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = 88, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = raw.SliceElements(0, 88), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(raw.SliceElements(0, 88), readback.SliceElements(0, 88));
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, 88), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] actual = new byte[88];
        readback.CopyTo(actual);
        readback.Unmap();

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

        Assert.Equal(expected, actual);
        await CheckMalformedAsync(device, artifact, value);
    }

    private static async Task CheckMalformedAsync(IGraphicDevice device, ShaderArtifact artifact, NativeAbiValue value)
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
                IShaderReferenceMetadata before = metadata.ReadReferences(0);
                IShaderReferenceMetadata after = metadata.ReadReferences(4);
                var malformed = new ManualBadAbiValue(value, mode);
                if (mode == 1)
                {
                    Assert.Throws<InvalidOperationException>(() => upload.CopyFrom([valid, malformed, valid], 1));
                }
                else
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => upload.CopyFrom([valid, malformed, valid], 1));
                }

                Assert.Same(before, metadata.ReadReferences(0));
                Assert.Same(after, metadata.ReadReferences(4));
                for (ulong index = 1; index <= 3; index++)
                {
                    ulong invalidIndex = index;
                    Assert.Throws<InvalidOperationException>(() => metadata.ReadReferences(invalidIndex));
                }
            }
        }
        finally
        {
            upload.Unmap();
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

    private static unsafe ulong Address(IGraphicDevice device, IGraphicsBuffer<uint> buffer)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var api = (Silk.NET.Vulkan.Vk)device.GetType().GetProperty("Api", flags)!.GetValue(device)!;
        var nativeDevice = (Silk.NET.Vulkan.Device)device.GetType().GetProperty("NativeDevice", flags)!.GetValue(device)!;

        // Query the actual native handle independently of the cached ShaderAddress property.
        var nativeBuffer = (Silk.NET.Vulkan.Buffer)buffer.GetType().GetProperty("Native", flags)!.GetValue(buffer)!;
        var info = new Silk.NET.Vulkan.BufferDeviceAddressInfo { SType = Silk.NET.Vulkan.StructureType.BufferDeviceAddressInfo, Buffer = nativeBuffer };
        return api.GetBufferDeviceAddress(nativeDevice, &info);
    }
}
