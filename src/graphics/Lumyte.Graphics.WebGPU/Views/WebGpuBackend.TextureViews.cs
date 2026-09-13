using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private readonly record struct TextureViewKey(P.GpuTextureView View, N.TextureUsage Usage);

    private sealed class TextureViewLease(TextureViewKey key, F.TextureViewHandle handle,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics)
    {
        internal readonly TextureViewKey Key = key;
        internal readonly F.TextureViewHandle Handle = handle;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal int References = 1;
    }

    private readonly Dictionary<TextureViewKey, TextureViewLease> textureViews = new();
    private long textureViewCreations;

    private unsafe TextureViewLease AcquireTextureView(P.GpuTextureView view, N.TextureUsage usage)
    {
        TextureResource texture = RequireTexture(view.Texture);
        P.GpuTextureViewDescription resolved = ResolveTextureView(view.Description, texture.Description);
        var key = new TextureViewKey(new(view.Texture, resolved), usage);
        if (textureViews.TryGetValue(key, out TextureViewLease? existing))
        {
            existing.References = checked(existing.References + 1);
            return existing;
        }
        var description = new F.TextureViewDescriptorFFI
        {
            Format = MapViewFormat(resolved.Format!.Value, resolved.Aspect),
            Dimension = MapViewDimension(resolved.Dimension!.Value),
            Aspect = MapTextureAspect(resolved.Aspect),
            BaseMipLevel = resolved.BaseMip,
            MipLevelCount = resolved.MipCount ?? F.WebGPU_FFI.MIP_LEVEL_COUNT_UNDEFINED,
            BaseArrayLayer = resolved.BaseLayer,
            ArrayLayerCount = resolved.LayerCount ?? F.WebGPU_FFI.ARRAY_LAYER_COUNT_UNDEFINED,
            Usage = usage,
        };
        F.TextureViewHandle handle = default;
        try
        {
            PushScopes();
            Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics;
            try { handle = F.WebGPU_FFI.TextureCreateView(texture.Handle, &description); }
            finally { diagnostics = PopScopes(); }
            if ((nuint)handle == 0)
            {
                status.Lose("WebGPU texture view creation returned no object.");
                status.ThrowIfFailed();
            }
            textureViewCreations++;
            var lease = new TextureViewLease(key, handle, WebGpuDiagnostics.CombineAsync(texture.Diagnostics, diagnostics));
            textureViews.Add(key, lease);
            return lease;
        }
        catch
        {
            if ((nuint)handle != 0) { F.WebGPU_FFI.TextureViewRelease(handle); }
            throw;
        }
    }

    private void ReleaseTextureView(TextureViewLease lease)
    {
        if (--lease.References != 0) { return; }
        textureViews.Remove(lease.Key);
        F.WebGPU_FFI.TextureViewRelease(lease.Handle);
    }

    private static P.GpuTextureViewDescription ResolveTextureView(
        P.GpuTextureViewDescription view, P.GpuTextureDescription texture)
    {
        uint? explicitMipCount = MapViewCount(view.MipCount, nameof(view.MipCount));
        uint? explicitLayerCount = MapViewCount(view.LayerCount, nameof(view.LayerCount));
        P.GpuTextureViewDimension dimension = view.Dimension ?? texture.Dimension switch
        {
            P.GpuTextureDimension.Texture1D => P.GpuTextureViewDimension.Texture1D,
            P.GpuTextureDimension.Texture2D => texture.LayerCount > 1
                ? P.GpuTextureViewDimension.Texture2DArray : P.GpuTextureViewDimension.Texture2D,
            P.GpuTextureDimension.Texture3D => P.GpuTextureViewDimension.Texture3D,
            _ => throw new ArgumentOutOfRangeException(nameof(texture)),
        };
        // Resolve only omitted values. Explicit out-of-range values still reach native validation.
        // If a default's subtraction would underflow, the native undefined sentinel preserves that validation path.
        uint? mipCount = explicitMipCount ?? (view.BaseMip <= texture.MipCount ? texture.MipCount - view.BaseMip : null);
        uint? layerCount = explicitLayerCount ?? dimension switch
        {
            P.GpuTextureViewDimension.Texture1D or P.GpuTextureViewDimension.Texture2D or P.GpuTextureViewDimension.Texture3D => 1u,
            P.GpuTextureViewDimension.Cube => 6u,
            P.GpuTextureViewDimension.Texture2DArray or P.GpuTextureViewDimension.CubeArray =>
                view.BaseLayer <= texture.LayerCount ? texture.LayerCount - view.BaseLayer : null,
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };
        return view with { Format = view.Format ?? texture.Format, Dimension = dimension, MipCount = mipCount, LayerCount = layerCount };
    }

    private static uint? MapViewCount(uint? count, string name)
    {
        if (count == uint.MaxValue)
        { throw new ArgumentOutOfRangeException(name, "An explicit count cannot use WebGPU's undefined sentinel."); }
        return count;
    }

    private static N.TextureFormat MapViewFormat(GpuFormat format, P.GpuTextureAspect aspect)
    {
        // Portable keeps the logical packed format together with its aspect. WebGPU names each aspect's format separately.
        if (format == GpuFormat.Depth24PlusStencil8)
        {
            if (aspect == P.GpuTextureAspect.DepthOnly) { return N.TextureFormat.Depth24Plus; }
            if (aspect == P.GpuTextureAspect.StencilOnly) { return N.TextureFormat.Stencil8; }
        }
        return MapTextureFormat(format);
    }

    private static N.TextureViewDimension MapViewDimension(P.GpuTextureViewDimension dimension) => dimension switch
    {
        P.GpuTextureViewDimension.Texture1D => N.TextureViewDimension.D1,
        P.GpuTextureViewDimension.Texture2D => N.TextureViewDimension.D2,
        P.GpuTextureViewDimension.Texture2DArray => N.TextureViewDimension.D2Array,
        P.GpuTextureViewDimension.Cube => N.TextureViewDimension.Cube,
        P.GpuTextureViewDimension.CubeArray => N.TextureViewDimension.CubeArray,
        P.GpuTextureViewDimension.Texture3D => N.TextureViewDimension.D3,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

    private static N.TextureAspect MapTextureAspect(P.GpuTextureAspect aspect) => aspect switch
    {
        P.GpuTextureAspect.All => N.TextureAspect.All,
        P.GpuTextureAspect.DepthOnly => N.TextureAspect.DepthOnly,
        P.GpuTextureAspect.StencilOnly => N.TextureAspect.StencilOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(aspect)),
    };
}
