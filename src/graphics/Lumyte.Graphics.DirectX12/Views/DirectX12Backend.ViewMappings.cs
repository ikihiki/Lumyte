using Lumyte.Graphics.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    internal static ShaderResourceViewDesc SampledTextureDescription(NativeGpuTextureView view, uint samples)
    {
        var result = new ShaderResourceViewDesc
        {
            Format = ShaderViewFormat(view), Shader4ComponentMapping = NativeDefaultShaderComponentMapping,
        };
        uint plane = view.Aspect == NativeGpuTextureAspect.Stencil ? 1u : 0u;
        switch (view.Dimension)
        {
            case NativeGpuTextureViewDimension.OneD:
                RequireNoArrayRange(view);
                result.ViewDimension = SrvDimension.Texture1D;
                result.Texture1D = new() { MostDetailedMip = view.BaseMip, MipLevels = view.MipCount };
                break;
            case NativeGpuTextureViewDimension.TwoD:
                RequireNoArrayRange(view);
                if (samples > 1)
                {
                    RequireMultisampleMip(view);
                    result.ViewDimension = SrvDimension.Texture2Dms;
                }
                else
                {
                    result.ViewDimension = SrvDimension.Texture2D;
                    result.Texture2D = new() { MostDetailedMip = view.BaseMip, MipLevels = view.MipCount, PlaneSlice = plane };
                }
                break;
            case NativeGpuTextureViewDimension.TwoDArray:
                if (samples > 1)
                {
                    RequireMultisampleMip(view);
                    result.ViewDimension = SrvDimension.Texture2Dmsarray;
                    result.Texture2DMSArray = new() { FirstArraySlice = view.BaseLayer, ArraySize = view.LayerCount };
                }
                else
                {
                    result.ViewDimension = SrvDimension.Texture2Darray;
                    result.Texture2DArray = new() { MostDetailedMip = view.BaseMip, MipLevels = view.MipCount,
                        FirstArraySlice = view.BaseLayer, ArraySize = view.LayerCount, PlaneSlice = plane };
                }
                break;
            case NativeGpuTextureViewDimension.ThreeD:
                RequireNoArrayRange(view);
                result.ViewDimension = SrvDimension.Texture3D;
                result.Texture3D = new() { MostDetailedMip = view.BaseMip, MipLevels = view.MipCount };
                break;
            case NativeGpuTextureViewDimension.Cube:
                if (view.BaseLayer != 0 || view.LayerCount != 6) { throw UnrepresentableView("A cube SRV addresses the first six faces; use CubeArray to select other faces."); }
                result.ViewDimension = SrvDimension.Texturecube;
                result.TextureCube = new() { MostDetailedMip = view.BaseMip, MipLevels = view.MipCount };
                break;
            case NativeGpuTextureViewDimension.CubeArray:
                if (view.LayerCount % 6 != 0) { throw UnrepresentableView("A cube array face count must be representable as whole cubes."); }
                result.ViewDimension = SrvDimension.Texturecubearray;
                result.TextureCubeArray = new() { MostDetailedMip = view.BaseMip, MipLevels = view.MipCount,
                    First2DArrayFace = view.BaseLayer, NumCubes = view.LayerCount / 6 };
                break;
            default: throw new ArgumentOutOfRangeException(nameof(view));
        }
        return result;
    }

    internal static UnorderedAccessViewDesc StorageTextureDescription(NativeGpuTextureView view)
    {
        RequireSingleMip(view);
        var result = new UnorderedAccessViewDesc { Format = ShaderViewFormat(view) };
        uint plane = view.Aspect == NativeGpuTextureAspect.Stencil ? 1u : 0u;
        switch (view.Dimension)
        {
            case NativeGpuTextureViewDimension.OneD:
                RequireNoArrayRange(view);
                result.ViewDimension = UavDimension.Texture1D;
                result.Texture1D = new() { MipSlice = view.BaseMip };
                break;
            case NativeGpuTextureViewDimension.TwoD:
                RequireNoArrayRange(view);
                result.ViewDimension = UavDimension.Texture2D;
                result.Texture2D = new() { MipSlice = view.BaseMip, PlaneSlice = plane };
                break;
            case NativeGpuTextureViewDimension.TwoDArray:
                result.ViewDimension = UavDimension.Texture2Darray;
                result.Texture2DArray = new() { MipSlice = view.BaseMip, FirstArraySlice = view.BaseLayer,
                    ArraySize = view.LayerCount, PlaneSlice = plane };
                break;
            case NativeGpuTextureViewDimension.ThreeD:
                RequireNoArrayRange(view);
                result.ViewDimension = UavDimension.Texture3D;
                result.Texture3D = new() { MipSlice = view.BaseMip, FirstWSlice = 0, WSize = uint.MaxValue };
                break;
            default: throw UnrepresentableView("Direct3D 12 storage views have no cube dimension; use a 2D array view.");
        }
        return result;
    }

    internal static RenderTargetViewDesc RenderTargetDescription(NativeGpuTextureView view,
        NativeGpuTextureDescription texture, NativeGpuRenderViewFlags flags)
    {
        RequireSingleMip(view);
        if (flags != NativeGpuRenderViewFlags.None) { throw new ArgumentException("Color attachment views do not represent depth/stencil read-only flags.", nameof(flags)); }
        if (view.Aspect != NativeGpuTextureAspect.Color) { throw UnrepresentableView("A color attachment requires the color aspect."); }
        var result = new RenderTargetViewDesc { Format = TextureFormat(view.Format, false) };
        if (texture.Dimension == NativeGpuTextureDimension.ThreeD
            && view.Dimension is NativeGpuTextureViewDimension.TwoD or NativeGpuTextureViewDimension.TwoDArray)
        {
            if (view.Dimension == NativeGpuTextureViewDimension.TwoD && view.LayerCount != 1)
            {
                throw UnrepresentableView("A 2D slice view selects exactly one depth slice.");
            }
            result.ViewDimension = RtvDimension.Texture3D;
            result.Texture3D = new() { MipSlice = view.BaseMip, FirstWSlice = view.BaseLayer, WSize = view.LayerCount };
            return result;
        }
        switch (view.Dimension)
        {
            case NativeGpuTextureViewDimension.OneD:
                RequireNoArrayRange(view);
                result.ViewDimension = RtvDimension.Texture1D;
                result.Texture1D = new() { MipSlice = view.BaseMip };
                break;
            case NativeGpuTextureViewDimension.TwoD:
                RequireNoArrayRange(view);
                if (texture.SampleCount > 1) { RequireMultisampleMip(view); result.ViewDimension = RtvDimension.Texture2Dms; }
                else { result.ViewDimension = RtvDimension.Texture2D; result.Texture2D = new() { MipSlice = view.BaseMip }; }
                break;
            case NativeGpuTextureViewDimension.TwoDArray:
                if (texture.SampleCount > 1)
                {
                    RequireMultisampleMip(view);
                    result.ViewDimension = RtvDimension.Texture2Dmsarray;
                    result.Texture2DMSArray = new() { FirstArraySlice = view.BaseLayer, ArraySize = view.LayerCount };
                }
                else
                {
                    result.ViewDimension = RtvDimension.Texture2Darray;
                    result.Texture2DArray = new() { MipSlice = view.BaseMip, FirstArraySlice = view.BaseLayer, ArraySize = view.LayerCount };
                }
                break;
            case NativeGpuTextureViewDimension.ThreeD:
                RequireNoArrayRange(view);
                result.ViewDimension = RtvDimension.Texture3D;
                result.Texture3D = new() { MipSlice = view.BaseMip, FirstWSlice = 0, WSize = uint.MaxValue };
                break;
            default: throw UnrepresentableView("Direct3D 12 attachment views have no cube dimension; use a 2D array view.");
        }
        return result;
    }

    internal static DepthStencilViewDesc DepthStencilDescription(NativeGpuTextureView view, uint samples, NativeGpuRenderViewFlags flags)
    {
        RequireSingleMip(view);
        if ((flags & ~(NativeGpuRenderViewFlags.DepthReadOnly | NativeGpuRenderViewFlags.StencilReadOnly)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flags));
        }
        if (!((view.Format == GpuFormat.D32Float && view.Aspect == NativeGpuTextureAspect.Depth)
            || (view.Format == GpuFormat.Depth24PlusStencil8 && view.Aspect == NativeGpuTextureAspect.DepthStencil)))
        {
            throw UnrepresentableView("A Direct3D 12 depth/stencil attachment exposes every aspect of its depth/stencil format.");
        }
        var result = new DepthStencilViewDesc
        {
            Format = TextureFormat(view.Format, false),
            Flags = ((flags & NativeGpuRenderViewFlags.DepthReadOnly) != 0 ? DsvFlags.ReadOnlyDepth : DsvFlags.None)
                | ((flags & NativeGpuRenderViewFlags.StencilReadOnly) != 0 ? DsvFlags.ReadOnlyStencil : DsvFlags.None),
        };
        switch (view.Dimension)
        {
            case NativeGpuTextureViewDimension.OneD:
                RequireNoArrayRange(view);
                result.ViewDimension = DsvDimension.Texture1D;
                result.Texture1D = new() { MipSlice = view.BaseMip };
                break;
            case NativeGpuTextureViewDimension.TwoD:
                RequireNoArrayRange(view);
                if (samples > 1) { RequireMultisampleMip(view); result.ViewDimension = DsvDimension.Texture2Dms; }
                else { result.ViewDimension = DsvDimension.Texture2D; result.Texture2D = new() { MipSlice = view.BaseMip }; }
                break;
            case NativeGpuTextureViewDimension.TwoDArray:
                if (samples > 1)
                {
                    RequireMultisampleMip(view);
                    result.ViewDimension = DsvDimension.Texture2Dmsarray;
                    result.Texture2DMSArray = new() { FirstArraySlice = view.BaseLayer, ArraySize = view.LayerCount };
                }
                else
                {
                    result.ViewDimension = DsvDimension.Texture2Darray;
                    result.Texture2DArray = new() { MipSlice = view.BaseMip, FirstArraySlice = view.BaseLayer, ArraySize = view.LayerCount };
                }
                break;
            default: throw UnrepresentableView("Direct3D 12 depth/stencil views have no 3D or cube dimension.");
        }
        return result;
    }

    private static Format ShaderViewFormat(NativeGpuTextureView view) => (view.Format, view.Aspect) switch
    {
        (GpuFormat.D32Float, NativeGpuTextureAspect.Depth) => Format.FormatR32Float,
        (GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Depth) => Format.FormatR24UnormX8Typeless,
        (GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Stencil) => Format.FormatX24TypelessG8Uint,
        (_, NativeGpuTextureAspect.Color) when view.Format is not GpuFormat.D32Float and not GpuFormat.Depth24PlusStencil8 => TextureFormat(view.Format, false),
        _ => throw UnrepresentableView("A shader texture descriptor must select one representable format aspect."),
    };

    private static void RequireSingleMip(NativeGpuTextureView view)
    {
        if (view.MipCount != 1) { throw UnrepresentableView("This native view can select only one mip."); }
    }

    private static void RequireNoArrayRange(NativeGpuTextureView view)
    {
        if (view.BaseLayer != 0 || view.LayerCount != 1) { throw UnrepresentableView("This native view has no array range."); }
    }

    private static void RequireMultisampleMip(NativeGpuTextureView view)
    {
        if (view.BaseMip != 0 || view.MipCount != 1) { throw UnrepresentableView("A multisample view cannot represent a mip range."); }
    }

    private static ArgumentException UnrepresentableView(string message, string parameterName = "view")
        => new(message, parameterName);
}
