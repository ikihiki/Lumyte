using System.Runtime.InteropServices.JavaScript;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private readonly record struct TextureViewKey(P.GpuTextureView View, uint Usage);
    private sealed class TextureViewLease(TextureViewKey key, JSObject handle, Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics)
    {
        internal readonly TextureViewKey Key = key;
        internal readonly JSObject Handle = handle;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal int References = 1;
    }
    private readonly Dictionary<TextureViewKey, TextureViewLease> textureViews = [];
    private long textureViewCreations;

    private TextureViewLease AcquireTextureView(P.GpuTextureView view, uint usage)
    {
        TextureResource texture = RequireTexture(view.Texture);
        P.GpuTextureViewDescription resolved = ResolveTextureView(view.Description, texture.Description);
        var key = new TextureViewKey(new(view.Texture, resolved), usage);
        if (textureViews.TryGetValue(key, out TextureViewLease? existing))
        {
            existing.References = checked(existing.References + 1);
            return existing;
        }
        var created = CreateObject(texture.Handle, "textureView", new
        {
            Format = MapViewFormat(resolved.Format!.Value, resolved.Aspect),
            Dimension = MapViewDimension(resolved.Dimension!.Value),
            Aspect = MapTextureAspect(resolved.Aspect),
            BaseMipLevel = resolved.BaseMip,
            MipLevelCount = resolved.MipCount,
            BaseArrayLayer = resolved.BaseLayer,
            ArrayLayerCount = resolved.LayerCount,
            Usage = usage,
        }, []);
        try
        {
            var lease = new TextureViewLease(key, created.Handle,
                BrowserDiagnostics.CombineAsync(texture.Diagnostics, created.Diagnostics));
            textureViews.Add(key, lease);
            textureViewCreations++;
            return lease;
        }
        catch { created.Handle.Dispose(); throw; }
    }

    private void ReleaseTextureView(TextureViewLease lease)
    {
        if (--lease.References != 0) { return; }
        textureViews.Remove(lease.Key);
        lease.Handle.Dispose();
    }

    private static P.GpuTextureViewDescription ResolveTextureView(P.GpuTextureViewDescription view, P.GpuTextureDescription texture)
    {
        P.GpuTextureViewDimension dimension = view.Dimension ?? texture.Dimension switch
        {
            P.GpuTextureDimension.Texture1D => P.GpuTextureViewDimension.Texture1D,
            P.GpuTextureDimension.Texture2D => texture.LayerCount > 1 ? P.GpuTextureViewDimension.Texture2DArray : P.GpuTextureViewDimension.Texture2D,
            P.GpuTextureDimension.Texture3D => P.GpuTextureViewDimension.Texture3D,
            _ => throw new ArgumentOutOfRangeException(nameof(texture)),
        };
        // Only omitted values are resolved; explicit limits remain browser validation.
        uint? mipCount = view.MipCount ?? (view.BaseMip <= texture.MipCount ? texture.MipCount - view.BaseMip : null);
        uint? layerCount = view.LayerCount ?? dimension switch
        {
            P.GpuTextureViewDimension.Texture1D or P.GpuTextureViewDimension.Texture2D or P.GpuTextureViewDimension.Texture3D => 1u,
            P.GpuTextureViewDimension.Cube => 6u,
            P.GpuTextureViewDimension.Texture2DArray or P.GpuTextureViewDimension.CubeArray =>
                view.BaseLayer <= texture.LayerCount ? texture.LayerCount - view.BaseLayer : null,
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };
        return view with { Format = view.Format ?? texture.Format, Dimension = dimension, MipCount = mipCount, LayerCount = layerCount };
    }

    private static string MapViewFormat(GpuFormat format, P.GpuTextureAspect aspect)
    {
        if (format == GpuFormat.Depth24PlusStencil8)
        {
            if (aspect == P.GpuTextureAspect.DepthOnly) { return "depth24plus"; }
            if (aspect == P.GpuTextureAspect.StencilOnly) { return "stencil8"; }
        }
        return MapTextureFormat(format);
    }

    private static string MapViewDimension(P.GpuTextureViewDimension dimension) => dimension switch
    {
        P.GpuTextureViewDimension.Texture1D => "1d",
        P.GpuTextureViewDimension.Texture2D => "2d",
        P.GpuTextureViewDimension.Texture2DArray => "2d-array",
        P.GpuTextureViewDimension.Cube => "cube",
        P.GpuTextureViewDimension.CubeArray => "cube-array",
        P.GpuTextureViewDimension.Texture3D => "3d",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

    private static string MapTextureAspect(P.GpuTextureAspect aspect) => aspect switch
    {
        P.GpuTextureAspect.All => "all",
        P.GpuTextureAspect.DepthOnly => "depth-only",
        P.GpuTextureAspect.StencilOnly => "stencil-only",
        _ => throw new ArgumentOutOfRangeException(nameof(aspect)),
    };
}
