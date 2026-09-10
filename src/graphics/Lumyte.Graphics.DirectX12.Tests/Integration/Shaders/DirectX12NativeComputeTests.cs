using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
public sealed partial class DirectX12NativeComputeTests
{
    private static readonly Lazy<byte[]> RootShader = new(() => Compile("""
        cbuffer Root : register(b0) {
            uint outputIndex : packoffset(c0.x);
            uint outputOffset : packoffset(c0.y);
            uint value : packoffset(c0.z);
            uint tail : packoffset(c15.w);
        };
        [numthreads(1, 1, 1)]
        void computeMain(uint3 group : SV_GroupID) {
            RWByteAddressBuffer output = ResourceDescriptorHeap[outputIndex];
            output.Store(outputOffset + group.x * 4, value + tail + group.x);
        }
        """));

    private static readonly Lazy<byte[]> IndirectWriterShader = new(() => Compile("""
        cbuffer Root : register(b0) { uint descriptor; uint offset; uint x; uint y; uint z; };
        [numthreads(1, 1, 1)]
        void computeMain() {
            RWByteAddressBuffer args = ResourceDescriptorHeap[descriptor];
            args.Store3(offset, uint3(x, y, z));
        }
        """));

    private static readonly Lazy<byte[]> GroupShader = new(() => Compile("""
        cbuffer Root : register(b0) { uint descriptor; uint offset; uint value; uint countX; uint countY; };
        [numthreads(1, 1, 1)]
        void computeMain(uint3 group : SV_GroupID) {
            RWByteAddressBuffer output = ResourceDescriptorHeap[descriptor];
            uint index = group.x + countX * (group.y + countY * group.z);
            output.Store(offset + index * 4, value + index);
        }
        """));

    private static readonly Lazy<byte[]> MixedShader = new(() => Compile("""
        cbuffer Root : register(b0) {
            uint inputIndex; uint outputIndex; uint textureIndex; uint samplerIndex;
            uint storageIndex; uint addend;
        };
        [numthreads(1, 1, 1)]
        void computeMain() {
            ByteAddressBuffer input = ResourceDescriptorHeap[inputIndex];
            RWByteAddressBuffer output = ResourceDescriptorHeap[outputIndex];
            Texture2D<float4> sampled = ResourceDescriptorHeap[textureIndex];
            SamplerState sampler = SamplerDescriptorHeap[samplerIndex];
            RWTexture2D<float4> storage = ResourceDescriptorHeap[storageIndex];
            float4 color = sampled.SampleLevel(sampler, float2(0.75, 0.25), 0);
            output.Store(0, input.Load(0) + (uint)round(color.r * 255) + addend);
            storage[uint2(0, 0)] = color;
        }
        """));

    [Fact]
    public void DispatchCopiesTheFullRootAtEachCall()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using var output = new Region(backend, NativeGpuMemoryKind.GpuOnly);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        using var descriptors = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Resource, 8);
        using var samplers = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Sampler, 1);
        byte[] callerCode = RootShader.Value.ToArray();
        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(Program(callerCode));
        Array.Clear(callerCode);
        try
        {
            backend.WriteBufferDescriptor(descriptors.Value, 5, new(output.Value, 512, 256), NativeGpuBufferAccess.ReadWrite);
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            // Selecting the heap after the pipeline must still produce a valid direct-index ABI.
            commands.SetComputePipeline(pipeline);
            commands.SetResourceDescriptorHeap(descriptors.Value);
            commands.SetSamplerDescriptorHeap(samplers.Value);
            var root = new uint[64];
            root[0] = 5; root[1] = 0; root[2] = 17; root[63] = 1000;
            commands.Dispatch(MemoryMarshal.AsBytes(root.AsSpan()), 3);
            root[1] = 64; root[2] = 37; root[63] = 2000;
            commands.Dispatch(MemoryMarshal.AsBytes(root.AsSpan()), 3);
            Array.Fill(root, uint.MaxValue);
            ReadOutput(commands, output, readback, 512, 80);

            Submit(backend, commands);

            Assert.Equal(new uint[] { 1017, 1018, 1019 }, ReadWords(readback.Value.CpuAddress, 3));
            Assert.Equal(new uint[] { 2037, 2038, 2039 }, ReadWords(readback.Value.CpuAddress + 64, 3));
        }
        finally { backend.DestroyComputePipeline(pipeline); }
    }

    [Fact]
    public void DispatchIndirectReadsAllCountsWrittenByTheGpu()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using var data = new Region(backend, NativeGpuMemoryKind.GpuOnly);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        using var descriptors = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Resource, 9);
        using var samplers = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Sampler, 1);
        NativeGpuComputePipelineHandle writer = backend.CreateComputePipeline(Program(IndirectWriterShader.Value));
        NativeGpuComputePipelineHandle consumer = backend.CreateComputePipeline(Program(GroupShader.Value));
        try
        {
            backend.WriteBufferDescriptor(descriptors.Value, 7, new(data.Value, 256, 256), NativeGpuBufferAccess.ReadWrite);
            backend.WriteBufferDescriptor(descriptors.Value, 3, new(data.Value, 1024, 256), NativeGpuBufferAccess.ReadWrite);
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.SetResourceDescriptorHeap(descriptors.Value);
            commands.SetSamplerDescriptorHeap(samplers.Value);
            commands.SetComputePipeline(writer);
            commands.Dispatch(Words(7, 128, 3, 2, 2), 1);
            commands.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite,
                GpuStage.DrawIndirect | GpuStage.ComputeShader, GpuAccess.IndirectRead | GpuAccess.ShaderWrite);
            commands.SetComputePipeline(consumer);
            byte[] root = Words(3, 0, 91, 3, 2);
            commands.DispatchIndirect(root, new(data.Value, 384, 12));
            Array.Fill(root, byte.MaxValue);
            ReadOutput(commands, data, readback, 1024, 48);

            Submit(backend, commands);

            Assert.Equal(Enumerable.Range(91, 12).Select(value => (uint)value), ReadWords(readback.Value.CpuAddress, 12));
        }
        finally { backend.DestroyComputePipeline(consumer); backend.DestroyComputePipeline(writer); }
    }

    [Fact]
    public void OneResourceHeapSuppliesBuffersAndTexturesWithIndependentSamplers()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using var upload = new Region(backend, NativeGpuMemoryKind.CpuVisible);
        using var output = new Region(backend, NativeGpuMemoryKind.GpuOnly);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        using var sampled = new Texture(backend, NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.CopyDestination);
        using var storage = new Texture(backend, NativeGpuTextureUsage.Storage | NativeGpuTextureUsage.CopySource);
        using var resources = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Resource, 9);
        using var samplers = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Sampler, 4);
        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(Program(MixedShader.Value));
        try
        {
            Marshal.WriteInt32(upload.Value.CpuAddress + 256, 1100);
            Marshal.Copy(new byte[] { 10, 20, 30, 255, 70, 80, 90, 255 }, 0, upload.Value.CpuAddress + 512, 8);
            Marshal.Copy(new byte[] { 120, 130, 140, 255, 180, 190, 200, 255 }, 0, upload.Value.CpuAddress + 768, 8);
            backend.WriteBufferDescriptor(resources.Value, 1, new(upload.Value, 256, 4), NativeGpuBufferAccess.ReadOnly);
            backend.WriteBufferDescriptor(resources.Value, 3, new(output.Value, 512, 16), NativeGpuBufferAccess.ReadWrite);
            backend.WriteTextureDescriptor(resources.Value, 5, sampled.View);
            backend.WriteTextureDescriptor(resources.Value, 7, storage.View, NativeGpuTextureDescriptorType.Storage);
            backend.WriteSamplerDescriptor(samplers.Value, 2, new(MinFilter: NativeGpuSamplerFilter.Nearest,
                MagFilter: NativeGpuSamplerFilter.Nearest, MipFilter: NativeGpuSamplerFilter.Nearest));
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.DiscardTexture(sampled.View, GpuTextureLayout.CopyDestination);
            commands.CopyMemoryToTexture(new(upload.Value, 512, 512), sampled.Value, TextureFootprint);
            commands.TextureTransition(sampled.View, GpuTextureLayout.CopyDestination, GpuTextureLayout.ShaderRead);
            commands.DiscardTexture(storage.View, GpuTextureLayout.General);
            commands.SetComputePipeline(pipeline);
            commands.SetSamplerDescriptorHeap(samplers.Value);
            commands.SetResourceDescriptorHeap(resources.Value);
            commands.Dispatch(Words(1, 3, 5, 2, 7, 23), 1);
            commands.TextureTransition(storage.View, GpuTextureLayout.General, GpuTextureLayout.CopySource);
            commands.CopyTextureToMemory(storage.Value, new(readback.Value, 512, 512), TextureFootprint);
            ReadOutput(commands, output, readback, 512, 4);

            Submit(backend, commands);

            Assert.Equal(1193u, ReadWords(readback.Value.CpuAddress, 1)[0]);
            byte[] pixel = new byte[4];
            Marshal.Copy(readback.Value.CpuAddress + 512, pixel, 0, 4);
            Assert.Equal(new byte[] { 70, 80, 90, 255 }, pixel);
        }
        finally { backend.DestroyComputePipeline(pipeline); }
    }

    private static readonly NativeGpuTextureCopyFootprint TextureFootprint =
        new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(2, 2, 1), 256, 512);

    private static NativeGpuShaderProgram Program(byte[] code) => new(new NativeGpuShaderCode
    {
        Stage = GpuShaderStage.Compute, Code = code, EntryPoint = "computeMain",
    });

    private static byte[] Words(params uint[] values) => MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

    private static uint[] ReadWords(nint address, int count)
    {
        var bytes = new byte[count * 4];
        Marshal.Copy(address, bytes, 0, bytes.Length);
        return MemoryMarshal.Cast<byte, uint>(bytes).ToArray();
    }

    private static void ReadOutput(NativeGpuCommandBuffer commands, Region source, Region destination, ulong offset, ulong size)
    {
        commands.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.Copy, GpuAccess.CopyRead);
        commands.CopyMemory(new(source.Value, offset, size), new(destination.Value, 0, size));
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
    }

    private static void Submit(DirectX12Backend backend, NativeGpuCommandBuffer commands)
    {
        using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);
        backend.MainQueue.Submit([commands], completion, 1);
        commands.Dispose();
        backend.MainQueue.Wait(completion, 1);
    }

    private static byte[] Compile(string source, string profile = "cs_6_6")
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lumyte-native-dxc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "shader.hlsl");
            string output = Path.Combine(directory, "shader.dxil");
            File.WriteAllText(input, source);
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "dxc.exe"))
            {
                RedirectStandardError = true, RedirectStandardOutput = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            foreach (string argument in new[] { "-T", profile, "-E", "computeMain", "-Fo", output, input })
            {
                start.ArgumentList.Add(argument);
            }
            using Process compiler = Process.Start(start) ?? throw new InvalidOperationException("DXC could not be started.");
            Task<string> errors = compiler.StandardError.ReadToEndAsync();
            Task<string> messages = compiler.StandardOutput.ReadToEndAsync();
            compiler.WaitForExit();
            if (compiler.ExitCode != 0) { throw new InvalidOperationException($"DXC failed: {errors.Result} {messages.Result}"); }
            return File.ReadAllBytes(output);
        }
        finally
        {
            // The freshly allocated temporary directory is the only recursive-delete target.
            string resolved = Path.GetFullPath(directory);
            string temporary = Path.GetFullPath(Path.GetTempPath());
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Compiler output escaped the temporary directory.");
            }
            Directory.Delete(resolved, true);
        }
    }

    private sealed class Region : IDisposable
    {
        private readonly DirectX12Backend backend;
        private readonly NativeGpuHeap heap;
        public NativeGpuLinearRegion Value { get; }
        public Region(DirectX12Backend backend, NativeGpuMemoryKind kind)
        {
            this.backend = backend;
            NativeGpuMemoryRequirements requirements = backend.GetLinearMemoryRequirements(4096, kind);
            heap = backend.CreateGpuHeap(requirements.Size * 2, requirements.Alignment, kind, [requirements.Compatibility]);
            Value = backend.CreateLinearRegion(4096, heap, requirements.Size);
        }
        public void Dispose() { backend.DestroyLinearRegion(Value); backend.DestroyGpuHeap(heap); }
    }

    private sealed class DescriptorHeap : IDisposable
    {
        private readonly DirectX12Backend backend;
        public NativeGpuDescriptorHeap Value { get; }
        public DescriptorHeap(DirectX12Backend backend, NativeGpuDescriptorHeapKind kind, uint capacity)
        { this.backend = backend; Value = backend.CreateDescriptorHeap(kind, capacity); }
        public void Dispose() => backend.DestroyDescriptorHeap(Value);
    }

    private sealed class Texture : IDisposable
    {
        private readonly DirectX12Backend backend;
        private readonly NativeGpuHeap heap;
        public NativeGpuTextureHandle Value { get; }
        public NativeGpuTextureView View { get; }
        public Texture(DirectX12Backend backend, NativeGpuTextureUsage usage)
        {
            this.backend = backend;
            var description = new NativeGpuTextureDescription(NativeGpuTextureDimension.TwoD,
                2, 2, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, usage);
            NativeGpuMemoryRequirements requirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
            heap = backend.CreateGpuHeap(requirements.Size * 2, requirements.Alignment,
                NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
            Value = backend.CreateTexture(description, heap, requirements.Size);
            View = new(Value, NativeGpuTextureViewDimension.TwoD, description.Format,
                NativeGpuTextureAspect.Color, 0, 1, 0, 1);
        }
        public void Dispose() { backend.DestroyTexture(Value); backend.DestroyGpuHeap(heap); }
    }
}
