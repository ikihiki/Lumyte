using System.Diagnostics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Effects.Benchmarks;

internal sealed class EffectSlot : IDisposable
{
    internal EffectSlot(VulkanDevice device, TextureDesc description)
    {
        Texture = device.CreateTexture(description);
        try
        {
            Timer = new HardwareGpuTimer(device);
        }
        catch
        {
            Texture.Dispose();
            throw;
        }
    }

    internal IGraphicsTexture Texture { get; }

    internal HardwareGpuTimer Timer { get; }

    internal EffectFrame? Frame { get; set; }

    internal int FrameId { get; set; }

    internal int Index { get; set; }

    internal double SlotWaitMs { get; set; }

    internal double HostMs { get; set; }

    internal double LastWaitMs { get; private set; }

    public void Dispose()
    {
        if (Frame is not null)
        {
            throw new InvalidOperationException("Drain the slot before disposing its native resources.");
        }

        Timer.Dispose();
        Texture.Dispose();
    }

    internal async Task<FrameSample?> RetireAsync()
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
        RenderGraphStatistics statistics = frame.Statistics;
        long started = Stopwatch.GetTimestamp();
        frame.Dispose();
        double retirement = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Frame = null;
        return new FrameSample(FrameId, Index, SlotWaitMs, frame.BuildPrepareMs, frame.CompileMs, frame.RecordMs, frame.EncodeMs, frame.SubmitMs, frame.CompletionLatencyMs, retirement, HostMs, gpu, statistics);
    }
}
