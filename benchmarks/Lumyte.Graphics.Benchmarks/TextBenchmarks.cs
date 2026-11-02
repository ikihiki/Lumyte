using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.Graphics.Text.Parley;

namespace Lumyte.Graphics.Benchmarks;

/// <summary>Separates static layout recording from changing text shaping and outlines.</summary>
public class TextBenchmarks
{
    private readonly Canvas2D _canvas = new();
    private ParleyTextEngine2D _engine = null!;
    private ParleyTextOptions2D _options = null!;
    private TextLayout2D _layout = null!;
    private int _frame;

    /// <summary>Loads a caller-selected font outside timed work.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _engine = new();
        using FileStream stream = File.OpenRead(Environment.GetEnvironmentVariable("LUMYTE_BENCH_FONT") ?? "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
        string family = _engine.AddFont(stream);
        _options = new() { FontFamily = family, FontSize = 24, WrappingLength = 600 };
        _layout = _engine.CreateLayout("Hello Lumyte! Static vector text 0123456789", _options);
    }

    /// <summary>Returns the native text engine.</summary>
    [GlobalCleanup]
    public void Cleanup() => _engine.Dispose();

    /// <summary>Records a cached immutable layout; excludes shaping.</summary>
    /// <returns>The drawing snapshot.</returns>
    [Benchmark]
    public DrawList2D StaticText()
    {
        _canvas.Begin();
        _canvas.DrawText(_layout);
        return _canvas.End();
    }

    /// <summary>Shapes changing text and extracts vector glyphs; excludes GPU preparation.</summary>
    /// <returns>The drawing snapshot.</returns>
    [Benchmark]
    public DrawList2D DynamicText()
    {
        TextLayout2D layout = _engine.CreateLayout($"Hello Lumyte! Dynamic frame {_frame++:D8}", _options);
        _canvas.Begin();
        _canvas.DrawText(layout);
        return _canvas.End();
    }
}
