using System.Reflection;
using G = Lumyte.Graphics;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuDriver(WgpuDevice device) : IGraphicsDriver
{
    public ulong MaxBufferSize => device.MaxBufferSize;

    public void Dispose() => device.Dispose();

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => device.GetBufferLayout<T>();

    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged => device.CreateBuffer(desc);

    public IGraphicsTexture CreateTexture(TextureDesc desc) => device.CreateTexture(desc);

    public object CreateSampler(SamplerDesc desc) => device.CreateSampler(desc);

    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc)
    {
        lock (device.Gate)
        {
            device.Check();
            ArgumentNullException.ThrowIfNull(desc);
            if ((ulong)desc.TextureCapacity + desc.SamplerCapacity + desc.BufferCapacity == 0)
            {
                throw new ArgumentException("At least one descriptor slot is required.", nameof(desc));
            }

            return new ArgumentTable(this, device, desc);
        }
    }

    public ShaderDataLayout<T> GetDataLayout<T>(object shader)
    {
        lock (device.Gate)
        {
            var module = (ShaderModule)shader;
            module.Check(device);
            ShaderDataSchema schema = module.ShaderDataSchema ?? throw new NotSupportedException("Shader reflection does not declare the supported bindless data ABI.");
            return new(this, schema, schema.ElementStrideInBytes);
        }
    }

    public void PackShaderData<T>(G.BufferRange destination, ReadOnlySpan<T> values, object schema, IShaderDataSerializer<T> serializer)
    {
        lock (device.Gate)
        {
            ArgumentNullException.ThrowIfNull(serializer);
            if (schema is not ShaderDataSchema layout || !ReferenceEquals(layout.Owner, device) || values.IsEmpty || destination.Length != checked((ulong)values.Length * layout.ElementStrideInBytes))
            {
                throw new ArgumentException("Packing requires an exact-size range and a matching device schema.");
            }

            BufferSlice target = Slice(destination);
            byte[] bytes = new byte[checked((int)target.Length)];
            var dependencies = new List<DescriptorRegistration>();
            var elements = new DescriptorRegistration[values.Length][];
            var writer = new ShaderDataWriter(device, layout, bytes, dependencies);
            for (int i = 0; i < values.Length; i++)
            {
                dependencies.Clear();
                writer.BeginRow(checked(i * (int)layout.ElementStrideInBytes));
                try
                {
                    serializer.Serialize(in values[i], writer);
                    elements[i] = dependencies.Distinct().ToArray();
                }
                finally
                {
                    writer.EndRow();
                }
            }

            foreach (DescriptorRegistration registration in elements.SelectMany(element => element).Distinct())
            {
                registration.Check(device);
            }

            var snapshot = new ShaderDataSnapshot(device, layout, typeof(T), elements);
            try
            {
                target.Buffer.CopyFrom(bytes, target.Offset, target.Length);
                target.Buffer.RegisterShaderData(target.Offset, target.Length, snapshot);
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
        }
    }

    public object CreateShaderDataReference(G.BufferRange range, Type dataType)
    {
        lock (device.Gate)
        {
            BufferSlice slice = Slice(range);
            if (!slice.Buffer.Usage.HasFlag(BufferUsage.ShaderRead) || slice.Buffer.Usage.HasFlag(BufferUsage.ShaderWrite))
            {
                throw new ArgumentException("Shader data requires read-only storage.");
            }

            ShaderDataRegion region = slice.Buffer.FindShaderData(slice.Offset, slice.Length) ?? throw new ArgumentException("Range has no completed shader-data metadata.");
            if (region.Snapshot.DataType != dataType)
            {
                throw new ArgumentException("Logical element type does not match the serialized schema.");
            }

            return region;
        }
    }

    public object CreateDrawingArguments(object pipeline, object? data)
    {
        if (data is not ShaderDataRegion region)
        {
            throw new ArgumentException("Invalid shader-data root reference.");
        }

        return ((GraphicsPipeline)pipeline).CreateArguments(region);
    }

    public object CreateShader(Assembly assembly, string resourceName) => device.CreateShader(assembly, resourceName);

    public object CreateComputePipeline(G.ComputePipelineDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        return device.CreateComputePipeline(new ComputePipelineDesc { Shader = Get<ShaderModule>(desc.Shader), EntryPoint = desc.EntryPoint });
    }

    public object CreateGraphicsPipeline(G.GraphicsPipelineDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        return device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = Get<ShaderModule>(desc.Shader), VertexEntry = desc.VertexEntry, FragmentEntry = desc.FragmentEntry });
    }

    public object CreateReference<T>(G.BufferRange data)
        where T : unmanaged => device.CreateReference<T>(Slice(data));

    public object CreateCommandEncoder() => device.CreateCommandEncoder();

    public object Submit(G.CommandBuffer commands) => device.Submit(Get<CommandBuffer>(commands));

    public void DisposeHandle(object handle) => ((IDisposable)handle).Dispose();

    public object CreateArguments(object handle, G.GpuReference<uint> data)
    {
        if (data.Handle is not GpuReference<uint> reference)
        {
            throw new ArgumentException("Invalid GPU data reference.", nameof(data));
        }

        return ((ComputePipeline)handle).CreateArguments(reference);
    }

    public object BeginRenderPass(object handle, G.RenderPassDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        return ((CommandEncoder)handle).BeginRenderPass(new RenderPassDesc { Target = GetTextureResource<TextureView>(desc.Target), Load = desc.Load, Store = desc.Store, ClearValue = desc.ClearValue });
    }

    public void Dispatch(object handle, G.ComputePipeline pipeline, G.ShaderArguments arguments, uint x, uint y, uint z) => ((CommandEncoder)handle).Dispatch(Get<ComputePipeline>(pipeline), Get<ShaderArguments>(arguments), x, y, z);

    public void RecordCopyBuffer(object handle, G.BufferRange source, G.BufferRange destination) => ((CommandEncoder)handle).RecordCopyBuffer(Slice(source), Slice(destination));

    public void RecordCopyBufferToTexture(object handle, G.BufferRange source, G.IGraphicsTexture destination, uint bytesPerRow) => ((CommandEncoder)handle).RecordCopyBufferToTexture(Slice(source), GetTextureResource<Texture>(destination), bytesPerRow);

    public void RecordCopyTextureToBuffer(object handle, G.IGraphicsTexture source, G.BufferRange destination, uint bytesPerRow) => ((CommandEncoder)handle).RecordCopyTextureToBuffer(GetTextureResource<Texture>(source), Slice(destination).Buffer, bytesPerRow);

    public object Finish(object handle) => ((CommandEncoder)handle).Finish();

    public void SetPipeline(object handle, G.GraphicsPipeline pipeline) => ((RenderEncoder)handle).SetPipeline(Get<GraphicsPipeline>(pipeline));

    public void SetViewport(object handle, Viewport viewport) => ((RenderEncoder)handle).SetViewport(viewport);

    public void SetScissor(object handle, Scissor scissor) => ((RenderEncoder)handle).SetScissor(scissor);

    public void SetIndexBuffer(object handle, G.BufferRange indices, IndexFormat format) => ((RenderEncoder)handle).SetIndexBuffer(Slice(indices), format);

    public void Draw(object handle, DrawDesc desc) => ((RenderEncoder)handle).Draw(desc);

    public void DrawWithArguments(object handle, G.ShaderArguments arguments, DrawDesc desc) => ((RenderEncoder)handle).Draw(Get<DrawingArguments>(arguments), desc);

    public void DrawIndexed(object handle, IndexedDrawDesc desc) => ((RenderEncoder)handle).DrawIndexed(desc);

    public void End(object handle) => ((RenderEncoder)handle).End();

    public bool IsCompleted(object handle) => ((Submission)handle).IsCompleted;

    public void Wait(object handle, CancellationToken cancellationToken) => ((Submission)handle).Wait(cancellationToken);

    public ValueTask WaitAsync(object handle, CancellationToken cancellationToken) => ((Submission)handle).WaitAsync(cancellationToken);

    private T Get<T>(G.GpuResource resource)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!ReferenceEquals(resource.Driver, this) || resource.Handle is not T value)
        {
            throw new ArgumentException("Resource belongs to another device or backend.", nameof(resource));
        }

        return value;
    }

    private T GetTextureResource<T>(object resource)
        where T : GpuResource
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource is not T value)
        {
            throw new ArgumentException("Unsupported texture implementation.", nameof(resource));
        }

        value.Check(device);
        return value;
    }

    private BufferSlice Slice(G.BufferRange slice)
    {
        if (slice.Buffer is not WgpuBuffer buffer)
        {
            throw new ArgumentException("Resource belongs to another backend.");
        }

        buffer.Check(device);
        return buffer.Slice(slice.Offset, slice.Length);
    }
}
