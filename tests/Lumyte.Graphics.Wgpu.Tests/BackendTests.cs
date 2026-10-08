using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.Samples;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>
/// Validates graphics behavior through the common API on a working GPU adapter.
/// </summary>
public sealed class BackendTests
{
    /// <summary>
    /// Verifies twenty distinct textures fill a five-by-four quad grid, including all shared edges and batch boundaries.
    /// </summary>
    [Fact]
    public void TwentyTexturedSquaresFillSceneWithTwentyDistinctColors()
    {
        Assert.Equal(20, TwentySquaresScene.SquareCount);
        Assert.Equal(20, TwentySquaresScene.Colors.ToArray().Distinct().Count());
        using GraphicsDevice device = Graphics.CreateDevice();
        byte[] pixels = TwentySquaresScene.Run(device, typeof(BackendTests).Assembly);
        TwentySquaresScene.Verify(pixels);
        var observed = new HashSet<uint>();
        for (int y = 0; y < TwentySquaresScene.Height; y++)
        {
            for (int x = 0; x < TwentySquaresScene.Width; x++)
            {
                int offset = checked((int)((y * TwentySquaresScene.BytesPerRow) + (x * 4)));
                observed.Add(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(pixels.AsSpan(offset, 4)));
            }
        }

        Assert.Equal(20, observed.Count);

        // A missing or corrupt square must fail the same complete-pixel validation used by the sample.
        pixels[0] ^= 0xff;
        Assert.Throws<InvalidOperationException>(() => TwentySquaresScene.Verify(pixels));
    }

    /// <summary>
    /// Verifies caller-owned logical and Slang wire types with a different stride can resolve textures on the GPU.
    /// </summary>
    [Fact]
    public void CallerDefinedMaterialSchemaUsesReflectedStrideAndGpuSampling()
    {
        Assert.DoesNotContain(typeof(GraphicsDevice).Assembly.GetTypes(), type => type.Name == "MaterialData");
        using GraphicsDevice device = Graphics.CreateDevice();
        using IGraphicsTexture red = device.CreateTexture(new TextureDesc { Width = 1, Height = 1, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTexture green = device.CreateTexture(new TextureDesc { Width = 1, Height = 1, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView redView = red.CreateView();
        using IGraphicsTextureView greenView = green.CreateView();
        using Sampler sampler = device.CreateSampler(new SamplerDesc { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest });
        SampledTexture2DReference redRef = device.CreateSampledTexture2DReference(redView, sampler);
        SampledTexture2DReference greenRef = device.CreateSampledTexture2DReference(greenView, sampler);
        using ShaderModule shader = device.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.customMaterial.wgsl");
        using GraphicsPipeline pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader });
        MaterialResourceLayout layout = shader.GetMaterialResourceLayout();
        Assert.Equal(48UL, layout.ElementStrideInBytes);
        Assert.Equal(96UL, layout.GetSizeInBytes(2));
        IShaderDataWriter? capturedWriter = null;
        var serializer = new DelegateShaderDataSerializer<CustomMaterial>((value, writer) =>
        {
            writer.Write("opacity", value.Opacity);
            writer.Write("options", 0u);
            writer.WriteSampledTexture2D("image", value.Texture);
            writer.Write("tint", value.Tint);
            capturedWriter = writer;
        });
        using IGraphicsMaterialBindings bindings = device.CreateMaterialBindings<CustomMaterial>(
            new MaterialBindingsDesc { Layout = layout, UnusedSlotFallback = redRef },
            new CustomMaterial[] { new(0.5f, System.Numerics.Vector4.One, redRef), new(1, System.Numerics.Vector4.One, greenRef) },
            serializer);
        Assert.Equal(96UL, bindings.SizeInBytes);
        Assert.Throws<InvalidOperationException>(() => capturedWriter!.Write("opacity", 1f));
        using IGraphicsBuffer<byte> upload = device.CreateBuffer(new BufferDesc<byte> { Count = bindings.SizeInBytes, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<byte> gpu = device.CreateBuffer(new BufferDesc<byte> { Count = bindings.SizeInBytes, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        using IGraphicsBuffer<byte> redUpload = device.CreateBuffer(new BufferDesc<byte> { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<byte> greenUpload = device.CreateBuffer(new BufferDesc<byte> { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        redUpload.CopyFrom(new byte[] { 255, 0, 0, 255 });
        greenUpload.CopyFrom(new byte[] { 0, 255, 0, 255 });
        upload.Slice(0, bindings.SizeInBytes).CopyFrom(bindings);
        using (CommandEncoder transfer = device.CreateCommandEncoder())
        {
            transfer.RecordCopyBufferToTexture(redUpload.Slice(0, 256), red, 256);
            transfer.RecordCopyBufferToTexture(greenUpload.Slice(0, 256), green, 256);
            transfer.RecordCopyBuffer(upload.Slice(0, bindings.SizeInBytes), gpu.Slice(0, bindings.SizeInBytes));
            using CommandBuffer commands = transfer.Finish();
            device.Submit(commands).Wait();
        }

        using ShaderArguments arguments = pipeline.CreateArguments(device.CreateMaterialReference(gpu.Slice(0, bindings.SizeInBytes)));
        using IGraphicsTexture target = device.CreateTexture(new TextureDesc { Width = 8, Height = 4 });
        using IGraphicsTextureView targetView = target.CreateView();
        using IGraphicsBuffer<byte> readback = Readback<byte>(device, 1024);
        using (CommandEncoder encoder = device.CreateCommandEncoder())
        {
            using (RenderEncoder pass = encoder.BeginRenderPass(new RenderPassDesc { Target = targetView }))
            {
                pass.SetPipeline(pipeline);
                pass.Draw(arguments, new DrawDesc { VertexCount = 3 });
            }

            encoder.RecordCopyTextureToBuffer(target, readback, 256);
            using CommandBuffer commands = encoder.Finish();
            device.Submit(commands).Wait();
        }

        byte[] pixels = new byte[1024];
        readback.CopyTo(pixels);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                byte[] expected = x < 4 ? new byte[] { 128, 0, 0, 128 } : new byte[] { 0, 255, 0, 255 };
                Assert.Equal(expected, pixels.AsSpan((y * 256) + (x * 4), 4).ToArray());
            }
        }
    }

    /// <summary>
    /// Verifies serializer field names, numeric types, resource slots, duplicate writes, and callback lifetime.
    /// </summary>
    [Fact]
    public void CallerSerializerMustMatchReflectedFieldsAndCannotRetainWriter()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using ShaderModule shader = device.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.material.wgsl");
        using IGraphicsTexture texture = device.CreateTexture(new TextureDesc { Width = 1, Height = 1, Usage = TextureUsage.Sampled });
        using IGraphicsTextureView view = texture.CreateView();
        using Sampler sampler = device.CreateSampler(new SamplerDesc());
        var desc = new MaterialBindingsDesc { Layout = shader.GetMaterialResourceLayout(), UnusedSlotFallback = device.CreateSampledTexture2DReference(view, sampler) };
        var values = new MaterialData[] { new(System.Numerics.Vector4.One) };
        Assert.Throws<ArgumentException>(() => device.CreateMaterialBindings<MaterialData>(
            desc,
            values,
            new DelegateShaderDataSerializer<MaterialData>((value, writer) => writer.Write("missing", 1u))));
        Assert.Throws<ArgumentException>(() => device.CreateMaterialBindings<MaterialData>(
            desc,
            values,
            new DelegateShaderDataSerializer<MaterialData>((value, writer) => writer.Write("baseColor", 1u))));
        Assert.Throws<NotSupportedException>(() => device.CreateMaterialBindings<MaterialData>(
            desc,
            values,
            new DelegateShaderDataSerializer<MaterialData>((value, writer) => writer.Write("baseColor", (byte)1))));
        Assert.Throws<ArgumentException>(() => device.CreateMaterialBindings<MaterialData>(
            desc,
            values,
            new DelegateShaderDataSerializer<MaterialData>((value, writer) => writer.WriteSampledTexture2D("baseColor", null))));
        Assert.Throws<ArgumentException>(() => device.CreateMaterialBindings<MaterialData>(
            desc,
            values,
            new DelegateShaderDataSerializer<MaterialData>((value, writer) =>
            {
                writer.Write("hasTexture", 0u);
                writer.Write("hasTexture", 1u);
            })));
        IShaderDataWriter? capturedWriter = null;
        Assert.Throws<ArgumentException>(() => device.CreateMaterialBindings<MaterialData>(
            desc,
            values,
            new DelegateShaderDataSerializer<MaterialData>((value, writer) =>
            {
                capturedWriter = writer;
                writer.Write("baseColor", new System.Numerics.Vector4(float.NaN, 1, 1, 1));
            })));
        Assert.Throws<InvalidOperationException>(() => capturedWriter!.Write("hasTexture", 1u));
        using IGraphicsMaterialBindings valid = device.CreateMaterialBindings<MaterialData>(desc, values, MaterialDataSerializer.Instance);
        Assert.Equal(32UL, valid.SizeInBytes);
    }

    /// <summary>
    /// Verifies gpu material buffer selects textures per pixel in one draw.
    /// </summary>
    /// <param name="swap">Whether to exchange the two sampled texture references.</param>
    /// <param name="untextured">Whether the first material omits its sampled texture.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GpuMaterialBufferSelectsTexturesPerPixelInOneDraw(bool swap, bool untextured)
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        byte[] pixels = Lumyte.Samples.MaterialDrawing.Run(device, typeof(BackendTests).Assembly, swap, untextured);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Assert.Equal(x < 4 ? untextured ? new byte[] { 128, 255, 255, 255 } : swap ? new byte[] { 0, 255, 0, 255 } : new byte[] { 128, 0, 0, 255 } : swap ? new byte[] { 255, 0, 0, 255 } : new byte[] { 0, 255, 0, 255 }, pixels.AsSpan((y * 256) + (x * 4), 4).ToArray());
            }
        }
    }

    /// <summary>
    /// Verifies material bindings validate references capacity and lifetime without hidden copies.
    /// </summary>
    [Fact]
    public void MaterialBindingsValidateReferencesCapacityAndLifetimeWithoutHiddenCopies()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using ShaderModule shader = device.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.material.wgsl");
        using ShaderModule rootless = device.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.triangle.wgsl");
        Assert.Throws<NotSupportedException>(() => rootless.GetMaterialResourceLayout());
        using IGraphicsTexture texture = device.CreateTexture(new TextureDesc { Width = 1, Height = 1, Usage = TextureUsage.Sampled });
        using IGraphicsTextureView v0 = texture.CreateView();
        using IGraphicsTextureView v1 = texture.CreateView();
        using IGraphicsTextureView v2 = texture.CreateView();
        using IGraphicsTextureView v3 = texture.CreateView();
        using IGraphicsTextureView v4 = texture.CreateView();
        using Sampler sampler = device.CreateSampler(new SamplerDesc());
        SampledTexture2DReference fallback = device.CreateSampledTexture2DReference(v0, sampler);
        var desc = new MaterialBindingsDesc
        {
            Layout = shader.GetMaterialResourceLayout(),
            UnusedSlotFallback = fallback,
        };
        Assert.Throws<ArgumentException>(() => device.CreateMaterialBindings<MaterialData>(desc, new MaterialData[] { new(System.Numerics.Vector4.One, default(SampledTexture2DReference)) }, MaterialDataSerializer.Instance));
        Assert.Throws<NotSupportedException>(() => device.CreateMaterialBindings<MaterialData>(desc, new[] { v1, v2, v3, v4 }.Select(v => new MaterialData(System.Numerics.Vector4.One, device.CreateSampledTexture2DReference(v, sampler))).ToArray(), MaterialDataSerializer.Instance));
        using GraphicsDevice foreign = Graphics.CreateDevice();
        Assert.Throws<ArgumentException>(() => foreign.CreateMaterialBindings<MaterialData>(desc, new MaterialData[] { new(System.Numerics.Vector4.One) }, MaterialDataSerializer.Instance));
        using IGraphicsTexture invalid = device.CreateTexture(new TextureDesc { Width = 1, Height = 1 });
        using IGraphicsTextureView invalidView = invalid.CreateView();
        Assert.Throws<ArgumentException>(() => device.CreateSampledTexture2DReference(invalidView, sampler));
        var values = new MaterialData[]
        {
            new(new System.Numerics.Vector4(0.5f, 1, 1, 1)),
        };
        using IGraphicsMaterialBindings bindings = device.CreateMaterialBindings<MaterialData>(desc, values, MaterialDataSerializer.Instance);
        values[0] = new(System.Numerics.Vector4.Zero);
        using IGraphicsBuffer<byte> upload = device.CreateBuffer(new BufferDesc<byte> { Count = 32, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        Assert.Throws<ArgumentException>(() => upload.Slice(0, 16).CopyFrom(bindings));
        upload.Slice(0, 32).CopyFrom(bindings);
        Assert.Throws<InvalidOperationException>(bindings.Dispose);
        Assert.Throws<InvalidOperationException>(v0.Dispose);
        Assert.Throws<InvalidOperationException>(sampler.Dispose);
        using IGraphicsBuffer<byte> readback = Readback<byte>(device, 32);
        using CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(upload.Slice(0, 32), readback.Slice(0, 32));
        using CommandBuffer commands = encoder.Finish();
        device.Submit(commands).Wait();
        byte[] bytes = new byte[32];
        readback.CopyTo(bytes);
        Assert.Equal(0.5f, System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(bytes));
        readback.Dispose();
        upload.Dispose();
        bindings.Dispose();
    }

    /// <summary>
    /// Verifies material upload completion discard and partial overwrite control validity.
    /// </summary>
    [Fact]
    public void MaterialUploadCompletionDiscardAndPartialOverwriteControlValidity()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using ShaderModule shader = device.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.material.wgsl");
        using GraphicsPipeline pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader });
        using IGraphicsTexture texture = device.CreateTexture(new TextureDesc { Width = 1, Height = 1, Usage = TextureUsage.Sampled });
        using IGraphicsTextureView view = texture.CreateView();
        using Sampler sampler = device.CreateSampler(new SamplerDesc());
        using IGraphicsMaterialBindings bindings = device.CreateMaterialBindings<MaterialData>(new MaterialBindingsDesc { Layout = shader.GetMaterialResourceLayout(), UnusedSlotFallback = device.CreateSampledTexture2DReference(view, sampler), }, new MaterialData[] { new(System.Numerics.Vector4.One) }, MaterialDataSerializer.Instance);
        using IGraphicsBuffer<byte> upload = device.CreateBuffer(new BufferDesc<byte> { Count = 32, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<byte> gpu = device.CreateBuffer(new BufferDesc<byte> { Count = 32, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        upload.Slice(0, 32).CopyFrom(bindings);
        using (CommandEncoder discarded = device.CreateCommandEncoder())
        {
            discarded.RecordCopyBuffer(upload.Slice(0, 32), gpu.Slice(0, 32));
            Assert.Throws<ArgumentException>(() => device.CreateMaterialReference(gpu.Slice(0, 32)));
        }

        Assert.Throws<ArgumentException>(() => device.CreateMaterialReference(gpu.Slice(0, 32)));
        using (CommandEncoder encoder = device.CreateCommandEncoder())
        {
            encoder.RecordCopyBuffer(upload.Slice(0, 32), gpu.Slice(0, 32));
            using CommandBuffer commands = encoder.Finish();
            Submission submission = device.Submit(commands);
            Assert.Throws<ArgumentException>(() => device.CreateMaterialReference(gpu.Slice(0, 32)));
            submission.Wait();
        }

        MaterialBufferReference reference = device.CreateMaterialReference(gpu.Slice(0, 32));
        using ShaderArguments arguments = pipeline.CreateArguments(reference);
        using IGraphicsBuffer<byte> raw = device.CreateBuffer(new BufferDesc<byte> { Count = 4, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        raw.CopyFrom(new byte[4]);
        using (CommandEncoder overwrite = device.CreateCommandEncoder())
        {
            overwrite.RecordCopyBuffer(raw.Slice(0, 4), gpu.Slice(0, 4));
            Assert.Throws<ArgumentException>(() => pipeline.CreateArguments(reference));
            using CommandBuffer commands = overwrite.Finish();
            device.Submit(commands).Wait();
        }

        Assert.Throws<ArgumentException>(() => device.CreateMaterialReference(gpu.Slice(0, 32)));

        // A later pending overwrite must prevent an older upload completion restoring its registration.
        using CommandEncoder first = device.CreateCommandEncoder();
        first.RecordCopyBuffer(upload.Slice(0, 32), gpu.Slice(0, 32));
        using CommandBuffer firstCommands = first.Finish();
        Submission firstSubmission = device.Submit(firstCommands);
        using CommandEncoder second = device.CreateCommandEncoder();
        second.RecordCopyBuffer(raw.Slice(0, 4), gpu.Slice(0, 4));
        using CommandBuffer secondCommands = second.Finish();
        Submission secondSubmission = device.Submit(secondCommands);
        firstSubmission.Wait();
        secondSubmission.Wait();
        Assert.Throws<ArgumentException>(() => device.CreateMaterialReference(gpu.Slice(0, 32)));
    }

    /// <summary>
    /// Verifies texture interfaces retain parent and recorded view until completion.
    /// </summary>
    [Fact]
    public void TextureInterfacesRetainParentAndRecordedViewUntilCompletion()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using IGraphicsTexture texture = device.CreateTexture(new TextureDesc { Width = 4, Height = 4 });
        using IGraphicsTextureView view = texture.CreateView();
        Assert.Equal(4U, texture.Width);
        Assert.Equal(4U, texture.Height);
        Assert.Same(texture, view.Texture);
        Assert.Throws<InvalidOperationException>(texture.Dispose);
        using IGraphicsBuffer<byte> readback = Readback<byte>(device, 4 * 256);
        using GraphicsDevice foreign = Graphics.CreateDevice();
        using CommandEncoder foreignEncoder = foreign.CreateCommandEncoder();
        Assert.Throws<ArgumentException>(() => foreignEncoder.RecordCopyTextureToBuffer(texture, readback, 256));
        using CommandEncoder encoder = device.CreateCommandEncoder();
        using (RenderEncoder pass = encoder.BeginRenderPass(new RenderPassDesc { Target = view }))
        {
        }

        Assert.Throws<InvalidOperationException>(view.Dispose);
        using CommandBuffer commands = encoder.Finish();
        Submission submission = device.Submit(commands);
        Assert.Throws<InvalidOperationException>(view.Dispose);
        submission.Wait();
        view.Dispose();
        view.Dispose();
        texture.Dispose();
        texture.Dispose();
        Assert.Throws<ObjectDisposedException>(() => texture.CreateView());
        using CommandEncoder nextEncoder = device.CreateCommandEncoder();
        Assert.Throws<ObjectDisposedException>(() => nextEncoder.BeginRenderPass(new RenderPassDesc { Target = view }));
    }

    /// <summary>
    /// Verifies backend reports typed copy alignment and recorded copies honor it.
    /// </summary>
    [Fact]
    public void BackendReportsTypedCopyAlignmentAndRecordedCopiesHonorIt()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        BufferLayout<byte> bytes = device.GetBufferLayout<byte>();
        Assert.Equal(1UL, bytes.ElementStrideInBytes);
        Assert.Equal(4UL, bytes.CopyOffsetAlignmentInBytes);
        Assert.Equal(4UL, bytes.CopySizeAlignmentInBytes);
        Assert.Equal(4UL, bytes.CopyOffsetAlignmentInElements);
        Assert.Equal(4UL, bytes.CopyCountAlignment);
        Assert.Equal(2UL, device.GetBufferLayout<ushort>().CopyCountAlignment);
        Assert.Equal(1UL, device.GetBufferLayout<uint>().CopyCountAlignment);
        Assert.Equal(1UL, device.GetBufferLayout<double>().CopyCountAlignment);
        BufferLayout<TripleByte> triples = device.GetBufferLayout<TripleByte>();
        Assert.Equal(3UL, triples.ElementSizeInBytes);
        Assert.Equal(3UL, triples.ElementStrideInBytes);
        Assert.Equal(4UL, triples.CopyOffsetAlignmentInElements);
        Assert.Equal(4UL, triples.CopyCountAlignment);
        Assert.Equal(12UL, triples.GetSizeInBytes(4));
        using IGraphicsBuffer<TripleByte> upload = device.CreateBuffer(new BufferDesc<TripleByte> { Count = 8, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload, });
        Assert.Equal(triples, upload.Layout);
        using IGraphicsBuffer<byte> readback = Readback<byte>(device, 12);
        var values = new TripleByte[]
        {
            new()
            {
                X = 1,
                Y = 2,
                Z = 3,
            },
        };
        upload.Slice(4, 4).CopyFrom(values); // CPU copies do not require GPU copy alignment.
        using CommandEncoder encoder = device.CreateCommandEncoder();
        Assert.Throws<ArgumentException>(() => encoder.RecordCopyBuffer(upload.Slice(1, 4), readback.Slice(0, 12)));
        Assert.Throws<ArgumentException>(() => encoder.RecordCopyBuffer(upload.Slice(0, 1), readback.Slice(0, 3)));
        encoder.RecordCopyBuffer(upload.Slice(4, 4), readback.Slice(0, 12));
        using CommandBuffer commands = encoder.Finish();
        device.Submit(commands).Wait();
        byte[] result = new byte[12];
        readback.CopyTo(result);
        Assert.Equal(new byte[] { 1, 2, 3 }, result[..3]);
        Assert.Throws<InvalidOperationException>(() => default(BufferLayout<uint>).GetSizeInBytes(4));
    }

    /// <summary>
    /// Verifies typed buffers compute sizes and copy element ranges across types.
    /// </summary>
    [Fact]
    public void TypedBuffersComputeSizesAndCopyElementRangesAcrossTypes()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using IGraphicsBuffer<Pair> upload = device.CreateBuffer(new BufferDesc<Pair> { Count = 3, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload, });
        Assert.Equal(3UL, upload.Count);
        Assert.Equal(24UL, upload.SizeInBytes);
        upload.CopyFrom(new Pair[] { new(1, 1.5f), new(2, 2.5f), new(3, 3.5f) });
        BufferSlice<Pair> part = upload.Slice(1, 2);
        Assert.Same(upload, part.Buffer);
        Assert.Equal(8UL, part.OffsetInBytes);
        Assert.Equal(16UL, part.SizeInBytes);
        part.CopyFrom(new Pair[] { new(4, 4.5f) });
        Assert.Throws<ArgumentException>(() => part.CopyFrom(new Pair[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => upload.Slice(2, 2));
        using IGraphicsBuffer<byte> result = Readback<byte>(device, 16);
        using CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(part, result.Slice(0, 16));
        using CommandBuffer commands = encoder.Finish();
        device.Submit(commands).Wait();
        byte[] bytes = new byte[16];
        result.CopyTo(bytes);
        Assert.Equal(new Pair[] { new(4, 4.5f), new(3, 3.5f) }, MemoryMarshal.Cast<byte, Pair>(bytes).ToArray());
        using GraphicsDevice foreign = Graphics.CreateDevice();
        using CommandEncoder foreignEncoder = foreign.CreateCommandEncoder();
        Assert.Throws<ArgumentException>(() => foreignEncoder.RecordCopyBuffer(part, result.Slice(0, 16)));

        // The backend object implements both contracts; no common resource wrapper is allocated.
        Assert.False(upload is GpuResource);
    }

    /// <summary>
    /// Verifies typed sizes use managed layout and reject overflow before allocation.
    /// </summary>
    [Fact]
    public void TypedSizesUseManagedLayoutAndRejectOverflowBeforeAllocation()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        Assert.Equal(4UL, device.GetBufferLayout<byte>().GetSizeInBytes(4));
        Assert.Equal(4UL, device.GetBufferLayout<ushort>().GetSizeInBytes(2));
        Assert.Equal(16UL, device.GetBufferLayout<double>().GetSizeInBytes(2));
        Assert.Equal(4UL, device.GetBufferLayout<bool>().GetSizeInBytes(4));
        Assert.Throws<OverflowException>(() => device.CreateBuffer(new BufferDesc<double> { Count = ulong.MaxValue, Usage = BufferUsage.CopySource, }));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.CreateBuffer(new BufferDesc<uint> { Count = 0, Usage = BufferUsage.CopySource, }));
        Assert.Throws<ArgumentNullException>(() => device.CreateBuffer<uint>(null!));
        Assert.Throws<ArgumentException>(() => device.CreateReference(default(BufferSlice<uint>)));
        device.Dispose();
    }

    /// <summary>
    /// Verifies cpu read copy uses caller memory and validates ranges.
    /// </summary>
    [Fact]
    public void CpuReadCopyUsesCallerMemoryAndValidatesRanges()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using IGraphicsBuffer<byte> upload = device.CreateBuffer(new BufferDesc<byte> { Count = 16, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload, });
        using IGraphicsBuffer<byte> readback = Readback<byte>(device, 16);
        upload.CopyFrom(Enumerable.Range(0, 16).Select(x => (byte)x).ToArray());
        upload.Slice(3, 4).CopyFrom(new byte[] { 30, 40 });
        using CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(upload.Slice(0, 16), readback.Slice(0, 16));
        using CommandBuffer commands = encoder.Finish();
        device.Submit(commands).Wait();
        byte[] destination = Enumerable.Repeat((byte)255, 8).ToArray();
        readback.Slice(3, 4).CopyTo(destination.AsSpan());
        Assert.Equal(new byte[] { 30, 40, 5, 6, 255, 255, 255, 255 }, destination);
        Assert.Throws<ArgumentException>(() => readback.Slice(0, 16).CopyTo(new byte[4].AsSpan()));
        Assert.Throws<ArgumentException>(() => upload.Slice(0, 4).CopyTo(new byte[4].AsSpan()));
        Assert.Throws<ArgumentException>(() => default(BufferSlice<byte>).CopyTo(new byte[4].AsSpan()));
        Assert.Throws<ArgumentException>(() => default(BufferSlice<byte>).CopyFrom(new byte[4]));
        Assert.Throws<ArgumentException>(() => upload.CopyFrom(new byte[17]));
        BufferSlice<byte> stale = upload.Slice(0, 4);
        upload.Dispose();
        Assert.Throws<ObjectDisposedException>(() => stale.CopyFrom(new byte[4]));
    }

    /// <summary>
    /// Verifies cpu copy only writes upload memory and transfer requires commands.
    /// </summary>
    [Fact]
    public void CpuCopyOnlyWritesUploadMemoryAndTransferRequiresCommands()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using IGraphicsBuffer<uint> upload = device.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload, });
        using IGraphicsBuffer<uint> gpu = device.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopyDestination, });
        Assert.Throws<ArgumentException>(() => gpu.Slice(0, 4).CopyFrom(new uint[] { 1, 2, 3, 4 }));
        Assert.Throws<ArgumentException>(() => device.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Upload, }));
        upload.Slice(0, 4).CopyFrom(new uint[] { 1, 2, 3, 4 });
        upload.Slice(0, 4).CopyFrom(new uint[] { 5, 6, 7, 8 });
        using IGraphicsBuffer<uint> output = Readback<uint>(device, 4);
        using CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(upload.Slice(0, 4), output.Slice(0, 4));
        Assert.Throws<InvalidOperationException>(() => upload.Slice(0, 4).CopyFrom(new uint[] { 9 }));
        Assert.Throws<InvalidOperationException>(() => ReadWords(output));
        using CommandBuffer commands = encoder.Finish();
        device.Submit(commands).Wait();
        Assert.Equal(new uint[] { 5, 6, 7, 8 }, ReadWords(output));
    }

    /// <summary>
    /// Verifies consumers and core have no backend assembly references.
    /// </summary>
    [Fact]
    public void ConsumersAndCoreHaveNoBackendAssemblyReferences()
    {
        foreach (Assembly? assembly in new[]
        {
            typeof(BackendTests).Assembly,
            typeof(GraphicsDevice).Assembly,
        })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), name => name.Name == "Lumyte.Graphics.Wgpu" || name.Name!.StartsWith("Ahjo."));
        }
    }

    /// <summary>
    /// Verifies backend selection and cross device commands are validated through common api.
    /// </summary>
    [Fact]
    public void BackendSelectionAndCrossDeviceCommandsAreValidatedThroughCommonApi()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.CreateDevice((GraphicsBackend)999));
        using GraphicsDevice first = Graphics.CreateDevice();
        using GraphicsDevice second = Graphics.CreateDevice();
        using IGraphicsTexture target = first.CreateTexture(new TextureDesc { Width = 4, Height = 4 });
        using IGraphicsTextureView view = target.CreateView();
        using CommandEncoder encoder = second.CreateCommandEncoder();
        Assert.Throws<ArgumentException>(() => encoder.BeginRenderPass(new RenderPassDesc { Target = view }));
        using ShaderModule shader = first.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.double.wgsl");
        Assert.Throws<ArgumentException>(() => second.CreateComputePipeline(new ComputePipelineDesc { Shader = shader }));
        using CommandBuffer commands = encoder.Finish();
        Assert.Throws<ArgumentException>(() => first.Submit(commands));
        second.Submit(commands).Wait();
    }

    /// <summary>
    /// Verifies offline shaders are embedded without deployment sidecars.
    /// </summary>
    [Fact]
    public void OfflineShadersAreEmbeddedWithoutDeploymentSidecars()
    {
        Assembly assembly = typeof(BackendTests).Assembly;
        foreach (string? name in new[]
        {
            "double",
            "triangle",
            "material",
        })
        {
            using Stream? stream = assembly.GetManifestResourceStream($"Lumyte.Shaders.{name}.wgsl");
            Assert.NotNull(stream);
            Assert.True(stream.Length > 0);
        }

        Assert.Empty(Directory.GetFiles(AppContext.BaseDirectory, "*.wgsl", SearchOption.AllDirectories));
        using GraphicsDevice device = Graphics.CreateDevice();
        Assert.Throws<ArgumentException>(() => device.CreateShader(assembly, "missing.wgsl"));
    }

    /// <summary>
    /// Verifies slang compute uses opaque reference and returns doubled data async.
    /// </summary>
    /// <returns>A task that completes when the asynchronous assertion has finished.</returns>
    [Fact]
    public async Task SlangComputeUsesOpaqueReferenceAndReturnsDoubledDataAsync()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using IGraphicsBuffer<uint> data = device.CreateBuffer(new BufferDesc<uint> { Count = 8, Usage = BufferUsage.CopyDestination | BufferUsage.CopySource | BufferUsage.ShaderWrite, });
        using IGraphicsBuffer<uint> output = Readback<uint>(device, 8);
        using ShaderModule shader = device.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.double.wgsl");
        using ComputePipeline pipeline = device.CreateComputePipeline(new ComputePipelineDesc { Shader = shader });
        using IGraphicsBuffer<uint> upload = device.CreateBuffer(new BufferDesc<uint> { Count = 8, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload, });
        upload.CopyFrom(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        GpuReference<uint> reference = device.CreateReference<uint>(data.Slice(0, 8));
        Assert.Equal("GpuReference<UInt32>", reference.ToString());
        using ShaderArguments arguments = pipeline.CreateArguments(reference);
        using CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(upload.Slice(0, 8), data.Slice(0, 8));
        encoder.Dispatch(pipeline, arguments, 1);
        encoder.RecordCopyBuffer(data.Slice(0, 8), output.Slice(0, 8));
        using CommandBuffer commands = encoder.Finish();
        Submission submitted = device.Submit(commands);
        Assert.Throws<InvalidOperationException>(() => device.Submit(commands));
        await submitted.WaitAsync();
        Assert.Equal(new uint[] { 2, 4, 6, 8, 10, 12, 14, 16 }, ReadWords(output));
        Assert.True(submitted.IsCompleted);
    }

    /// <summary>
    /// Verifies indexed triangle and clear have correct readback.
    /// </summary>
    [Fact]
    public void IndexedTriangleAndClearHaveCorrectReadback()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using IGraphicsTexture target = device.CreateTexture(new TextureDesc { Width = 64, Height = 64 });
        using IGraphicsTextureView view = target.CreateView();
        using IGraphicsBuffer<uint> output = Readback<uint>(device, 64 * 256 / sizeof(uint));
        using IGraphicsBuffer<uint> indices = device.CreateBuffer(new BufferDesc<uint> { Count = 3, Usage = BufferUsage.Index | BufferUsage.CopyDestination });
        using IGraphicsBuffer<uint> upload = device.CreateBuffer(new BufferDesc<uint> { Count = 3, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload, });
        upload.Slice(0, 3).CopyFrom(new uint[] { 0, 1, 2 });
        using ShaderModule shader = device.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.triangle.wgsl");
        using GraphicsPipeline pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader });
        using CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(upload.Slice(0, 3), indices.Slice(0, 3));
        using (RenderEncoder pass = encoder.BeginRenderPass(new RenderPassDesc { Target = view, ClearValue = new Color4(0, 0, 1, 1) }))
        {
            pass.SetPipeline(pipeline);
            Assert.Throws<ArgumentException>(() => pass.SetIndexBuffer(indices.Slice(0, 3), IndexFormat.Uint16));
            pass.SetIndexBuffer(indices.Slice(0, 3), IndexFormat.Uint32);
            Assert.Throws<ArgumentOutOfRangeException>(() => pass.DrawIndexed(new IndexedDrawDesc { IndexCount = 4 }));
            pass.DrawIndexed(new IndexedDrawDesc { IndexCount = 3 });
        }

        encoder.RecordCopyTextureToBuffer(target, output, 256);
        using CommandBuffer commands = encoder.Finish();
        device.Submit(commands).Wait();
        uint[] result = ReadWords(output);
        byte[] bytes = MemoryMarshal.AsBytes(result.AsSpan()).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, bytes.AsSpan(((32 * 64) + 32) * 4, 4).ToArray());
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, bytes.AsSpan(0, 4).ToArray());
    }

    /// <summary>
    /// Verifies pass state validation does not corrupt recording.
    /// </summary>
    [Fact]
    public void PassStateValidationDoesNotCorruptRecording()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using IGraphicsTexture target = device.CreateTexture(new TextureDesc { Width = 64, Height = 64 });
        using IGraphicsTextureView view = target.CreateView();
        using CommandEncoder encoder = device.CreateCommandEncoder();
        using RenderEncoder pass = encoder.BeginRenderPass(new RenderPassDesc { Target = view });
        Assert.Throws<InvalidOperationException>(() => encoder.Finish());
        Assert.Throws<InvalidOperationException>(() => encoder.BeginRenderPass(new RenderPassDesc { Target = view }));
        Assert.Throws<InvalidOperationException>(() => pass.Draw(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => pass.SetViewport(new Viewport(float.NaN, 0, 64, 64)));
        Assert.Throws<ArgumentOutOfRangeException>(() => pass.SetScissor(new Scissor(uint.MaxValue, 0, 2, 64)));
        pass.SetViewport(new Viewport(0, 0, 64, 64));
        pass.SetScissor(new Scissor(0, 0, 64, 64));
        pass.End();
        Assert.Throws<InvalidOperationException>(pass.End);
        Assert.Throws<InvalidOperationException>(() => pass.SetScissor(new Scissor(0, 0, 1, 1)));
        pass.Dispose();
        using CommandBuffer commands = encoder.Finish();
        device.Submit(commands).Wait();
    }

    /// <summary>
    /// Verifies resources are held through recording and submission.
    /// </summary>
    [Fact]
    public void ResourcesAreHeldThroughRecordingAndSubmission()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using IGraphicsBuffer<uint> source = device.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopySource });
        using IGraphicsBuffer<uint> destination = Readback<uint>(device, 4);
        using CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(source.Slice(0, 4), destination.Slice(0, 4));
        Assert.Throws<InvalidOperationException>(source.Dispose);
        using CommandBuffer commands = encoder.Finish();
        Submission submission = device.Submit(commands);
        Assert.Throws<InvalidOperationException>(source.Dispose);
        Assert.Throws<InvalidOperationException>(device.Dispose);
        submission.Wait();
        source.Dispose();
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.Slice(0, 4));
    }

    /// <summary>
    /// Verifies discarding encoder releases resources and invalidates open pass.
    /// </summary>
    [Fact]
    public void DiscardingEncoderReleasesResourcesAndInvalidatesOpenPass()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        using IGraphicsTexture target = device.CreateTexture(new TextureDesc { Width = 64, Height = 64 });
        using IGraphicsTextureView view = target.CreateView();
        using CommandEncoder encoder = device.CreateCommandEncoder();
        using RenderEncoder pass = encoder.BeginRenderPass(new RenderPassDesc { Target = view });
        encoder.Dispose();
        Assert.Throws<InvalidOperationException>(() => pass.Draw(3));
        pass.Dispose();
        view.Dispose();
        target.Dispose();
    }

    /// <summary>
    /// Verifies invalid embedded shader requests do not retain resources.
    /// </summary>
    [Fact]
    public void InvalidEmbeddedShaderRequestsDoNotRetainResources()
    {
        using GraphicsDevice device = Graphics.CreateDevice();
        Assert.Throws<ArgumentNullException>(() => device.CreateShader(null!, "missing"));
        Assert.Throws<ArgumentException>(() => device.CreateShader(typeof(BackendTests).Assembly, string.Empty));
        Assert.Throws<ArgumentException>(() => device.CreateShader(typeof(BackendTests).Assembly, "missing"));
        device.Dispose();
    }

    /// <summary>
    /// Verifies invalid ranges and cross device references are rejected.
    /// </summary>
    [Fact]
    public void InvalidRangesAndCrossDeviceReferencesAreRejected()
    {
        using GraphicsDevice first = Graphics.CreateDevice();
        using GraphicsDevice second = Graphics.CreateDevice();
        using IGraphicsBuffer<uint> data = first.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.ShaderWrite });
        Assert.Throws<ArgumentOutOfRangeException>(() => data.Slice(ulong.MaxValue, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.Slice(3, 2));
        Assert.Throws<ArgumentException>(() => second.CreateReference<uint>(data.Slice(0, 4)));
        using IGraphicsBuffer<float> floatData = first.CreateBuffer(new BufferDesc<float> { Count = 4, Usage = BufferUsage.ShaderWrite });
        Assert.Throws<NotSupportedException>(() => first.CreateReference<float>(floatData.Slice(0, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.CreateBuffer(new BufferDesc<byte> { Count = 3, Usage = BufferUsage.CopySource }));
        Assert.Throws<ArgumentException>(() => first.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = (BufferUsage)128 }));
        using ShaderModule shader = first.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.double.wgsl");
        using ComputePipeline pipeline = first.CreateComputePipeline(new ComputePipelineDesc { Shader = shader });
        Assert.Throws<ArgumentException>(() => pipeline.CreateArguments(default));
    }

    private static IGraphicsBuffer<T> Readback<T>(GraphicsDevice device, ulong count)
        where T : unmanaged => device.CreateBuffer(new BufferDesc<T> { Count = count, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback, });

    private static uint[] ReadWords(IGraphicsBuffer<uint> buffer)
    {
        uint[] words = new uint[checked((int)buffer.Count)];
        buffer.CopyTo(words.AsSpan());
        return words;
    }

    private readonly record struct Pair(int X, float Y);

    [System.Runtime.InteropServices.StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TripleByte
    {
        public byte X;
        public byte Y;
        public byte Z;
    }
}
