using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarfBuzzSharp;
using Lumyte.Graphics.TwoD;
using HbFont = HarfBuzzSharp.Font;

namespace Lumyte.Graphics.Text;

/// <summary>Converts HarfBuzz OpenType outlines into backend-independent 2D paths.</summary>
internal static unsafe class HarfBuzzOutlineReader
{
    private static readonly HarfBuzzDrawFunctionsHandle DrawFunctions = CreateFunctions();

    internal static bool TryRead(HbFont font, uint glyphId, out PathGeometry? path)
    {
        ArgumentNullException.ThrowIfNull(font);
        var sink = new HarfBuzzOutlineSink();
        GCHandle sinkHandle = GCHandle.Alloc(sink);
        int result;
        try
        {
            result = HarfBuzzNative.DrawGlyph(
                font.Handle,
                glyphId,
                DrawFunctions.DangerousGetHandle(),
                GCHandle.ToIntPtr(sinkHandle));
            GC.KeepAlive(font);
        }
        finally
        {
            sinkHandle.Free();
        }

        if (sink.Failure is Exception failure)
        {
            throw new InvalidOperationException("HarfBuzz returned an invalid glyph outline.", failure);
        }

        path = null;
        return result != 0 && sink.TryBuild(out path);
    }

    private static HarfBuzzDrawFunctionsHandle CreateFunctions()
    {
        nint functions = HarfBuzzNative.CreateDrawFunctions();
        var handle = new HarfBuzzDrawFunctionsHandle(functions);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new InvalidOperationException("HarfBuzz could not allocate glyph draw functions.");
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
    private static void MoveTo(
        nint functions,
        nint drawData,
        nint state,
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
            sink.Builder.MoveTo(Point(x, y));
        }
        catch (Exception exception)
        {
            sink.RecordFailure(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void LineTo(
        nint functions,
        nint drawData,
        nint state,
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
