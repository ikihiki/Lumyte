using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using System.Runtime.InteropServices;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Lumyte.Graphics.WebGPU;

/// <summary>Owns a native WebGPU instance, selected adapter, device, and queue.</summary>
public sealed unsafe partial class WebGpuDevice : IGpuBackend, IDisposable
{
    private static readonly Lazy<Silk.NET.WebGPU.WebGPU> SharedApi = new(Silk.NET.WebGPU.WebGPU.GetApi);
    private readonly Silk.NET.WebGPU.WebGPU api;
    private readonly PfnRequestAdapterCallback adapterCallback;
    private readonly PfnRequestDeviceCallback deviceCallback;
    private readonly Dictionary<ulong, TextureRecord> textures = [];
    private readonly Dictionary<ulong, TextureViewRecord> textureViews = [];
    private readonly Dictionary<ulong, nint> samplers = [];
    private readonly Dictionary<ulong, RasterPipelineRecord> rasterPipelines = [];
    private readonly Dictionary<ResourceTableCacheKey, CachedBindGroup> bindGroups = [];
    private Instance* instance;
    private Adapter* adapter;
    private Device* device;
    private Queue* queue;
    private string? failure;
    private ulong nextResourceId = 1;
    private ulong nextTextureId = 1;
    private ulong nextPipelineId = 1;
    private int bindGroupCreationCount;
    private bool disposed;

    private WebGpuDevice(Silk.NET.WebGPU.WebGPU api)
    {
        this.api = api;
        adapterCallback = new((status, value, message, _) =>
        {
            if (status == RequestAdapterStatus.Success) { adapter = value; }
            else { failure = $"WebGPU adapter request failed: {status}."; }
        });
        deviceCallback = new((status, value, message, _) =>
        {
            if (status == RequestDeviceStatus.Success) { device = value; }
            else { failure = $"WebGPU device request failed: {status}."; }
        });
    }

    public static WebGpuDevice Create()
    {
        Silk.NET.WebGPU.WebGPU api = SharedApi.Value;
        var result = new WebGpuDevice(api);
        try
        {
            var instanceDescription = new InstanceDescriptor();
            result.instance = api.CreateInstance(in instanceDescription);
            if (result.instance is null) { throw new InvalidOperationException("wgpuCreateInstance returned null."); }

            var options = new RequestAdapterOptions { PowerPreference = PowerPreference.HighPerformance };
            api.InstanceRequestAdapter(result.instance, in options, result.adapterCallback, null);
            if (result.adapter is null) { throw new InvalidOperationException(result.failure ?? "WebGPU adapter request did not complete."); }

            var deviceDescription = new DeviceDescriptor();
            api.AdapterRequestDevice(result.adapter, in deviceDescription, result.deviceCallback, null);
            if (result.device is null) { throw new InvalidOperationException(result.failure ?? "WebGPU device request did not complete."); }
            result.queue = api.DeviceGetQueue(result.device);
            if (result.queue is null) { throw new InvalidOperationException("wgpuDeviceGetQueue returned null."); }
            result.MainQueue = new WebGpuQueue(result);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public nint NativeDevice => (nint)device;
    public nint NativeQueue => (nint)queue;
    public IGpuQueue MainQueue { get; private set; } = null!;
    public GpuBackendCapabilities Capabilities =>
        GpuBackendCapabilities.DeviceOwnedResources | GpuBackendCapabilities.RasterPipeline;

    internal int CachedBindGroupCount => bindGroups.Count;
    internal int BindGroupCreationCount => bindGroupCreationCount;

    /// <summary>Creates a WebGPU-owned texture without pretending to expose placed memory.</summary>
    public GpuTextureHandle CreateTexture(GpuTextureDescription description)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        description.Validate();
        var nativeDescription = new TextureDescriptor
        {
            Usage = ToWebGpuUsage(description.Usage),
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(description.Width, description.Height, description.LayerCount),
            Format = ToWebGpuFormat(description.Format),
            MipLevelCount = description.MipCount,
            SampleCount = description.SampleCount,
        };
        Texture* nativeTexture = api.DeviceCreateTexture(device, in nativeDescription);
        if (nativeTexture is null) { throw new InvalidOperationException("WebGPU texture creation failed."); }
        var handle = new GpuTextureHandle(nextTextureId++);
        textures.Add(handle.Value, new((nint)nativeTexture, description));
        return handle;
    }

    public GpuTextureView CreateTextureView(GpuTextureHandle texture, GpuTextureViewDescription description)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!textures.TryGetValue(texture.Value, out TextureRecord? record))
        {
            throw new ArgumentException("Texture does not belong to this WebGPU device.", nameof(texture));
        }
        uint mipCount = description.MipCount == uint.MaxValue
            ? record.Description.MipCount - description.BaseMip
            : description.MipCount;
        uint layerCount = description.LayerCount == uint.MaxValue
            ? record.Description.LayerCount - description.BaseLayer
            : description.LayerCount;
        if (description.Format != record.Description.Format
            || description.BaseMip >= record.Description.MipCount || mipCount == 0
            || mipCount > record.Description.MipCount - description.BaseMip
            || description.BaseLayer >= record.Description.LayerCount || layerCount == 0
            || layerCount > record.Description.LayerCount - description.BaseLayer)
        {
            throw new ArgumentOutOfRangeException(nameof(description));
        }
        var nativeDescription = new TextureViewDescriptor
        {
            Format = ToWebGpuFormat(description.Format),
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = description.BaseMip,
            MipLevelCount = mipCount,
            BaseArrayLayer = description.BaseLayer,
            ArrayLayerCount = layerCount,
            Aspect = TextureAspect.All,
        };
        TextureView* nativeView = api.TextureCreateView((Texture*)record.Handle, in nativeDescription);
        if (nativeView is null) { throw new InvalidOperationException("WebGPU texture view creation failed."); }
        var id = new TextureId(nextResourceId++);
        var normalized = description with { MipCount = mipCount, LayerCount = layerCount };
        textureViews.Add(id.Value, new((nint)nativeView, texture));
        return new(id, texture, normalized);
    }

    public SamplerId CreateSampler(GpuSamplerDescription description)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        description.Validate();
        var nativeDescription = new SamplerDescriptor
        {
            AddressModeU = ToWebGpuAddressMode(description.AddressU),
            AddressModeV = ToWebGpuAddressMode(description.AddressV),
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = ToWebGpuFilter(description.MagFilter),
            MinFilter = ToWebGpuFilter(description.MinFilter),
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            MaxAnisotropy = 1,
        };
        Sampler* nativeSampler = api.DeviceCreateSampler(device, in nativeDescription);
        if (nativeSampler is null) { throw new InvalidOperationException("WebGPU sampler creation failed."); }
        var id = new SamplerId(nextResourceId++);
        samplers.Add(id.Value, (nint)nativeSampler);
        return id;
    }

    public void DestroyTextureView(GpuTextureView view)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!textureViews.TryGetValue(view.Id.Value, out TextureViewRecord? record)
            || record.Texture != view.Texture)
        {
            throw new ArgumentException("Texture view does not belong to this WebGPU device.", nameof(view));
        }
        textureViews.Remove(view.Id.Value);
        InvalidateBindGroups();
        api.TextureViewRelease((TextureView*)record.Handle);
    }

    public void DestroyTexture(GpuTextureHandle texture)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (textureViews.Values.Any(view => view.Texture == texture))
        {
            throw new InvalidOperationException("Texture still has a live view.");
        }
        if (!textures.Remove(texture.Value, out TextureRecord? record))
        {
            throw new ArgumentException("Texture does not belong to this WebGPU device.", nameof(texture));
        }
        api.TextureRelease((Texture*)record.Handle);
    }

    internal Texture* ResolveTexture(GpuTextureHandle texture)
        => textures.TryGetValue(texture.Value, out TextureRecord? record)
            ? (Texture*)record.Handle
            : throw new ArgumentException("Texture does not belong to this WebGPU device.", nameof(texture));

    public void DestroySampler(SamplerId id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!samplers.Remove(id.Value, out nint sampler))
        {
            throw new ArgumentException("Sampler ID does not belong to this WebGPU device.", nameof(id));
        }
        InvalidateBindGroups();
        api.SamplerRelease((Sampler*)sampler);
    }

    internal nint GetOrCreateBindGroup(GpuResourceTable table, nint nativeLayout)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(table);
        if (nativeLayout == 0) { throw new ArgumentException("Bind group layout cannot be null.", nameof(nativeLayout)); }

        var key = new ResourceTableCacheKey(table, nativeLayout);
        if (bindGroups.TryGetValue(key, out CachedBindGroup? cached))
        {
            if (cached.Revision == table.Revision) { return cached.Handle; }
            api.BindGroupRelease((BindGroup*)cached.Handle);
            bindGroups.Remove(key);
        }

        int entryCount = checked(table.TextureSlotCount + table.SamplerSlotCount);
        if (entryCount == 0) { throw new ArgumentException("Resource table is empty.", nameof(table)); }
        var entries = new BindGroupEntry[entryCount];
        for (int slot = 0; slot < table.TextureSlotCount; slot++)
        {
            TextureId id = table.GetTexture(slot);
            if (id.IsNull || !textureViews.TryGetValue(id.Value, out TextureViewRecord? view))
            {
                throw new ArgumentException($"Texture slot {slot} is empty or belongs to another WebGPU device.", nameof(table));
            }
            entries[slot] = new BindGroupEntry { Binding = checked((uint)slot), TextureView = (TextureView*)view.Handle };
        }
        for (int slot = 0; slot < table.SamplerSlotCount; slot++)
        {
            SamplerId id = table.GetSampler(slot);
            if (id.IsNull || !samplers.TryGetValue(id.Value, out nint sampler))
            {
                throw new ArgumentException($"Sampler slot {slot} is empty or belongs to another WebGPU device.", nameof(table));
            }
            int index = table.TextureSlotCount + slot;
            entries[index] = new BindGroupEntry
            {
                Binding = checked((uint)index),
                Sampler = (Sampler*)sampler,
            };
        }

        fixed (BindGroupEntry* entryPointer = entries)
        {
            var description = new BindGroupDescriptor
            {
                Layout = (BindGroupLayout*)nativeLayout,
                EntryCount = checked((nuint)entryCount),
                Entries = entryPointer,
            };
            BindGroup* bindGroup = api.DeviceCreateBindGroup(device, in description);
            if (bindGroup is null) { throw new InvalidOperationException("WebGPU bind group creation failed."); }
            var result = new CachedBindGroup((nint)bindGroup, table.Revision);
            bindGroups.Add(key, result);
            bindGroupCreationCount++;
            return result.Handle;
        }
    }

    public byte[] RoundTripBuffer(ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (source.IsEmpty) { throw new ArgumentException("Source cannot be empty.", nameof(source)); }
        ulong size = Align(checked((ulong)source.Length), 4);
        var uploadDescription = new BufferDescriptor { Size = size, Usage = BufferUsage.CopySrc | BufferUsage.CopyDst };
        var readbackDescription = new BufferDescriptor { Size = size, Usage = BufferUsage.CopyDst | BufferUsage.MapRead };
        WgpuBuffer* upload = null;
        WgpuBuffer* readback = null;
        try
        {
            upload = api.DeviceCreateBuffer(device, in uploadDescription);
            readback = api.DeviceCreateBuffer(device, in readbackDescription);
            if (upload is null || readback is null) { throw new InvalidOperationException("WebGPU buffer creation failed."); }

            byte[] uploadBytes = new byte[checked((int)size)];
            source.CopyTo(uploadBytes);
            fixed (byte* bytes = uploadBytes)
            {
                api.QueueWriteBuffer(queue, upload, 0, bytes, checked((nuint)size));
            }
            SubmitCopy(upload, readback, size);
            return MapReadback(readback, checked((nuint)size)).AsSpan(0, source.Length).ToArray();
        }
        finally
        {
            if (upload is not null) { api.BufferRelease(upload); }
            if (readback is not null) { api.BufferRelease(readback); }
        }
    }

    public byte[] RoundTripRgba8Texture2X2(ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (source.Length != 16) { throw new ArgumentException("A 2x2 RGBA8 texture requires 16 bytes.", nameof(source)); }
        return RoundTripTexture(
            new(2, 2, GpuFormat.Rgba8Unorm, GpuTextureUsage.CopySource | GpuTextureUsage.CopyDestination),
            source,
            new(2, 2, 4, 8));
    }

    private byte[] RoundTripTexture(
        GpuTextureDescription description,
        ReadOnlySpan<byte> source,
        GpuTextureCopyFootprint footprint)
    {
        TextureFormat format = ToWebGpuFormat(description.Format);
        uint sourceRowPitch = checked((uint)footprint.RowPitch);
        uint tightRowPitch = checked(footprint.Width * footprint.BytesPerPixel);
        uint readbackRowPitch = Align(tightRowPitch, 256);
        ulong readbackSize = checked((ulong)readbackRowPitch * footprint.Height);
        var extent = new Extent3D
        {
            Width = footprint.Width,
            Height = footprint.Height,
            DepthOrArrayLayers = 1,
        };
        var textureDescription = new TextureDescriptor
        {
            Dimension = TextureDimension.Dimension2D,
            Size = extent,
            Format = format,
            MipLevelCount = description.MipCount,
            SampleCount = description.SampleCount,
            Usage = TextureUsage.CopyDst | TextureUsage.CopySrc,
        };
        var readbackDescription = new BufferDescriptor
        {
            Size = readbackSize,
            Usage = BufferUsage.CopyDst | BufferUsage.MapRead,
        };
        Texture* texture = null;
        WgpuBuffer* readback = null;
        try
        {
            texture = api.DeviceCreateTexture(device, in textureDescription);
            readback = api.DeviceCreateBuffer(device, in readbackDescription);
            if (texture is null || readback is null)
            {
                throw new InvalidOperationException("WebGPU texture resources could not be created.");
            }

            var textureCopy = new ImageCopyTexture { Texture = texture, Aspect = TextureAspect.All };
            var sourceLayout = new TextureDataLayout
            {
                BytesPerRow = sourceRowPitch,
                RowsPerImage = footprint.Height,
            };
            fixed (byte* bytes = source)
            {
                api.QueueWriteTexture(queue, in textureCopy, bytes, checked((nuint)source.Length), in sourceLayout, in extent);
            }

            var destinationLayout = new TextureDataLayout
            {
                BytesPerRow = readbackRowPitch,
                RowsPerImage = footprint.Height,
            };
            var bufferCopy = new ImageCopyBuffer { Buffer = readback, Layout = destinationLayout };
            SubmitTextureReadback(textureCopy, bufferCopy, extent);

            byte[] padded = MapReadback(readback, checked((nuint)readbackSize));
            byte[] result = new byte[checked((int)footprint.RequiredBytes)];
            for (uint row = 0; row < footprint.Height; row++)
            {
                padded.AsSpan(checked((int)(row * readbackRowPitch)), checked((int)tightRowPitch))
                    .CopyTo(result.AsSpan(checked((int)(row * footprint.RowPitch))));
            }
            return result;
        }
        finally
        {
            if (texture is not null) { api.TextureRelease(texture); }
            if (readback is not null) { api.BufferRelease(readback); }
        }
    }

    private void SubmitCopy(WgpuBuffer* source, WgpuBuffer* destination, ulong size)
    {
        CommandEncoder* encoder = null;
        CommandBuffer* commands = null;
        try
        {
            var encoderDescription = new CommandEncoderDescriptor();
            encoder = api.DeviceCreateCommandEncoder(device, in encoderDescription);
            if (encoder is null) { throw new InvalidOperationException("WebGPU command encoder creation failed."); }
            api.CommandEncoderCopyBufferToBuffer(encoder, source, 0, destination, 0, size);
            var commandDescription = new CommandBufferDescriptor();
            commands = api.CommandEncoderFinish(encoder, in commandDescription);
            if (commands is null) { throw new InvalidOperationException("WebGPU command buffer creation failed."); }
            api.QueueSubmit(queue, 1, ref commands);
        }
        finally
        {
            if (commands is not null) { api.CommandBufferRelease(commands); }
            if (encoder is not null) { api.CommandEncoderRelease(encoder); }
        }
    }

    private void SubmitTextureReadback(ImageCopyTexture source, ImageCopyBuffer destination, Extent3D extent)
    {
        CommandEncoder* encoder = null;
        CommandBuffer* commands = null;
        try
        {
            var encoderDescription = new CommandEncoderDescriptor();
            encoder = api.DeviceCreateCommandEncoder(device, in encoderDescription);
            if (encoder is null) { throw new InvalidOperationException("WebGPU command encoder creation failed."); }
            api.CommandEncoderCopyTextureToBuffer(encoder, in source, in destination, in extent);
            var commandDescription = new CommandBufferDescriptor();
            commands = api.CommandEncoderFinish(encoder, in commandDescription);
            if (commands is null) { throw new InvalidOperationException("WebGPU command buffer creation failed."); }
            api.QueueSubmit(queue, 1, ref commands);
        }
        finally
        {
            if (commands is not null) { api.CommandBufferRelease(commands); }
            if (encoder is not null) { api.CommandEncoderRelease(encoder); }
        }
    }

    private byte[] MapReadback(WgpuBuffer* buffer, nuint size)
    {
        bool completed = false;
        BufferMapAsyncStatus mapStatus = default;
        var callback = new PfnBufferMapCallback((status, _) => { mapStatus = status; completed = true; });
        api.BufferMapAsync(buffer, MapMode.Read, 0, size, callback, null);
        var extensions = new Wgpu(api.Context);
        while (!completed) { extensions.DevicePoll(device, true, null); }
        if (mapStatus != BufferMapAsyncStatus.Success) { throw new InvalidOperationException($"WebGPU map failed: {mapStatus}."); }
        void* mapped = api.BufferGetConstMappedRange(buffer, 0, size);
        if (mapped is null) { throw new InvalidOperationException("WebGPU returned a null mapped buffer range."); }
        try
        {
            byte[] result = new byte[checked((int)size)];
            Marshal.Copy((nint)mapped, result, 0, result.Length);
            return result;
        }
        finally
        {
            api.BufferUnmap(buffer);
            GC.KeepAlive(callback);
        }
    }

    private static uint Align(uint value, uint alignment)
        => checked((value + alignment - 1) & ~(alignment - 1));

    private static ulong Align(ulong value, ulong alignment)
        => checked((value + alignment - 1) & ~(alignment - 1));

    private static TextureFormat ToWebGpuFormat(GpuFormat format) => format switch
    {
        GpuFormat.R8Unorm => TextureFormat.R8Unorm,
        GpuFormat.Rg8Unorm => TextureFormat.RG8Unorm,
        GpuFormat.Rgba8Unorm => TextureFormat.Rgba8Unorm,
        GpuFormat.Bgra8Unorm => TextureFormat.Bgra8Unorm,
        GpuFormat.Rgba8UnormSrgb => TextureFormat.Rgba8UnormSrgb,
        GpuFormat.Bgra8UnormSrgb => TextureFormat.Bgra8UnormSrgb,
        GpuFormat.R32Float => TextureFormat.R32float,
        GpuFormat.D32Float => TextureFormat.Depth32float,
        GpuFormat.Depth24PlusStencil8 => TextureFormat.Depth24PlusStencil8,
        _ => throw new NotSupportedException($"WebGPU textures do not support {format}.")
    };

    private static TextureUsage ToWebGpuUsage(GpuTextureUsage usage)
    {
        TextureUsage result = 0;
        if ((usage & GpuTextureUsage.CopySource) != 0) { result |= TextureUsage.CopySrc; }
        if ((usage & GpuTextureUsage.CopyDestination) != 0) { result |= TextureUsage.CopyDst; }
        if ((usage & GpuTextureUsage.Sampled) != 0) { result |= TextureUsage.TextureBinding; }
        if ((usage & GpuTextureUsage.Storage) != 0) { result |= TextureUsage.StorageBinding; }
        if ((usage & (GpuTextureUsage.ColorAttachment | GpuTextureUsage.DepthStencilAttachment)) != 0)
        {
            result |= TextureUsage.RenderAttachment;
        }
        return result;
    }

    private static FilterMode ToWebGpuFilter(GpuSamplerFilter filter) => filter switch
    {
        GpuSamplerFilter.Nearest => FilterMode.Nearest,
        GpuSamplerFilter.Linear => FilterMode.Linear,
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    private static AddressMode ToWebGpuAddressMode(GpuSamplerAddressMode mode) => mode switch
    {
        GpuSamplerAddressMode.ClampToEdge => AddressMode.ClampToEdge,
        GpuSamplerAddressMode.Repeat => AddressMode.Repeat,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        InvalidateBindGroups();
        foreach (RasterPipelineRecord pipeline in rasterPipelines.Values) { pipeline.Dispose(api); }
        rasterPipelines.Clear();
        foreach (TextureViewRecord view in textureViews.Values) { api.TextureViewRelease((TextureView*)view.Handle); }
        textureViews.Clear();
        foreach (TextureRecord texture in textures.Values) { api.TextureRelease((Texture*)texture.Handle); }
        textures.Clear();
        foreach (nint sampler in samplers.Values) { api.SamplerRelease((Sampler*)sampler); }
        samplers.Clear();
        if (queue is not null) { api.QueueRelease(queue); queue = null; }
        if (device is not null) { api.DeviceRelease(device); device = null; }
        if (adapter is not null) { api.AdapterRelease(adapter); adapter = null; }
        if (instance is not null) { api.InstanceRelease(instance); instance = null; }
    }

    private void InvalidateBindGroups()
    {
        foreach (CachedBindGroup bindGroup in bindGroups.Values)
        {
            api.BindGroupRelease((BindGroup*)bindGroup.Handle);
        }
        bindGroups.Clear();
    }

    private readonly record struct ResourceTableCacheKey(GpuResourceTable Table, nint Layout);
    private sealed record CachedBindGroup(nint Handle, ulong Revision);
    private sealed record TextureRecord(nint Handle, GpuTextureDescription Description);
    private sealed record TextureViewRecord(nint Handle, GpuTextureHandle Texture);
}
