using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>Copies an outline from a font borrowed by a HarfBuzz paint callback.</summary>
internal static unsafe class HarfBuzzPaintOutlineReader
{
    private static readonly HarfBuzzDrawFunctionsHandle DrawFunctions = CreateFunctions();

    internal static bool TryRead(nint font, uint glyphId, out PathGeometry? path)
    {
        if (font == 0)
        {
            path = null;
            return false;
        }

        var sink = new HarfBuzzOutlineSink();
        GCHandle sinkHandle = GCHandle.Alloc(sink);
        int result;
        try
        {
            result = HarfBuzzNative.DrawGlyph(
                font,
                glyphId,
                DrawFunctions.DangerousGetHandle(),
                GCHandle.ToIntPtr(sinkHandle));
        }
        finally
        {
            sinkHandle.Free();
        }

        if (sink.Failure is Exception failure)
        {
            ColorPaintFailure.RethrowIfFatal(failure);
            path = null;
            return false;
        }

        path = null;
        if (result == 0)
        {
            return false;
        }

        // A successful outline request may legitimately contain no drawable segments.
        // Keep that distinct from a failed HarfBuzz request so an empty clip stays
        // transparent while a malformed/unsupported outline rejects the paint glyph.
        sink.TryBuild(out path);
        return true;
    }

    private static HarfBuzzDrawFunctionsHandle CreateFunctions()
    {
        nint functions = HarfBuzzNative.CreateDrawFunctions();
        var handle = new HarfBuzzDrawFunctionsHandle(functions);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new InvalidOperationException("HarfBuzz could not allocate paint clip draw functions.");
        }

        try
        {
            HarfBuzzNative.SetMoveTo(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, float, float, nint, void>)&MoveTo,
                0,
                0);
            HarfBuzzNative.SetLineTo(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, float, float, nint, void>)&LineTo,
                0,
                0);
            HarfBuzzNative.SetQuadraticTo(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, float, float, float, float, nint, void>)&QuadraticTo,
                0,
                0);
            HarfBuzzNative.SetCubicTo(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, float, float, float, float, float, float, nint, void>)&CubicTo,
                0,
                0);
            HarfBuzzNative.SetClosePath(
                functions,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&ClosePath,
                0,
                0);
            HarfBuzzNative.MakeDrawFunctionsImmutable(functions);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MoveTo(nint functions, nint drawData, nint state, float x, float y, nint userData)
    {
        HarfBuzzOutlineSink? sink = GetSink(drawData);
        if (sink is null || sink.Failure is not null)
        {
            return;
        }
        try
        {
            sink.Builder.MoveTo(Point(x, y));
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void LineTo(nint functions, nint drawData, nint state, float x, float y, nint userData)
    {
        HarfBuzzOutlineSink? sink = GetSink(drawData);
        if (sink is null || sink.Failure is not null)
        {
            return;
        }
        try
        {
            sink.Builder.LineTo(Point(x, y));
            sink.AddDrawableSegment();
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void QuadraticTo(
        nint functions,
        nint drawData,
        nint state,
        float controlX,
        float controlY,
        float x,
        float y,
        nint userData)
    {
        HarfBuzzOutlineSink? sink = GetSink(drawData);
        if (sink is null || sink.Failure is not null)
        {
            return;
        }
        try
        {
            sink.Builder.QuadraticTo(Point(controlX, controlY), Point(x, y));
            sink.AddDrawableSegment();
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CubicTo(
        nint functions,
        nint drawData,
        nint state,
        float control0X,
        float control0Y,
        float control1X,
        float control1Y,
        float x,
        float y,
        nint userData)
    {
        HarfBuzzOutlineSink? sink = GetSink(drawData);
        if (sink is null || sink.Failure is not null)
        {
            return;
        }
        try
        {
            sink.Builder.CubicTo(
                Point(control0X, control0Y),
                Point(control1X, control1Y),
                Point(x, y));
            sink.AddDrawableSegment();
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ClosePath(nint functions, nint drawData, nint state, nint userData)
    {
        HarfBuzzOutlineSink? sink = GetSink(drawData);
        if (sink is null || sink.Failure is not null)
        {
            return;
        }

        try
        {
            sink.Builder.Close();
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    private static HarfBuzzOutlineSink? GetSink(nint drawData)
    {
        try
        {
            return (HarfBuzzOutlineSink?)GCHandle.FromIntPtr(drawData).Target;
        }
        catch
        {
            // Exceptions must never cross the native callback boundary.
            return null;
        }
    }

    private static Vector2 Point(float x, float y) => new(x, -y);
}
