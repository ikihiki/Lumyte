using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

internal sealed class DeviceValue(GraphicsRun run, SchemaDescriptor descriptor, SchemaExtent extent, int producerNode = 0)
{
    private readonly List<PipelineTextureResource> _textures = [];
    private readonly List<IGraphicsSampler> _samplers = [];
    private object? _buffer;
    private bool _encoded;
    private IDeviceStorage? _storage;
    private ulong _bufferOffset;
    private PipelineExternalResource? _externalBuffer;

    internal GraphicsRun Run { get; } = run;

    internal SchemaDescriptor Descriptor { get; } = descriptor;

    internal SchemaExtent Extent { get; } = extent;

    internal bool Encoded => _encoded;

    internal IDeviceStorage Storage => _storage ?? throw new InvalidOperationException("No canonical device buffer is bound.");

    internal void ValidateAccess(ResourceAccess access, int node)
    {
        Run.Validate();
        if (!_encoded)
        {
            throw new InvalidOperationException("A device input has no prepared producer dependency.");
        }

        _storage?.Validate(Run.Device, access);
        foreach (PipelineTextureResource texture in _textures)
        {
            if (texture.View is not IGraphicsNativeOrigin origin || !ReferenceEquals(origin.Device, Run.Device))
            {
                throw new ArgumentException("The texture view belongs to another actual native device.");
            }

            origin.ValidateOrigin();
            if ((access & ResourceAccess.ShaderWrite) != 0)
            {
                throw new NotSupportedException("A sampled/attachment view is not a storage-texture write binding.");
            }

            var required = (TextureUsage)0;
            if ((access & ResourceAccess.ShaderRead) != 0)
            {
                required |= TextureUsage.Sampled;
            }

            if ((access & (ResourceAccess.ColorRead | ResourceAccess.ColorWrite)) != 0)
            {
                required |= TextureUsage.RenderAttachment;
            }

            if ((access & ResourceAccess.CopyRead) != 0)
            {
                required |= TextureUsage.CopySource;
            }

            if ((access & ResourceAccess.CopyWrite) != 0)
            {
                required |= TextureUsage.CopyDestination;
            }

            if ((texture.View.Texture.Usage & required) != required)
            {
                throw new ArgumentException("Texture usage conflicts with the complete declared access.");
            }

            if ((access & (ResourceAccess.ColorWrite | ResourceAccess.CopyWrite | ResourceAccess.ShaderWrite)) != 0)
            {
                Run.ClaimWrite(origin.AllocationIdentity, node);
            }
        }
    }

    internal void BindTextures(IReadOnlyList<IGraphicsTextureView> views, IReadOnlyList<IGraphicsSampler>? samplers = null)
    {
        if (_buffer is not null || _encoded)
        {
            throw new InvalidOperationException("Device source is already bound.");
        }

        for (int index = 0; index < views.Count; index++)
        {
            _textures.Add(Run.BindTexture(views[index], TextureState.Undefined, default, ((long)producerNode << 32) | (uint)index));
        }

        if (samplers is not null)
        {
            _samplers.AddRange(samplers);
        }

        _encoded = true;
    }

    internal void DeclareSamplers(BarrierScope scope)
    {
        _externalBuffer?.AddAccess(scope);

        foreach (IGraphicsSampler sampler in _samplers)
        {
            Run.BindSampler(sampler, scope, ((long)producerNode << 32) | (uint)(_textures.Count + _samplers.IndexOf(sampler)));
        }
    }

    internal void BindExternalBuffer<TElement>(BufferSlice<TElement> slice)
        where TElement : unmanaged
    {
        if (Extent.ElementCount != checked((long)slice.Count))
        {
            throw new ArgumentException("Canonical extent differs from the exact native range.", nameof(slice));
        }

        SetBuffer(slice.Buffer, slice.Offset);
        _externalBuffer = Run.BindBuffer(slice, (long)producerNode << 32);
        _encoded = true;
    }

    internal IGraphicsTextureView GetTexture(int index, BarrierScope scope)
    {
        if ((uint)index >= (uint)_textures.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        PipelineTextureResource resource = _textures[index];
        if ((scope.Access & (ResourceAccess.ColorRead | ResourceAccess.ColorWrite)) != 0)
        {
            resource.Access(TextureState.ColorAttachment, scope);
        }

        if ((scope.Access & ResourceAccess.ShaderRead) != 0)
        {
            resource.Access(TextureState.Sampled, scope);
        }

        if ((scope.Access & ResourceAccess.CopyRead) != 0)
        {
            resource.Access(TextureState.CopySource, scope);
        }

        if ((scope.Access & ResourceAccess.CopyWrite) != 0)
        {
            resource.Access(TextureState.CopyDestination, scope);
        }

        return resource.View;
    }

    internal void SetBuffer<TElement>(IGraphicsBuffer<TElement> buffer, ulong offset = 0)
        where TElement : unmanaged
    {
        CheckLayout<TElement>();
        if (_buffer is not null)
        {
            throw new InvalidOperationException("Device storage is already bound.");
        }

        _buffer = buffer;
        _bufferOffset = offset;
        _storage = new DeviceStorage<TElement>(buffer.SliceElements(offset, checked((ulong)Extent.ElementCount)));
    }

    internal BufferSlice<TElement> GetBuffer<TElement>()
        where TElement : unmanaged
    {
        CheckLayout<TElement>();
        Run.Validate();
        if (_buffer is null)
        {
            long bytes = checked(Extent.ElementCount * Descriptor.ElementStride);
            Run.ReserveScratch(bytes);
            IGraphicsBuffer<TElement> buffer = Run.Device.CreateBuffer<TElement>(new BufferDesc<TElement>
            {
                Count = checked((ulong)Extent.ElementCount),
                Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource | BufferUsage.CopyDestination,
            });
            Run.Own(buffer);
            SetBuffer(buffer);
        }

        return ((IGraphicsBuffer<TElement>)_buffer!).SliceElements(_bufferOffset, checked((ulong)Extent.ElementCount));
    }

    internal void Encode()
    {
        bool dependency = Descriptor.Metadata.TryGetValue("dependencyOnly", out object? token) && Equals(token, true);
        if (_encoded || (_buffer is null && !dependency))
        {
            throw new InvalidOperationException("Device output must be allocated and encoded exactly once.");
        }

        _encoded = true;
    }

    private void CheckLayout<TElement>()
        where TElement : unmanaged
    {
        if (Descriptor.ElementType != typeof(TElement) || Descriptor.ElementStride != Unsafe.SizeOf<TElement>() || Extent.ElementCount <= 0)
        {
            throw new InvalidOperationException("Device binding element layout or extent mismatch.");
        }
    }
}
