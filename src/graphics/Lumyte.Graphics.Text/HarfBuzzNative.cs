using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Text;

internal static unsafe partial class HarfBuzzNative
{
#if __IOS__ || __TVOS__
    private const string LibraryName = "@rpath/libHarfBuzzSharp.framework/libHarfBuzzSharp";
#else
    private const string LibraryName = "libHarfBuzzSharp";
#endif

    [LibraryImport(LibraryName, EntryPoint = "hb_draw_funcs_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint CreateDrawFunctions();

    [LibraryImport(LibraryName, EntryPoint = "hb_draw_funcs_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void DestroyDrawFunctions(nint functions);

    [LibraryImport(LibraryName, EntryPoint = "hb_draw_funcs_make_immutable")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void MakeDrawFunctionsImmutable(nint functions);

    [LibraryImport(LibraryName, EntryPoint = "hb_draw_funcs_set_move_to_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetMoveTo(nint functions, nint callback, nint userData, nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_draw_funcs_set_line_to_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetLineTo(nint functions, nint callback, nint userData, nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_draw_funcs_set_quadratic_to_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetQuadraticTo(nint functions, nint callback, nint userData, nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_draw_funcs_set_cubic_to_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetCubicTo(nint functions, nint callback, nint userData, nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_draw_funcs_set_close_path_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetClosePath(nint functions, nint callback, nint userData, nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_font_draw_glyph_or_fail")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int DrawGlyph(nint font, uint glyphId, nint functions, nint drawData);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint CreatePaintFunctions();

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void DestroyPaintFunctions(nint functions);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_make_immutable")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void MakePaintFunctionsImmutable(nint functions);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_push_transform_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintPushTransform(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_pop_transform_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintPopTransform(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_color_glyph_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintColorGlyph(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_push_clip_glyph_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintPushClipGlyph(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_push_clip_rectangle_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintPushClipRectangle(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_push_clip_path_start_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintPushClipPathStart(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_push_clip_path_end_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintPushClipPathEnd(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_pop_clip_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintPopClip(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_color_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintColor(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_image_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintImage(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_linear_gradient_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintLinearGradient(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_radial_gradient_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintRadialGradient(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_sweep_gradient_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintSweepGradient(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_push_group_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintPushGroup(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_push_group_for_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintPushGroupFor(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_pop_group_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintPopGroup(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_paint_funcs_set_custom_palette_color_func")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPaintCustomPaletteColor(
        nint functions,
        nint callback,
        nint userData,
        nint destroy);

    [LibraryImport(LibraryName, EntryPoint = "hb_font_paint_glyph_or_fail")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int PaintGlyph(
        nint font,
        uint glyphId,
        nint functions,
        nint paintData,
        uint paletteIndex,
        uint foreground);

    [LibraryImport(LibraryName, EntryPoint = "hb_color_line_get_color_stops")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint GetColorStops(
        nint colorLine,
        uint startOffset,
        uint* colorStopCount,
        HarfBuzzColorStop* colorStops);

    [LibraryImport(LibraryName, EntryPoint = "hb_color_line_get_extend")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial HarfBuzzPaintExtend GetColorLineExtend(nint colorLine);

    [LibraryImport(LibraryName, EntryPoint = "hb_ot_color_has_paint")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int HasColorPaint(nint face);

    [LibraryImport(LibraryName, EntryPoint = "hb_ot_color_glyph_get_layers")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint GetColorLayers(
        nint face,
        uint glyphId,
        uint startOffset,
        uint* layerCount,
        HarfBuzzColorLayer* layers);

    [LibraryImport(LibraryName, EntryPoint = "hb_font_set_ppem")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetPixelsPerEm(nint font, uint horizontal, uint vertical);

    [LibraryImport(LibraryName, EntryPoint = "hb_ot_color_glyph_reference_png")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint ReferenceColorPng(nint font, uint glyphId);

    [LibraryImport(LibraryName, EntryPoint = "hb_blob_get_data")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial byte* GetBlobData(nint blob, uint* length);

    [LibraryImport(LibraryName, EntryPoint = "hb_blob_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void DestroyBlob(nint blob);
}

[StructLayout(LayoutKind.Sequential)]
internal struct HarfBuzzColorStop
{
    internal float Offset;
    internal int IsForeground;
    internal uint Color;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HarfBuzzGlyphExtents
{
    internal int XBearing;
    internal int YBearing;
    internal int Width;
    internal int Height;
}

internal enum HarfBuzzPaintExtend
{
    Pad = 0,
    Repeat = 1,
    Reflect = 2,
}

internal enum HarfBuzzPaintCompositeMode
{
    Clear = 0,
    Source = 1,
    Destination = 2,
    SourceOver = 3,
    DestinationOver = 4,
    SourceIn = 5,
    DestinationIn = 6,
    SourceOut = 7,
    DestinationOut = 8,
    SourceAtop = 9,
    DestinationAtop = 10,
    Xor = 11,
    Plus = 12,
    Screen = 13,
    Overlay = 14,
    Darken = 15,
    Lighten = 16,
    ColorDodge = 17,
    ColorBurn = 18,
    HardLight = 19,
    SoftLight = 20,
    Difference = 21,
    Exclusion = 22,
    Multiply = 23,
    HslHue = 24,
    HslSaturation = 25,
    HslColor = 26,
    HslLuminosity = 27,
}
