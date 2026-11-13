using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Uses only baseline APIs to detect per-command table creation.</summary>
public sealed class SharedTableRegressionTests
{
    /// <summary>Distinct clip objects must not force independent logical tables.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task IndependentClipsReuseSameLogicalTableAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 200; i++)
        {
            canvas.Save();
            canvas.ClipRect(new(0, 0, 16, 16));
            canvas.FillCircle(new(8), 3, new SRgba<float>(1, 0, 0, 1));
            canvas.Restore();
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        Assert.Single(device.Tables);
        Assert.Equal(800, device.NativeBuffers);
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        Assert.Equal(200, commands.Events.OfType<DrawingArguments>().Count());
        Assert.Single(commands.Events.OfType<IArgumentTable>().Distinct());
    }
}
