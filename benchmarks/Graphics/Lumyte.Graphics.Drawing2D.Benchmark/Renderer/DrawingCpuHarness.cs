using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Renderer;

/// <summary>Owns a managed target and warm renderer; never submits or executes shaders.</summary>
public sealed class DrawingCpuHarness : IDisposable
{
    private readonly IGraphicsTexture _texture;
    private readonly IGraphicsTextureView _view;
    private readonly Renderer2D _renderer;
    private readonly RenderTarget2D _target;

    /// <summary>Initializes a new instance of the <see cref="DrawingCpuHarness"/> class, without preparing a drawing.</summary>
    public DrawingCpuHarness()
    {
        Device = new CpuGraphicsDevice();
        _texture = Device.CreateTexture(new() { Width = 512, Height = 512, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment });
        _view = _texture.CreateView();
        _target = new() { View = _view };
        _renderer = new(Device, DrawingCommandRenderers2D.CreateDefault());
    }

    /// <summary>Gets the recording-only device and immutable observations.</summary>
    public CpuGraphicsDevice Device { get; }

    /// <summary>Prepares and records with an existing renderer, then disposes commands and prepared resources.</summary>
    /// <param name="drawing">The prebuilt canvas snapshot, excluded from this phase.</param>
    /// <returns>The consumed synthetic-driver checksum.</returns>
    public ulong WarmPrepareRecordDispose(DrawList2D drawing)
    {
        Device.ResetCounters();
        Execute(_renderer, drawing);
        return Device.Counters.Checksum;
    }

    /// <summary>Creates a renderer, first-prepares, records and disposes all renderer-owned resources.</summary>
    /// <param name="drawing">The prebuilt canvas snapshot.</param>
    /// <returns>The consumed synthetic-driver checksum.</returns>
    public ulong ColdPrepareRecordDispose(DrawList2D drawing)
    {
        Device.ResetCounters();
        using var renderer = new Renderer2D(Device, DrawingCommandRenderers2D.CreateDefault());
        Execute(renderer, drawing);
        return Device.Counters.Checksum;
    }

    /// <summary>Releases warm renderer, target view, target and CPU-only device, in dependency order.</summary>
    public void Dispose()
    {
        _renderer.Dispose();
        _view.Dispose();
        _texture.Dispose();
        Device.Dispose();
    }

    private void Execute(Renderer2D renderer, DrawList2D drawing)
    {
        ValueTask<PreparedDraw2D> preparation = renderer.PrepareAsync(drawing, _target);
        if (!preparation.IsCompleted)
        {
            throw new InvalidOperationException("The CPU-only device must complete preparation synchronously.");
        }

        using PreparedDraw2D prepared = preparation.GetAwaiter().GetResult();
        using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
        prepared.Record(commands);
        commands.Finish();
    }
}
