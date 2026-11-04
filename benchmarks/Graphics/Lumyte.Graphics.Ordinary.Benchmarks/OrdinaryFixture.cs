using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Gpu.Benchmarks;

internal sealed class OrdinaryFixture : IDisposable
{
    private readonly string _scenario;
    private readonly string _variant;
    private readonly DrawingScene2D[] _scenes;
    private readonly DrawingNodeId2D _node;
    private int _frame;

    internal OrdinaryFixture(string scenario, string variant)
    {
        Validate(scenario, variant);
        _scenario = scenario;
        _variant = variant;
        _scenes = Enumerable.Range(0, scenario == "D12" ? 9 : 1).Select(_ => new DrawingScene2D()).ToArray();
        if (scenario == "D12")
        {
            for (int page = 0; page < 9; page++)
            {
                for (int item = 0; item < 16; item++)
                {
                    var canvas = new Canvas2D();
                    canvas.Begin();
                    canvas.FillRect(new(32 + (item * 80), 32, 48, 48), PageColor(page));
                    _scenes[page].Insert(_scenes[page].Root, item, canvas.End());
                }
            }
        }
        else
        {
            _node = Scene.Insert(Scene.Root, 0, Record(scenario, variant, 0, scenario != "D04"));
            if (scenario == "D04")
            {
                Scene.SetTransform(_node, Camera(variant, 0));
            }
        }
    }

    internal DrawingScene2D Scene => _scenes[_scenario == "D12" ? _frame % 9 : 0];

    internal int PropertyEdits { get; private set; }

    internal int StructuralEdits => 0;

    internal int ElementCount => _scenario switch { "D04" => 3000, "D05" => 32, "D09" => 256, "D12" => 16, _ => 1 };

    internal int VisibleElements
    {
        get
        {
            if (_scenario != "D04")
            {
                return ElementCount;
            }

            Matrix3x2 camera = Camera(_variant, _frame);
            int visible = 0;
            for (int y = -10; y < 40; y++)
            {
                for (int x = -10; x < 50; x++)
                {
                    var first = Vector2.Transform(new Vector2(x * 64, y * 64), camera);
                    var last = Vector2.Transform(new Vector2((x * 64) + 56, (y * 64) + 56), camera);
                    if (last.X > 0 && last.Y > 0 && first.X < 1920 && first.Y < 1080)
                    {
                        visible++;
                    }
                }
            }

            return visible;
        }
    }

    internal int RerecordedCommands { get; private set; }

    internal int WorkingSetPage => _scenario == "D12" ? _frame % 9 : 0;

    public void Dispose()
    {
        foreach (DrawingScene2D scene in _scenes)
        {
            scene.Dispose();
        }
    }

    internal static void Validate(string scenario, string variant)
    {
        bool valid = scenario switch
        {
            "D04" => variant is "pan" or "zoom",
            "D05" => variant is "16" or "64",
            "D08" => variant is "7" or "8" or "9" or "path" or "gradient",
            "D09" => variant is "1" or "4" or "16",
            "D12" => variant is "small-cache" or "large-cache",
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException("Unsupported ordinary scenario/variant.");
        }
    }

    internal static Path2D GeometryPath(int segments, int frame, float x, float y)
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(x, y));

        // Fixed64x64 bounds; actual segment count includes Move and Close.
        for (int segment = 1; segment <= segments - 4; segment++)
        {
            path.LineTo(new(x + ((segment * 64f) / (segments - 4)), y + (segment == (segments - 4) / 2 ? (frame & 1) * 8 : 0)));
        }

        path.LineTo(new(x + 64, y + 64));
        path.LineTo(new(x, y + 64));
        path.Close();
        return path.Build();
    }

    internal static string InputId(string scenario, string variant, int frame) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"ordinary-v1:seed27:{scenario}:{variant}:{(scenario == "D12" ? frame % 9 : scenario is "D04" or "D05" ? frame % 2 : 0)}")));

    internal static DrawList2D Reference(string scenario, string variant, int frame) => Record(scenario, variant, frame, true);

    internal static void VerifyAnalytic(byte[] pixels, string scenario, string variant, int frame)
    {
        if (pixels.Length != 1920 * 1080 * 4)
        {
            throw new InvalidOperationException("Invalid RGBA8 readback.");
        }

        if (scenario == "D09")
        {
            int layers = int.Parse(variant, System.Globalization.CultureInfo.InvariantCulture);
            double linear = 1 - Math.Pow(0.5, layers);
            int alpha = (int)Math.Round(linear * 255);
            int red = (int)Math.Round(((1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055) * 255);
            Check(40, 40, red, 0, 0, alpha, 2);
        }
        else if (scenario == "D08")
        {
            Check(160, 160, 255, 0, 0, 255, 0);
            Check(32, 32, 0, 0, 0, 0, 0);
        }
        else if (scenario == "D05")
        {
            for (int i = 0; i < 32; i++)
            {
                Check(48 + ((i % 8) * 180), 48 + ((i / 8) * 180), 255, 0, 0, 255, 0);
            }
        }
        else if (scenario == "D12")
        {
            int page = frame % 9;
            Check(40, 40, 255, page % 2 == 0 ? 0 : 255, 0, 255, 0);
        }
        else
        {
            // Map tile interiors around origin remain visible for both controlled camera phases.
            Check(12, 12, 255, 0, 0, 255, 0);
        }

        void Check(int x, int y, int r, int g, int b, int a, int tolerance)
        {
            int offset = ((y * 1920) + x) * 4;
            int[] expected = [r, g, b, a];
            for (int channel = 0; channel < 4; channel++)
            {
                if (Math.Abs(pixels[offset + channel] - expected[channel]) > tolerance)
                {
                    throw new InvalidOperationException($"Analytic mismatch {scenario}/{variant} frame{frame} pixel{x},{y} channel{channel}.");
                }
            }
        }
    }

    internal void Apply(int frame)
    {
        PropertyEdits = 0;
        RerecordedCommands = 0;
        _frame = frame;
        if (_scenario == "D04")
        {
            Scene.SetTransform(_node, Camera(_variant, frame));
            PropertyEdits = 1;
        }
        else if (_scenario == "D05")
        {
            DrawList2D content = Record(_scenario, _variant, frame, false);
            Scene.ReplaceContent(_node, content);
            RerecordedCommands = content.Count;
        }
    }

    private static Matrix3x2 Camera(string variant, int frame) => variant == "pan" ? Matrix3x2.CreateTranslation((frame & 1) * 4, 0) : Matrix3x2.CreateScale((frame & 1) == 0 ? 1 : 1.1f);

    private static Color2D PageColor(int page) => page % 2 == 0 ? new(1, 0, 0, 1) : new(1, 1, 0, 1);

    private static DrawList2D Record(string scenario, string variant, int frame, bool reference)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        switch (scenario)
        {
            case "D04":
                if (reference)
                {
                    canvas.ConcatTransform(Camera(variant, frame));
                }

                for (int y = -10; y < 40; y++)
                {
                    for (int x = -10; x < 50; x++)
                    {
                        canvas.FillRect(new(x * 64, y * 64, 56, 56), new Color2D(1, 0, 0, 1));
                    }
                }

                break;
            case "D05":
                int segments = int.Parse(variant, System.Globalization.CultureInfo.InvariantCulture);
                for (int i = 0; i < 32; i++)
                {
                    float x = 16 + ((i % 8) * 180), y = 16 + ((i / 8) * 180);
                    canvas.FillPath(GeometryPath(segments, frame, x, y), new Color2D(1, 0, 0, 1));
                }

                break;
            case "D08":
                if (variant == "path")
                {
                    var path = new PathBuilder2D();
                    path.MoveTo(new(128, 128));
                    path.LineTo(new(512, 128));
                    path.LineTo(new(128, 512));
                    path.Close();
                    canvas.ClipPath(path.Build());
                }
                else
                {
                    int depth = variant == "gradient" ? 1 : int.Parse(variant, System.Globalization.CultureInfo.InvariantCulture);
                    for (int i = 0; i < depth; i++)
                    {
                        canvas.ClipRect(new(128, 128, 384, 384));
                    }
                }

                PaintGraph2D paint = variant == "gradient" ? PaintGraph2D.LinearGradient(new(128, 128), new(512, 128), [new(0, new(1, 0, 0, 1)), new(0.5f, new(1, 0, 0, 1)), new(1, new(0, 0, 1, 1))]) : PaintGraph2D.Solid(new(1, 0, 0, 1));
                canvas.FillRect(new(0, 0, 640, 640), paint);
                break;
            case "D09":
                int layers = int.Parse(variant, System.Globalization.CultureInfo.InvariantCulture);
                for (int i = 0; i < 256; i++)
                {
                    int cell = i / layers;
                    canvas.FillRect(new(32 + ((cell % 16) * 64), 32 + ((cell / 16) * 64), 48, 48), new Color2D(1, 0, 0, 0.5f));
                }

                break;
            case "D12":
                for (int i = 0; i < 16; i++)
                {
                    canvas.FillRect(new(32 + (i * 80), 32, 48, 48), PageColor(frame % 9));
                }

                break;
        }

        return canvas.End();
    }
}
