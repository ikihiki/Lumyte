using System.Buffers.Binary;
using Lumyte.Samples;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Validates logical argument tables and automatic resource lowering through the common API.</summary>
public sealed class BindingTests
{
    /// <summary>Verifies a single material resolves five textures sharing one independently registered sampler.</summary>
    [Fact]
    public void FiveTexturesShareOneSamplerInOneMaterialDraw()
    {
        using var resources = new BindingTestResources();
        GraphicsDevice device = resources.Own(Graphics.CreateDevice());
        var views = new IGraphicsTextureView[5];
        for (int i = 0; i < views.Length; i++)
        {
            views[i] = CreateTexture(device, resources, new byte[] { 255, 0, 0, 255 });
        }

        Sampler sampler = resources.Own(device.CreateSampler(new SamplerDesc()));
        IArgumentTable table = resources.Own(device.CreateArgumentTable(new ArgumentTableDesc { TextureCapacity = 20, SamplerCapacity = 1 }));
        IGpuRef<IGraphicsTextureView>[] textures = views.Select((view, index) => table.WriteTexture((uint)index, view)).ToArray();
        IGpuRef<Sampler> samplerReference = table.WriteSampler(0, sampler);
        ShaderModule shader = resources.Own(device.CreateShader(typeof(BindingTests).Assembly, "Lumyte.Shaders.textureMaterial.wgsl"));
        GraphicsPipeline pipeline = resources.Own(device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader }));
        IGraphicsBuffer<byte> gpu = UploadData(device, resources, shader, new TextureMaterial[] { new(textures, samplerReference) }, TextureSerializer());
        ShaderArguments arguments = resources.Own(pipeline.CreateArguments(RegisterRoot<TextureMaterial>(device, resources, shader, gpu.Slice(0, gpu.Count))));
        AssertPixels(Render(device, resources, pipeline, arguments), new byte[] { 255, 0, 0, 255 });
    }

    /// <summary>Verifies logical registration capacity is separate from per-draw capacity and root element selection.</summary>
    [Fact]
    public void CandidateArrayExceedsCapacityButEachRootElementCanDraw()
    {
        using var resources = new BindingTestResources();
        GraphicsDevice device = resources.Own(Graphics.CreateDevice());
        var views = new IGraphicsTextureView[9];
        for (int i = 0; i < views.Length; i++)
        {
            views[i] = CreateTexture(device, resources, new byte[] { (byte)(i * 20), 0, 0, 255 });
        }

        Sampler sampler = resources.Own(device.CreateSampler(new SamplerDesc()));
        IArgumentTable table = resources.Own(device.CreateArgumentTable(new ArgumentTableDesc { TextureCapacity = 9, SamplerCapacity = 1 }));
        IGpuRef<Sampler> samplerReference = table.WriteSampler(0, sampler);
        MaterialData[] materials = views.Select((view, index) => new MaterialData(System.Numerics.Vector4.One, table.WriteTexture((uint)index, view), samplerReference)).ToArray();
        ShaderModule shader = resources.Own(device.CreateShader(typeof(BindingTests).Assembly, "Lumyte.Shaders.material.wgsl"));
        GraphicsPipeline pipeline = resources.Own(device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader }));
        IGraphicsBuffer<byte> gpu = UploadData(device, resources, shader, materials, MaterialDataSerializer.Instance);
        IGpuRef<MaterialData> root = RegisterRoot<MaterialData>(device, resources, shader, gpu.Slice(0, gpu.Count));
        Assert.Equal(9UL, root.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => root.GetElement(9));
        Assert.Throws<NotSupportedException>(() => pipeline.CreateArguments(root));
        for (int i = 0; i < materials.Length; i++)
        {
            using ShaderArguments arguments = pipeline.CreateArguments(root.GetElement((ulong)i));
            AssertPixels(Render(device, resources, pipeline, arguments), new byte[] { (byte)(i * 20), 0, 0, 255 }, leftOnly: true);
        }

        Assert.Throws<ArgumentException>(() => RegisterRoot<CustomMaterial>(device, resources, shader, gpu.Slice(0, 32)));
    }

    /// <summary>Verifies alias registrations across argument tables do not consume distinct physical resource slots.</summary>
    [Fact]
    public void MultipleTablesWithSameSlotDeduplicateSharedResources()
    {
        using var resources = new BindingTestResources();
        GraphicsDevice device = resources.Own(Graphics.CreateDevice());
        IGraphicsTextureView red = CreateTexture(device, resources, new byte[] { 255, 0, 0, 255 });
        IGraphicsTextureView green = CreateTexture(device, resources, new byte[] { 0, 255, 0, 255 });
        Sampler sampler = resources.Own(device.CreateSampler(new SamplerDesc()));
        var materials = new MaterialData[10];
        for (int i = 0; i < materials.Length; i++)
        {
            IArgumentTable table = resources.Own(device.CreateArgumentTable(new ArgumentTableDesc { TextureCapacity = 1, SamplerCapacity = 1 }));
            materials[i] = new(System.Numerics.Vector4.One, table.WriteTexture(0, i % 2 == 0 ? red : green), table.WriteSampler(0, sampler));
        }

        ShaderModule shader = resources.Own(device.CreateShader(typeof(BindingTests).Assembly, "Lumyte.Shaders.material.wgsl"));
        GraphicsPipeline pipeline = resources.Own(device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader }));
        IGraphicsBuffer<byte> gpu = UploadData(device, resources, shader, materials, MaterialDataSerializer.Instance);
        ShaderArguments arguments = resources.Own(pipeline.CreateArguments(RegisterRoot<MaterialData>(device, resources, shader, gpu.Slice(0, gpu.Count))));
        byte[] pixels = Render(device, resources, pipeline, arguments);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.AsSpan(0, 4).ToArray());
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels.AsSpan(4 * 4, 4).ToArray());
    }

    /// <summary>Verifies one draw automatically resolves a read-only buffer slice alongside texture and sampler descriptors.</summary>
    [Fact]
    public void ShaderDataTracksIndependentBufferDescriptorWithLogicalOffset()
    {
        using var resources = new BindingTestResources();
        GraphicsDevice device = resources.Own(Graphics.CreateDevice());
        IGraphicsTextureView texture = CreateTexture(device, resources, new byte[] { 255, 0, 0, 255 });
        Sampler sampler = resources.Own(device.CreateSampler(new SamplerDesc()));
        IGraphicsBuffer<uint> factors = resources.Own(device.CreateBuffer(new BufferDesc<uint> { Count = 2, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }));
        using (IGraphicsBuffer<uint> upload = device.CreateBuffer(new BufferDesc<uint> { Count = 2, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }))
        {
            upload.CopyFrom(new uint[] { 0, 128 });
            using CommandEncoder encoder = device.CreateCommandEncoder();
            encoder.RecordCopyBuffer(upload.Slice(0, 2), factors.Slice(0, 2));
            using CommandBuffer commands = encoder.Finish();
            device.Submit(commands).Wait();
        }

        IArgumentTable table = resources.Own(device.CreateArgumentTable(new ArgumentTableDesc { TextureCapacity = 1, SamplerCapacity = 1, BufferCapacity = 1 }));
        var value = new BufferMaterial(table.WriteTexture(0, texture), table.WriteSampler(0, sampler), table.WriteBuffer(0, factors.Slice(0, 2)).GetElement(1));
        ShaderModule shader = resources.Own(device.CreateShader(typeof(BindingTests).Assembly, "Lumyte.Shaders.bufferMaterial.wgsl"));
        GraphicsPipeline pipeline = resources.Own(device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader }));
        var serializer = new DelegateShaderDataSerializer<BufferMaterial>((data, writer) =>
        {
            writer.WriteTextureReference("texture", data.Texture);
            writer.WriteSamplerReference("sampler", data.Sampler);
            writer.WriteBufferReference("factor", data.Factor);
        });
        IGraphicsBuffer<byte> gpu = UploadData(device, resources, shader, new BufferMaterial[] { value }, serializer);
        ShaderArguments arguments = resources.Own(pipeline.CreateArguments(RegisterRoot<BufferMaterial>(device, resources, shader, gpu.Slice(0, gpu.Count))));
        AssertPixels(Render(device, resources, pipeline, arguments), new byte[] { 128, 0, 0, 128 });
        Assert.Throws<InvalidOperationException>(() => table.ReleaseBuffer(0));
        Assert.Throws<InvalidOperationException>(factors.Dispose);
    }

    /// <summary>Verifies element-aligned partial copies preserve dependencies and raw writes invalidate only touched elements.</summary>
    [Fact]
    public void ElementCopiesPreserveDependenciesAndPartialWritesInvalidateTouchedElement()
    {
        using var resources = new BindingTestResources();
        GraphicsDevice device = resources.Own(Graphics.CreateDevice());
        IGraphicsTextureView texture = CreateTexture(device, resources, new byte[] { 255, 0, 0, 255 });
        Sampler sampler = resources.Own(device.CreateSampler(new SamplerDesc()));
        IArgumentTable table = resources.Own(device.CreateArgumentTable(new ArgumentTableDesc { TextureCapacity = 1, SamplerCapacity = 1 }));
        IGpuRef<IGraphicsTextureView> image = table.WriteTexture(0, texture);
        IGpuRef<Sampler> sampling = table.WriteSampler(0, sampler);
        ShaderModule shader = resources.Own(device.CreateShader(typeof(BindingTests).Assembly, "Lumyte.Shaders.material.wgsl"));
        GraphicsPipeline pipeline = resources.Own(device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader }));
        var values = new MaterialData[] { new(System.Numerics.Vector4.One, image, sampling), new(new System.Numerics.Vector4(0.5f, 1, 1, 1), image, sampling) };
        IGraphicsBuffer<byte> gpu = UploadData(device, resources, shader, values, MaterialDataSerializer.Instance);
        IGpuRef<MaterialData> root = RegisterRoot<MaterialData>(device, resources, shader, gpu.Slice(0, 64));
        IGpuRef<MaterialData> second = root.GetElement(1);
        IGraphicsBuffer<byte> destination = resources.Own(device.CreateBuffer(new BufferDesc<byte> { Count = 32, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }));
        IGraphicsBuffer<byte> upload = resources.Own(device.CreateBuffer(new BufferDesc<byte> { Count = 64, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }));
        upload.Slice(0, 64).CopyFrom<MaterialData>(values, shader.GetDataLayout<MaterialData>(), MaterialDataSerializer.Instance);
        using (CommandEncoder encoder = device.CreateCommandEncoder())
        {
            encoder.RecordCopyBuffer(upload.Slice(32, 32), destination.Slice(0, 32));
            using CommandBuffer commands = encoder.Finish();
            device.Submit(commands).Wait();
        }

        using (ShaderArguments arguments = pipeline.CreateArguments(RegisterRoot<MaterialData>(device, resources, shader, destination.Slice(0, 32))))
        {
            AssertPixels(Render(device, resources, pipeline, arguments), new byte[] { 128, 0, 0, 255 }, leftOnly: true);
        }

        using (IGraphicsBuffer<byte> raw = device.CreateBuffer(new BufferDesc<byte> { Count = 4, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }))
        {
            raw.CopyFrom(new byte[4]);
            using CommandEncoder encoder = device.CreateCommandEncoder();
            encoder.RecordCopyBuffer(raw.Slice(0, 4), gpu.Slice(0, 4));
            using CommandBuffer commands = encoder.Finish();
            device.Submit(commands).Wait();
        }

        Assert.Throws<ArgumentException>(() => RegisterRoot<MaterialData>(device, resources, shader, gpu.Slice(0, 32)));
        Assert.Throws<ArgumentException>(() => root.GetElement(0));
        using ShaderArguments preserved = pipeline.CreateArguments(root.GetElement(1));
        AssertPixels(Render(device, resources, pipeline, preserved), new byte[] { 128, 0, 0, 255 }, leftOnly: true);
    }

    /// <summary>Verifies element selection uses range-relative indices and shares the table slot's generation.</summary>
    [Fact]
    public void BufferElementReferencesShareRegistrationGeneration()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using IGraphicsBuffer<uint> buffer = device.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.ShaderRead });
        using IArgumentTable table = device.CreateArgumentTable(new ArgumentTableDesc { BufferCapacity = 1 });
        IGpuRef<uint> range = table.WriteBuffer(0, buffer.Slice(1, 2));
        IGpuRef<uint> element = range.GetElement(1);
        Assert.Equal(2UL, range.Count);
        Assert.Equal(1UL, element.Count);
        Assert.Same(element, element.GetElement(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => range.GetElement(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => element.GetElement(1));
        IGpuRef<uint> replacement = table.WriteBuffer(0, buffer.Slice(0, 4));
        Assert.Throws<ObjectDisposedException>(() => range.GetElement(0));
        Assert.Throws<ObjectDisposedException>(() => element.GetElement(0));
        IGpuRef<uint> fresh = replacement.GetElement(3);
        table.ReleaseBuffer(0);
        Assert.Throws<ObjectDisposedException>(() => fresh.GetElement(0));
        Assert.Throws<ObjectDisposedException>(() => replacement.GetElement(0));
    }

    private static IGpuRef<T> RegisterRoot<T>(GraphicsDevice device, BindingTestResources resources, ShaderModule shader, BufferSlice<byte> range)
    {
        IArgumentTable table = resources.Own(device.CreateArgumentTable(new ArgumentTableDesc { BufferCapacity = 1 }));
        return table.WriteBuffer(0, range, shader.GetDataLayout<T>());
    }

    private static DelegateShaderDataSerializer<TextureMaterial> TextureSerializer() => new((value, writer) =>
    {
        for (int i = 0; i < value.Textures.Length; i++)
        {
            writer.WriteTextureReference($"texture{i}", value.Textures[i]);
        }

        writer.WriteSamplerReference("sampler", value.Sampler);
    });

    private static IGraphicsTextureView CreateTexture(GraphicsDevice device, BindingTestResources resources, byte[] color)
    {
        IGraphicsTexture texture = resources.Own(device.CreateTexture(new TextureDesc { Width = 1, Height = 1, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination }));
        IGraphicsTextureView view = resources.Own(texture.CreateView());
        using IGraphicsBuffer<byte> upload = device.CreateBuffer(new BufferDesc<byte> { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        upload.CopyFrom(color);
        using CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBufferToTexture(upload.Slice(0, 256), texture, 256);
        using CommandBuffer commands = encoder.Finish();
        device.Submit(commands).Wait();
        return view;
    }

    private static IGraphicsBuffer<byte> UploadData<T>(GraphicsDevice device, BindingTestResources resources, ShaderModule shader, T[] values, IShaderDataSerializer<T> serializer)
    {
        ShaderDataLayout<T> layout = shader.GetDataLayout<T>();
        ulong size = layout.GetSizeInBytes((ulong)values.Length);
        IGraphicsBuffer<byte> upload = resources.Own(device.CreateBuffer(new BufferDesc<byte> { Count = size, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }));
        IGraphicsBuffer<byte> gpu = resources.Own(device.CreateBuffer(new BufferDesc<byte> { Count = size, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }));
        upload.Slice(0, size).CopyFrom<T>(values, layout, serializer);
        using CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(upload.Slice(0, size), gpu.Slice(0, size));
        using CommandBuffer commands = encoder.Finish();
        device.Submit(commands).Wait();
        return gpu;
    }

    private static byte[] Render(GraphicsDevice device, BindingTestResources resources, GraphicsPipeline pipeline, ShaderArguments arguments)
    {
        IGraphicsTexture target = resources.Own(device.CreateTexture(new TextureDesc { Width = 8, Height = 4 }));
        IGraphicsTextureView view = resources.Own(target.CreateView());
        IGraphicsBuffer<byte> readback = resources.Own(device.CreateBuffer(new BufferDesc<byte> { Count = 1024, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }));
        using CommandEncoder encoder = device.CreateCommandEncoder();
        using (RenderEncoder render = encoder.BeginRenderPass(new RenderPassDesc { Target = view }))
        {
            render.SetPipeline(pipeline);
            render.Draw(arguments, new DrawDesc { VertexCount = 3 });
        }

        encoder.RecordCopyTextureToBuffer(target, readback, 256);
        using CommandBuffer commands = encoder.Finish();
        device.Submit(commands).Wait();
        byte[] pixels = new byte[1024];
        readback.CopyTo(pixels);
        return pixels;
    }

    private static void AssertPixels(byte[] pixels, byte[] expected, bool leftOnly = false)
    {
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < (leftOnly ? 4 : 8); x++)
            {
                Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(expected), BinaryPrimitives.ReadUInt32LittleEndian(pixels.AsSpan((y * 256) + (x * 4), 4)));
            }
        }
    }
}
