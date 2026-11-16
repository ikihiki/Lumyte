using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

// Shared lifecycle, not a renderer strategy. Writable scratch is always frame-private.
internal static class DrawingFramePreparation2D
{
    internal static DrawingTargetInfo2D Describe(RenderTarget2D target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(target.View);
        IGraphicsTexture texture = target.View.Texture;
        TextureViewInfo view = target.View.Info;
        if (view.Dimension != TextureViewDimension.D2 || view.BaseMipLevel != 0 || view.MipLevelCount != 1 || view.BaseArrayLayer != 0 || view.ArrayLayerCount != 1)
        {
            throw new ArgumentException("The target must be a whole single-mip, single-layer D2 view.", nameof(target));
        }

        var info = new DrawingTargetInfo2D
        {
            Description = new() { Width = texture.Width, Height = texture.Height, MipLevels = texture.MipLevels, ArrayLayers = texture.ArrayLayers, Format = texture.Format, Usage = texture.Usage },
            PixelScale = target.PixelScale,
            LoadOp = target.LoadOp,
            ClearColor = target.ClearColor,
        };
        Validate(info);
        return info;
    }

    internal static void Validate(DrawingTargetInfo2D target)
    {
        ArgumentNullException.ThrowIfNull(target);
        TextureDesc desc = target.Description;
        if (desc.Width == 0 || desc.Height == 0 || desc.Width > int.MaxValue || desc.Height > int.MaxValue || desc.MipLevels != 1 || desc.ArrayLayers != 1
            || (desc.Usage & TextureUsage.RenderAttachment) == 0 || desc.Format is not (TextureFormat.Rgba8Srgb or TextureFormat.Rgba16Float)
            || !float.IsFinite(target.PixelScale) || target.PixelScale <= 0 || !Enum.IsDefined(target.LoadOp)
            || !Integer(target.PixelOrigin.X) || !Integer(target.PixelOrigin.Y))
        {
            throw new ArgumentException("The target must have valid extent, format, scale, origin and attachment usage.", nameof(target));
        }

        _ = checked((int)target.PixelOrigin.X + (int)desc.Width);
        _ = checked((int)target.PixelOrigin.Y + (int)desc.Height);
    }

    internal static async ValueTask<PreparedDrawingFrame2D> PrepareAsync(
        IGraphicDevice device,
        IDrawingSceneSource2D source,
        RenderTarget2D target,
        RetainedDrawingCache2D cache,
        Func<DrawList2D, DrawingTargetInfo2D, CancellationToken, ValueTask<IReadOnlyList<IPreparedCommandBatch2D>>> prepare,
        CancellationToken cancellationToken,
        long maxWorkingBytes)
    {
        DrawingTargetInfo2D info = Describe(target);
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        try
        {
            DrawList2D captured = DrawingSceneCapture2D.Capture(source, cancellationToken, maxWorkingBytes);
            IReadOnlyList<IPreparedCommandBatch2D> batches = await prepare(captured, info, cancellationToken).ConfigureAwait(false);
            var key = new RetainedDrawingCache2D.Key([captured], Matrix3x2.Identity, [], info.Description.Width, info.Description.Height, info.Description.Format, info.PixelScale);
            frame.Assets.Add(new(key, new RetainedBatchRun2D(batches, false)));
            cache.CountCompilation();
            _ = source.Revision;
            cache.AdmitFrame(frame);
            return new(device, target, frame.Assets, completed => cache.ReleaseFrame(frame, completed), cache: cache);
        }
        catch
        {
            cache.ReleaseFrame(frame, false);
            throw;
        }
    }

    private static bool Integer(float value) => float.IsFinite(value) && value == MathF.Truncate(value) && value >= int.MinValue && (double)value <= int.MaxValue;
}
