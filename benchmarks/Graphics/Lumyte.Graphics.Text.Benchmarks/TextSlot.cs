using System.Diagnostics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Text.Benchmarks;

internal sealed class TextSlot : IDisposable
{
    internal TextSlot(VulkanDevice device, int index)
    {
        Index = index;
        Texture = device.CreateTexture(new TextureDesc
        {
            Width = TextPixels.Width,
            Height = TextPixels.Height,
            Format = TextureFormat.Rgba16Float,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
        });
        try
        {
            View = Texture.CreateView();
            Timer = new HardwareGpuTimer(device);
            Renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        }
        catch
        {
            Renderer?.Dispose();
            Timer?.Dispose();
            View?.Dispose();
            Texture.Dispose();
            throw;
        }
    }

    internal int Index { get; }

    internal IGraphicsTexture Texture { get; }

    internal IGraphicsTextureView View { get; }

    internal HardwareGpuTimer Timer { get; }

    internal Renderer2D Renderer { get; }

    internal TextFrame? Frame { get; set; }

    internal int FrameId { get; set; }

    internal int State { get; set; }

    internal double PriorSlotWaitMs { get; set; }

    internal double LastWaitMs { get; private set; }

    internal double HostMs { get; set; }

    public void Dispose()
    {
        TextChecks.Require(Frame is null, "Drain the slot before disposing text resources.");
        Renderer.Dispose();
        Timer.Dispose();
        View.Dispose();
        Texture.Dispose();
    }

    internal async Task ConfirmCompletionAsync()
    {
        if (Frame is { } frame)
        {
            await frame.CompleteAsync();
        }
    }

    internal void DiscardCompletedFrame()
    {
        Frame?.Dispose();
        Frame = null;
    }

    internal async Task<TextFrameSample?> RetireAsync()
    {
        LastWaitMs = 0;
        if (Frame is not { } frame)
        {
            return null;
        }

        if (!frame.IsSubmitted)
        {
            frame.Dispose();
            Frame = null;
            return null;
        }

        long wait = Stopwatch.GetTimestamp();
        await frame.CompleteAsync();
        LastWaitMs = Stopwatch.GetElapsedTime(wait).TotalMilliseconds;
        double? gpu = Timer.Supported ? Timer.ReadMilliseconds() : null;
        long start = Stopwatch.GetTimestamp();
        frame.Dispose();
        double retirement = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Frame = null;
        return new TextFrameSample(FrameId, State, Index, PriorSlotWaitMs, frame.PrepareHostMs, frame.RecordHostMs, frame.EncodeHostMs, frame.SubmitHostMs, frame.CompletionObservationMs, retirement, HostMs, gpu);
    }
}
