using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Prepares pure drawing batches for direct command-buffer recording without RenderGraph.</summary>
/// <param name="device">The borrowed rendering device.</param>
/// <param name="renderers">The borrowed fixed command renderer table.</param>
public sealed class Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers) : IDisposable
{
    private readonly IGraphicDevice _device = device ?? throw new ArgumentNullException(nameof(device));
    private readonly DrawingCommandRenderers2D _renderers = renderers ?? throw new ArgumentNullException(nameof(renderers));
    private bool _disposed;
    private int _preparing;

    /// <summary>Prepares one-shot batches; creates no RenderGraph and never submits work.</summary>
    /// <param name="list">The pure drawing snapshot.</param>
    /// <param name="target">The borrowed target, in ColorAttachment state at Record.</param>
    /// <param name="cancellationToken">The preparation cancellation token.</param>
    /// <returns>The fixed-resource owner and direct command recording.</returns>
    public async ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(target.View);
        ValidateView(target.View);
        var info = new DrawingTargetInfo2D { Description = Describe(target.View.Texture), PixelScale = target.PixelScale, LoadOp = target.LoadOp, ClearColor = target.ClearColor };
        ValidateTarget(info);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _preparing, 1, 0) != 0)
        {
            throw new InvalidOperationException("Concurrent preparation using one Renderer2D is not supported.");
        }

        var batches = new List<IPreparedCommandBatch2D>();
        try
        {
            var context = new RenderPreparationContext2D(_device, info with { LoadOp = AttachmentLoadOp.Load });
            int index = 0;
            while (index < list.Commands.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IPreparedCommandBatch2D batch = list.Commands[index] switch
                {
                    FillRectCommand2D => await _renderers.FillRect.PrepareAsync(context, TakeBatch<FillRectCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
                    FillRoundedRectCommand2D => await _renderers.FillRoundedRect.PrepareAsync(context, TakeBatch<FillRoundedRectCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
                    FillCircleCommand2D => await _renderers.FillCircle.PrepareAsync(context, TakeBatch<FillCircleCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
                    DrawLineCommand2D => await _renderers.DrawLine.PrepareAsync(context, TakeBatch<DrawLineCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
                    FillPathCommand2D => await _renderers.FillPath.PrepareAsync(context, TakeBatch<FillPathCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
                    StrokePathCommand2D => await _renderers.StrokePath.PrepareAsync(context, TakeBatch<StrokePathCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
                    DrawImageCommand2D => await _renderers.DrawImage.PrepareAsync(context, TakeBatch<DrawImageCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
                    DrawCoverageMaskCommand2D => await _renderers.DrawCoverageMask.PrepareAsync(context, TakeBatch<DrawCoverageMaskCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
                    DrawDistanceFieldCommand2D => await _renderers.DrawDistanceField.PrepareAsync(context, TakeBatch<DrawDistanceFieldCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
                    DrawMeshCommand2D => await _renderers.DrawMesh.PrepareAsync(context, TakeBatch<DrawMeshCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
                    DrawPaintGraphCommand2D => await _renderers.DrawPaintGraph.PrepareAsync(context, TakeBatch<DrawPaintGraphCommand2D>(list, ref index, list.Commands.Count), cancellationToken).ConfigureAwait(false),
                    _ => throw new NotSupportedException("Layer and effect commands require RenderGraphCanvas2D."),
                };
                ArgumentNullException.ThrowIfNull(batch);
                batches.Add(batch);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new PreparedDraw2D(target, batches);
        }
        catch
        {
            for (int i = batches.Count - 1; i >= 0; i--)
            {
                batches[i].Dispose();
            }

            throw;
        }
        finally
        {
            Volatile.Write(ref _preparing, 0);
        }
    }

    /// <summary>Disposes this host without disposing borrowed strategies or waiting.</summary>
    public void Dispose() => _disposed = true;

    private static TextureDesc Describe(IGraphicsTexture texture) => new()
    {
        Width = texture.Width,
        Height = texture.Height,
        MipLevels = texture.MipLevels,
        ArrayLayers = texture.ArrayLayers,
        Format = texture.Format,
        Usage = texture.Usage,
    };

    private static void ValidateView(IGraphicsTextureView view)
    {
        TextureViewInfo info = view.Info;
        if (info.Dimension != TextureViewDimension.D2 || info.BaseMipLevel != 0 || info.MipLevelCount != 1 || info.BaseArrayLayer != 0 || info.ArrayLayerCount != 1)
        {
            throw new ArgumentException("The drawing target must be a whole single-mip, single-layer D2 view.", nameof(view));
        }
    }

    private static void ValidateTarget(DrawingTargetInfo2D target)
    {
        ArgumentNullException.ThrowIfNull(target.Description);
        TextureDesc desc = target.Description;
        if (desc.Width == 0 || desc.Height == 0 || desc.Width > int.MaxValue || desc.Height > int.MaxValue
            || desc.MipLevels != 1 || desc.ArrayLayers != 1
            || (desc.Usage & TextureUsage.RenderAttachment) == 0 || desc.Format is not (TextureFormat.Rgba8Srgb or TextureFormat.Rgba16Float))
        {
            throw new ArgumentException("The target must be a positive, single-mip, single-layer Rgba8Srgb or Rgba16Float attachment.", nameof(target));
        }

        if (!float.IsFinite(target.PixelScale) || target.PixelScale <= 0 || !IsInteger(target.PixelOrigin.X) || !IsInteger(target.PixelOrigin.Y)
            || !Enum.IsDefined(target.LoadOp) || !ValidColor(target.ClearColor))
        {
            throw new ArgumentException("The target scale, origin, load operation, and clear color must be valid.", nameof(target));
        }

        _ = checked((int)target.PixelOrigin.X + (int)desc.Width);
        _ = checked((int)target.PixelOrigin.Y + (int)desc.Height);
    }

    private static bool IsInteger(float value) => float.IsFinite(value) && value == MathF.Truncate(value) && value >= int.MinValue && (double)value <= int.MaxValue;

    private static bool ValidColor(Color2D color) => ValidChannel(color.R) && ValidChannel(color.G) && ValidChannel(color.B) && ValidChannel(color.A);

    private static bool ValidChannel(float value) => float.IsFinite(value) && value >= 0 && value <= 1;

    private static ReadOnlyMemory<T> TakeBatch<T>(DrawList2D list, ref int index, int end)
        where T : DrawingCommand2D
    {
        IReadOnlyList<DrawingCommand2D> commands = list.Commands;
        ClipStack2D clip = commands[index].State.Clip;
        int begin = index;
        while (index < end && commands[index] is T command && ReferenceEquals(command.State.Clip, clip))
        {
            index++;
        }

        return list.GetCommands<T>(begin, index - begin);
    }
}
