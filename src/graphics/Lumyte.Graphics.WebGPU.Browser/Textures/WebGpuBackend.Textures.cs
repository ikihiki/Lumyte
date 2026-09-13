using System.Runtime.InteropServices.JavaScript;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private sealed class TextureResource(WebGpuBackend owner, JSObject handle, P.GpuTextureDescription description,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuTextureHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly JSObject Handle = handle;
        internal readonly P.GpuTextureDescription Description = description;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    public P.GpuTextureHandle CreateTexture(P.GpuTextureDescription description)
    {
        RequireAvailable();
        string dimension = description.Dimension switch
        {
            P.GpuTextureDimension.Texture1D => "1d",
            P.GpuTextureDimension.Texture2D => "2d",
            P.GpuTextureDimension.Texture3D => "3d",
            _ => throw new ArgumentOutOfRangeException(nameof(description)),
        };
        if (description.Dimension == P.GpuTextureDimension.Texture3D ? description.LayerCount != 1 : description.Depth != 1)
        { throw new ArgumentException("WebGPU textures use depth for 3D or array layers for 1D/2D; the unused value must be one.", nameof(description)); }
        string format = MapTextureFormat(description.Format);
        string? alternate = description.MutableFormat ? AlternateViewFormat(format) : null;
        var created = CreateObject(device, "texture", new
        {
            Dimension = dimension,
            Size = new { description.Width, description.Height,
                DepthOrArrayLayers = description.Dimension == P.GpuTextureDimension.Texture3D ? description.Depth : description.LayerCount },
            MipLevelCount = description.MipCount,
            description.SampleCount,
            Format = format,
            Usage = MapTextureUsage(description.Usage),
            ViewFormats = alternate is null ? Array.Empty<string>() : [alternate],
        }, []);
        try { return new TextureResource(this, created.Handle, description, created.Diagnostics); }
        catch
        {
            try { BrowserInterop.Destroy(created.Handle); }
            finally { created.Handle.Dispose(); }
            throw;
        }
    }

    public void DestroyTexture(P.GpuTextureHandle texture)
    {
        runtime.RequireThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        TextureResource resource = RequireTexture(texture);
        resource.Destroyed = true;
        try { BrowserInterop.Destroy(resource.Handle); }
        finally { resource.Handle.Dispose(); }
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
    { runtime.RequireThread(); return RequireTexture(texture).Diagnostics; }

    private static string MapTextureFormat(GpuFormat format) => format switch
    {
        GpuFormat.Rgba8Unorm => "rgba8unorm",
        GpuFormat.Rgba8UnormSrgb => "rgba8unorm-srgb",
        GpuFormat.Bgra8Unorm => "bgra8unorm",
        GpuFormat.Bgra8UnormSrgb => "bgra8unorm-srgb",
        GpuFormat.R8Unorm => "r8unorm",
        GpuFormat.Rg8Unorm => "rg8unorm",
        GpuFormat.R32Float => "r32float",
        GpuFormat.D32Float => "depth32float",
        GpuFormat.Depth24PlusStencil8 => "depth24plus-stencil8",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static string? AlternateViewFormat(string format) => format switch
    {
        "rgba8unorm" => "rgba8unorm-srgb",
        "rgba8unorm-srgb" => "rgba8unorm",
        "bgra8unorm" => "bgra8unorm-srgb",
        "bgra8unorm-srgb" => "bgra8unorm",
        _ => null,
    };

    private static uint MapTextureUsage(P.GpuTextureUsage usage)
    {
        const P.GpuTextureUsage known = P.GpuTextureUsage.Sampled | P.GpuTextureUsage.Storage
            | P.GpuTextureUsage.ColorAttachment | P.GpuTextureUsage.DepthStencilAttachment
            | P.GpuTextureUsage.CopySource | P.GpuTextureUsage.CopyDestination;
        if ((usage & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(usage)); }
        uint result = 0;
        if ((usage & P.GpuTextureUsage.CopySource) != 0) { result |= 1; }
        if ((usage & P.GpuTextureUsage.CopyDestination) != 0) { result |= 2; }
        if ((usage & P.GpuTextureUsage.Sampled) != 0) { result |= 4; }
        if ((usage & P.GpuTextureUsage.Storage) != 0) { result |= 8; }
        if ((usage & (P.GpuTextureUsage.ColorAttachment | P.GpuTextureUsage.DepthStencilAttachment)) != 0) { result |= 16; }
        return result;
    }
}
