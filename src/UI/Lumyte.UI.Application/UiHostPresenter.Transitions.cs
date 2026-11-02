using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Application;

internal sealed partial class UiHostPresenter
{
    internal async ValueTask DrainVisualsAsync()
    {
        foreach (Frame frame in _frames.ToArray())
        {
            await frame.Finished.Task;
            await frame.DrainAsync();
            frame.Dispose();
            _frames.Remove(frame);
        }
    }

    internal async ValueTask<UiVisualSnapshot?> CaptureVisualAsync(DrawList2D list, uint width, uint height, IDisposable reservation, IReadOnlyList<UiVisualSnapshot>? visuals = null, Action? submitted = null)
    {
        IGraphicsTexture texture;
        IGraphicsTextureView view;
        try
        {
            texture = _device.CreateTexture(new() { Width = width, Height = height, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled });
            try
            {
                view = texture.CreateView();
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }
        catch (Exception error) when (error is NotSupportedException or OutOfMemoryException or ArgumentException)
        {
            reservation.Dispose();
            return null;
        }
        catch
        {
            reservation.Dispose();
            throw;
        }

        var snapshot = new UiVisualSnapshot(texture, view, reservation);
        try
        {
            await RenderVisualAsync(snapshot, list, true, visuals, submitted);
            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    internal ValueTask UpdateVisualAsync(UiVisualSnapshot image, DrawList2D list, IReadOnlyList<UiVisualSnapshot> visuals, Action? submitted = null) => RenderVisualAsync(image, list, false, visuals, submitted);

    private async ValueTask RenderVisualAsync(UiVisualSnapshot snapshot, DrawList2D list, bool initial, IReadOnlyList<UiVisualSnapshot>? visuals, Action? submitted)
    {
        IGraphicsTexture texture = snapshot.View.Texture;
        IGraphicsTextureView view = snapshot.View;
        var frame = new Frame(Guid.Empty);
        frame.Own(snapshot.Retain());
        if (visuals is not null)
        {
            foreach (UiVisualSnapshot image in visuals)
            {
                frame.Own(image.Retain());
            }
        }

        _frames.Add(frame);
        try
        {
            PreparedDraw2D prepared = frame.Own(await _renderer.PrepareAsync(list, new() { View = view, PixelScale = 1, LoadOp = AttachmentLoadOp.Clear, ClearColor = default }));
            IGraphicsCommandBuffer commands = frame.Own(_device.CreateCommandBuffer(new()));
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
            var sampled = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = initial ? TextureState.Undefined : TextureState.Sampled, Before = initial ? default : sampled, AfterState = TextureState.ColorAttachment, After = color });
            prepared.Record(commands);
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.Sampled, Before = color, After = sampled });
            commands.Finish();
            frame.Submission = _device.Queue.Submit(new QueueSubmitDesc { CommandBuffers = [commands] });
            submitted?.Invoke();
            await frame.DrainAsync();
            if (frame.IsRetired)
            {
                throw new InvalidOperationException("The submitted device generation was retired.");
            }

            frame.Dispose();
            _frames.Remove(frame);
        }
        catch
        {
            if (frame.Submission is null)
            {
                frame.Dispose();
                _frames.Remove(frame);
            }

            throw;
        }
        finally
        {
            frame.Finished.TrySetResult();
        }
    }
}
