using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private sealed class TextureResource(WebGpuBackend owner, F.TextureHandle handle,
        P.GpuTextureDescription description, Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuTextureHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly F.TextureHandle Handle = handle;
        internal readonly P.GpuTextureDescription Description = description;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    public unsafe P.GpuTextureHandle CreateTexture(P.GpuTextureDescription description)
    {
        N.TextureDimension dimension = description.Dimension switch
        {
            P.GpuTextureDimension.Texture1D => N.TextureDimension.D1,
            P.GpuTextureDimension.Texture2D => N.TextureDimension.D2,
            P.GpuTextureDimension.Texture3D => N.TextureDimension.D3,
            _ => throw new ArgumentOutOfRangeException(nameof(description)),
        };
        // WebGPU has one depth-or-layers field. Reject only the value that would otherwise disappear in translation.
        if (dimension == N.TextureDimension.D3 ? description.LayerCount != 1 : description.Depth != 1)
        { throw new ArgumentException("WebGPU textures use depth for 3D or array layers for 1D/2D; the unused value must be one.", nameof(description)); }
        N.TextureFormat format = MapTextureFormat(description.Format);
        N.TextureUsage usage = MapTextureUsage(description.Usage);
        N.TextureFormat alternate = description.MutableFormat ? AlternateViewFormat(format) : N.TextureFormat.Undefined;
        var native = new F.TextureDescriptorFFI
        {
            Dimension = dimension,
            Size = new()
            {
                Width = description.Width,
                Height = description.Height,
                DepthOrArrayLayers = dimension == N.TextureDimension.D3 ? description.Depth : description.LayerCount,
            },
            MipLevelCount = description.MipCount,
            SampleCount = description.SampleCount,
            Format = format,
            Usage = usage,
            ViewFormats = alternate == N.TextureFormat.Undefined ? null : &alternate,
            ViewFormatCount = alternate == N.TextureFormat.Undefined ? 0u : 1u,
        };
        lock (gate)
        {
            RequireAvailable();
            F.TextureHandle texture = default;
            try
            {
                PushScopes();
                Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics;
                try { texture = F.WebGPU_FFI.DeviceCreateTexture(device, &native); }
                finally { diagnostics = PopScopes(); }
                if ((nuint)texture == 0)
                {
                    status.Lose("WebGPU texture creation returned no object.");
                    status.ThrowIfFailed();
                }
                return new TextureResource(this, texture, description, diagnostics);
            }
            catch
            {
                if ((nuint)texture != 0)
                {
                    F.WebGPU_FFI.TextureDestroy(texture);
                    F.WebGPU_FFI.TextureRelease(texture);
                }
                throw;
            }
        }
    }

    public void DestroyTexture(P.GpuTextureHandle texture)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            TextureResource resource = RequireTexture(texture);
            resource.Destroyed = true;
            F.WebGPU_FFI.TextureDestroy(resource.Handle);
            F.WebGPU_FFI.TextureRelease(resource.Handle);
        }
    }

    private TextureResource RequireTexture(P.GpuTextureHandle texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (texture is not TextureResource resource || !ReferenceEquals(resource.Owner, this))
        { throw new ArgumentException("Texture belongs to another device.", nameof(texture)); }
        ObjectDisposedException.ThrowIf(resource.Destroyed, texture);
        return resource;
    }

    internal Task<IReadOnlyList<P.GpuDiagnostic>> GetCreationDiagnostics(P.GpuTextureHandle texture)
    {
        lock (gate) { return RequireTexture(texture).Diagnostics; }
    }

    internal static N.TextureFormat MapTextureFormat(GpuFormat format) => format switch
    {
        GpuFormat.Rgba8Unorm => N.TextureFormat.RGBA8Unorm,
        GpuFormat.Rgba8UnormSrgb => N.TextureFormat.RGBA8UnormSrgb,
        GpuFormat.Bgra8Unorm => N.TextureFormat.BGRA8Unorm,
        GpuFormat.Bgra8UnormSrgb => N.TextureFormat.BGRA8UnormSrgb,
        GpuFormat.R8Unorm => N.TextureFormat.R8Unorm,
        GpuFormat.Rg8Unorm => N.TextureFormat.RG8Unorm,
        GpuFormat.Rgba16Float => N.TextureFormat.RGBA16Float,
        GpuFormat.R32Float => N.TextureFormat.R32Float,
        GpuFormat.D32Float => N.TextureFormat.Depth32Float,
        GpuFormat.Depth24PlusStencil8 => N.TextureFormat.Depth24PlusStencil8,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    internal static N.TextureFormat AlternateViewFormat(N.TextureFormat format) => format switch
    {
        N.TextureFormat.RGBA8Unorm => N.TextureFormat.RGBA8UnormSrgb,
        N.TextureFormat.RGBA8UnormSrgb => N.TextureFormat.RGBA8Unorm,
        N.TextureFormat.BGRA8Unorm => N.TextureFormat.BGRA8UnormSrgb,
        N.TextureFormat.BGRA8UnormSrgb => N.TextureFormat.BGRA8Unorm,
        _ => N.TextureFormat.Undefined,
    };

    internal static N.TextureUsage MapTextureUsage(P.GpuTextureUsage usage)
    {
        const P.GpuTextureUsage known = P.GpuTextureUsage.Sampled | P.GpuTextureUsage.Storage
            | P.GpuTextureUsage.ColorAttachment | P.GpuTextureUsage.DepthStencilAttachment
            | P.GpuTextureUsage.CopySource | P.GpuTextureUsage.CopyDestination;
        if ((usage & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(usage)); }
        N.TextureUsage result = 0;
        if ((usage & P.GpuTextureUsage.Sampled) != 0) { result |= N.TextureUsage.TextureBinding; }
        if ((usage & P.GpuTextureUsage.Storage) != 0) { result |= N.TextureUsage.StorageBinding; }
        if ((usage & (P.GpuTextureUsage.ColorAttachment | P.GpuTextureUsage.DepthStencilAttachment)) != 0)
        { result |= N.TextureUsage.RenderAttachment; }
        if ((usage & P.GpuTextureUsage.CopySource) != 0) { result |= N.TextureUsage.CopySrc; }
        if ((usage & P.GpuTextureUsage.CopyDestination) != 0) { result |= N.TextureUsage.CopyDst; }
        return result;
    }
}
