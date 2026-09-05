using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>Copies a HarfBuzz COLRv1 paint graph into an immutable managed operation stream.</summary>
internal static unsafe class HarfBuzzPaintReader
{
    private const uint MaximumColorStopCount = 4_096;
    private const uint OpaqueWhite = uint.MaxValue;

    private static readonly HarfBuzzPaintFunctionsHandle PaintFunctions = CreateFunctions();

    internal static bool TryRead(
        nint font,
        uint glyphId,
        uint paletteIndex,
        out ColorPaintGlyph? glyph)
    {
        if (font == 0)
        {
            throw new ArgumentException("A valid HarfBuzz font is required.", nameof(font));
        }

        var sink = new HarfBuzzPaintSink();
        GCHandle sinkHandle = GCHandle.Alloc(sink);
        int result;
        try
        {
            result = HarfBuzzNative.PaintGlyph(
                font,
                glyphId,
                PaintFunctions.DangerousGetHandle(),
                GCHandle.ToIntPtr(sinkHandle),
                paletteIndex,
                OpaqueWhite);
        }
        finally
        {
            sinkHandle.Free();
        }

        if (sink.Failure is Exception failure)
        {
            ColorPaintFailure.RethrowIfFatal(failure);
            glyph = null;
            return false;
        }

        glyph = null;
        if (result == 0)
        {
            return false;
        }

        try
        {
            return sink.TryBuild(out glyph);
        }
        catch (Exception exception) when (!ColorPaintFailure.IsFatal(exception))
        {
            glyph = null;
            return false;
        }
    }

    private static HarfBuzzPaintFunctionsHandle CreateFunctions()
    {
        nint functions = HarfBuzzNative.CreatePaintFunctions();
        var handle = new HarfBuzzPaintFunctionsHandle(functions);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new InvalidOperationException("HarfBuzz could not allocate glyph paint functions.");
        }

        try
        {
            HarfBuzzNative.SetPaintPushTransform(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, float, float, float, float, float, float, nint, void>)&PushTransform,
                0,
                0);
            HarfBuzzNative.SetPaintPopTransform(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&PopTransform,
                0,
                0);
            HarfBuzzNative.SetPaintPushClipGlyph(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, nint, nint, void>)&PushClipGlyph,
                0,
                0);
            HarfBuzzNative.SetPaintPushClipRectangle(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, float, float, float, float, nint, void>)&PushClipRectangle,
                0,
                0);
            HarfBuzzNative.SetPaintPopClip(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&PopClip,
                0,
                0);
            HarfBuzzNative.SetPaintColor(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, uint, nint, void>)&PaintColor,
                0,
                0);
            HarfBuzzNative.SetPaintLinearGradient(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, float, float, float, float, float, float, nint, void>)&PaintLinearGradient,
                0,
                0);
            HarfBuzzNative.SetPaintRadialGradient(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, float, float, float, float, float, float, nint, void>)&PaintRadialGradient,
                0,
                0);
            HarfBuzzNative.SetPaintSweepGradient(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, float, float, float, float, nint, void>)&PaintSweepGradient,
                0,
                0);
            HarfBuzzNative.SetPaintPushGroup(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&PushGroup,
                0,
                0);
            HarfBuzzNative.SetPaintPushGroupFor(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, nint, void>)&PushGroupFor,
                0,
                0);
            HarfBuzzNative.SetPaintPopGroup(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, nint, void>)&PopGroup,
                0,
                0);
            HarfBuzzNative.MakePaintFunctionsImmutable(functions);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PushTransform(
        nint functions,
        nint paintData,
        float xx,
        float yx,
        float xy,
        float yy,
        float dx,
        float dy,
        nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            Matrix3x2 transform = ToYDownTransform(xx, yx, xy, yy, dx, dy);
            sink.Push(
                HarfBuzzPaintStackKind.Transform,
                new ColorPaintPushTransform(transform),
                emit: !transform.IsIdentity);
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PopTransform(nint functions, nint paintData, nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            sink.Pop(HarfBuzzPaintStackKind.Transform, new ColorPaintPopTransform());
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PushClipGlyph(
        nint functions,
        nint paintData,
        uint glyphId,
        nint font,
        nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            if (!HarfBuzzPaintOutlineReader.TryRead(font, glyphId, out PathGeometry? clipPath))
            {
                throw new InvalidOperationException("A COLRv1 clip glyph outline could not be read.");
            }
            sink.Push(
                HarfBuzzPaintStackKind.Clip,
                new ColorPaintPushClipGlyph(glyphId, clipPath));
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PushClipRectangle(
        nint functions,
        nint paintData,
        float minimumX,
        float minimumY,
        float maximumX,
        float maximumY,
        nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            ValidateFinite(minimumX, minimumY, maximumX, maximumY);
            if (maximumX < minimumX || maximumY < minimumY)
            {
                throw new InvalidOperationException("A COLRv1 clip rectangle has reversed bounds.");
            }
            sink.Push(
                HarfBuzzPaintStackKind.Clip,
                new ColorPaintPushClipRectangle(new(
                    minimumX,
                    -maximumY,
                    maximumX - minimumX,
                    maximumY - minimumY)));
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PopClip(nint functions, nint paintData, nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            sink.Pop(HarfBuzzPaintStackKind.Clip, new ColorPaintPopClip());
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintColor(
        nint functions,
        nint paintData,
        int isForeground,
        uint color,
        nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            sink.Add(new ColorPaintSolid(ToColor(color, isForeground != 0)));
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintLinearGradient(
        nint functions,
        nint paintData,
        nint colorLine,
        float x0,
        float y0,
        float x1,
        float y1,
        float x2,
        float y2,
        nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            ValidateFinite(x0, y0, x1, y1, x2, y2);
            sink.Add(new ColorPaintLinearGradient(
                CopyColorLine(colorLine),
                Point(x0, y0),
                Point(x1, y1),
                Point(x2, y2)));
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintRadialGradient(
        nint functions,
        nint paintData,
        nint colorLine,
        float x0,
        float y0,
        float radius0,
        float x1,
        float y1,
        float radius1,
        nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            ValidateFinite(x0, y0, radius0, x1, y1, radius1);
            sink.Add(new ColorPaintRadialGradient(
                CopyColorLine(colorLine),
                Point(x0, y0),
                radius0,
                Point(x1, y1),
                radius1));
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintSweepGradient(
        nint functions,
        nint paintData,
        nint colorLine,
        float centerX,
        float centerY,
        float startAngle,
        float endAngle,
        nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            ValidateFinite(centerX, centerY, startAngle, endAngle);
            sink.Add(new ColorPaintSweepGradient(
                CopyColorLine(colorLine),
                Point(centerX, centerY),
                -startAngle,
                -endAngle));
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PushGroup(nint functions, nint paintData, nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            sink.Push(HarfBuzzPaintStackKind.Group, new ColorPaintPushGroup(null));
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PushGroupFor(nint functions, nint paintData, int mode, nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            sink.Push(
                HarfBuzzPaintStackKind.Group,
                new ColorPaintPushGroup(ToCompositeMode(mode)));
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PopGroup(nint functions, nint paintData, int mode, nint userData)
    {
        HarfBuzzPaintSink? sink = GetActiveSink(paintData);
        if (sink is null)
        {
            return;
        }
        try
        {
            sink.Pop(
                HarfBuzzPaintStackKind.Group,
                new ColorPaintPopGroup(ToCompositeMode(mode)));
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    private static HarfBuzzPaintSink? GetActiveSink(nint paintData)
    {
        HarfBuzzPaintSink? sink = GetSink(paintData);
        return sink?.Failure is null ? sink : null;
    }

    private static HarfBuzzPaintSink? GetSink(nint paintData)
    {
        try
        {
            return (HarfBuzzPaintSink?)GCHandle.FromIntPtr(paintData).Target;
        }
        catch
        {
            // Exceptions must never cross the native callback boundary.
            return null;
        }
    }

    private static ColorPaintGradient CopyColorLine(nint colorLine)
    {
        if (colorLine == 0)
        {
            throw new InvalidOperationException("HarfBuzz returned a null COLRv1 color line.");
        }

        uint count = 0;
        uint total = HarfBuzzNative.GetColorStops(colorLine, 0, &count, null);
        if (total > MaximumColorStopCount)
        {
            throw new InvalidOperationException("A COLRv1 color line has an invalid number of stops.");
        }

        var nativeStops = new HarfBuzzColorStop[checked((int)total)];
        if (total != 0)
        {
            count = total;
            fixed (HarfBuzzColorStop* stopsPointer = nativeStops)
            {
                uint reportedTotal = HarfBuzzNative.GetColorStops(colorLine, 0, &count, stopsPointer);
                if (reportedTotal != total || count != total)
                {
                    throw new InvalidOperationException("HarfBuzz changed a COLRv1 color line while it was being copied.");
                }
            }
        }

        var stops = new ColorPaintGradientStop[nativeStops.Length];
        for (int index = 0; index < stops.Length; index++)
        {
            HarfBuzzColorStop stop = nativeStops[index];
            if (!float.IsFinite(stop.Offset))
            {
                throw new InvalidOperationException("A COLRv1 gradient stop has an invalid offset.");
            }
            stops[index] = new(
                stop.Offset,
                ToColor(stop.Color, stop.IsForeground != 0));
        }

        StableSortStops(stops);
        return new(stops, ToExtendMode(HarfBuzzNative.GetColorLineExtend(colorLine)));
    }

    private static void StableSortStops(Span<ColorPaintGradientStop> stops)
    {
        for (int index = 1; index < stops.Length; index++)
        {
            ColorPaintGradientStop current = stops[index];
            int insertionIndex = index;
            while (insertionIndex > 0
                && stops[insertionIndex - 1].Offset > current.Offset)
            {
                stops[insertionIndex] = stops[insertionIndex - 1];
                insertionIndex--;
            }
            stops[insertionIndex] = current;
        }
    }

    private static ColorPaintColor ToColor(uint value, bool isForeground)
    {
        float alpha = (value & 0xff) / 255f;
        float red = ((value >> 8) & 0xff) / 255f;
        float green = ((value >> 16) & 0xff) / 255f;
        float blue = ((value >> 24) & 0xff) / 255f;
        return new(Color.FromSrgb(red, green, blue, alpha), isForeground);
    }

    private static ColorPaintExtendMode ToExtendMode(HarfBuzzPaintExtend value)
        => value switch
        {
            HarfBuzzPaintExtend.Pad => ColorPaintExtendMode.Pad,
            HarfBuzzPaintExtend.Repeat => ColorPaintExtendMode.Repeat,
            HarfBuzzPaintExtend.Reflect => ColorPaintExtendMode.Reflect,
            // HarfBuzz currently normalizes unknown OpenType values to Pad. Keep the
            // managed boundary forward-compatible if a future native version exposes one.
            _ => ColorPaintExtendMode.Pad,
        };

    private static ColorPaintCompositeMode ToCompositeMode(int value)
    {
        if (value < (int)HarfBuzzPaintCompositeMode.Clear
            || value > (int)HarfBuzzPaintCompositeMode.HslLuminosity)
        {
            return ColorPaintCompositeMode.Clear;
        }
        return (ColorPaintCompositeMode)value;
    }

    private static Matrix3x2 ToYDownTransform(
        float xx,
        float yx,
        float xy,
        float yy,
        float dx,
        float dy)
    {
        ValidateFinite(xx, yx, xy, yy, dx, dy);
        return new(xx, -yx, -xy, yy, dx, -dy);
    }

    private static Vector2 Point(float x, float y) => new(x, -y);

    private static void ValidateFinite(params float[] values)
    {
        foreach (float value in values)
        {
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException("HarfBuzz returned non-finite COLRv1 geometry.");
            }
        }
    }
}
