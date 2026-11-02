using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;

namespace Lumyte.Graphics.Benchmarks;

/// <summary>Measures layer tape recording only; effects execute later during graph expansion.</summary>
public class LayerRecordingBenchmarks
{
    private readonly RenderGraphCanvas2D _canvas = new();
    private readonly LayerOptions2D _options = new()
    {
        ContentBounds = new(0, 0, 256, 256),
        Opacity = 0.8f,
        Effects = graph => graph.AppendGaussianBlur(graph.Source, 3, 3),
    };

    /// <summary>Records ten effect layers, each with ten rectangles.</summary>
    /// <returns>The immutable layer tape.</returns>
    [Benchmark]
    public RenderGraphDrawList2D BlurLayers()
    {
        _canvas.Begin();
        for (int layer = 0; layer < 10; layer++)
        {
            _canvas.BeginLayer(_options);
            for (int i = 0; i < 10; i++)
            {
                _canvas.FillRect(new(i * 20, layer * 20, 16, 16), new Color2D(1, 0, 0, 0.5f));
            }

            _canvas.EndLayer();
        }

        return _canvas.End();
    }
}
