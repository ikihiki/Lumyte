using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.BenchmarkWorkloads;

/// <summary>Bounded D03 placement and D11 roundtrip topology traces, with independent source-order reference.</summary>
public sealed class SceneMutationFixture : IDisposable
{
    private readonly string _scenario;
    private readonly string _placement;
    private readonly DrawingNodeId2D[] _nodes;
    private readonly DrawingNodeId2D _groupA;
    private readonly DrawingNodeId2D _groupB;
    private DrawingNodeId2D _inserted;
    private int _frame;

    /// <summary>Initializes a new instance of the <see cref="SceneMutationFixture"/> class.</summary>
    /// <param name="scenario">D03 or D11.</param>
    /// <param name="placement">Parent or individual placement updates.</param>
    /// <param name="shared">Whether placement nodes share one immutable recording.</param>
    public SceneMutationFixture(string scenario, string placement = "parent", bool shared = true)
    {
        if (scenario is not ("D03" or "D11") || placement is not ("parent" or "individual"))
        {
            throw new ArgumentException("Unknown scene-mutation variant.");
        }

        _scenario = scenario;
        _placement = placement;
        _groupA = Scene.AddGroup(Scene.Root, 0);
        _groupB = Scene.AddGroup(Scene.Root, 1);
        _nodes = new DrawingNodeId2D[scenario == "D03" ? 256 : 3];
        DrawList2D common = Content(0);
        for (int i = 0; i < _nodes.Length; i++)
        {
            _nodes[i] = Scene.Insert(_groupA, i, scenario == "D03" && shared ? common : Content(scenario == "D03" ? 0 : i));
            if (scenario == "D03")
            {
                Scene.SetTransform(_nodes[i], Placement(i, 0));
            }
        }
    }

    /// <summary>Gets the owned mutable public scene.</summary>
    public DrawingScene2D Scene { get; } = new();

    /// <summary>Gets the last operation's successful public property edit count.</summary>
    public int PropertyEdits { get; private set; }

    /// <summary>Gets the last operation's successful public structural edit count.</summary>
    public int StructuralEdits { get; private set; }

    /// <summary>Gets the bounded current logical element count.</summary>
    public int ElementCount => _scenario == "D03" ? 256 : (_frame % 4 == 1 ? 4 : 3);

    /// <summary>Gets the deterministic partition-independent source input ID.</summary>
    /// <param name="scenario">The scenario ID.</param>
    /// <param name="frame">The logical frame.</param>
    /// <returns>The source trace state hash.</returns>
    public static string InputId(string scenario, int frame) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"scene-mutation-v1:{scenario}:{frame % (scenario == "D03" ? 2 : 4)}")));

    /// <summary>Records an independent reference from known logical positions and painter order.</summary>
    /// <param name="scenario">D03 or D11.</param>
    /// <param name="frame">The source logical frame.</param>
    /// <returns>The independently constructed immutable Canvas stream.</returns>
    public static DrawList2D Reference(string scenario, int frame)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        if (scenario == "D03")
        {
            for (int i = 0; i < 256; i++)
            {
                canvas.FillRect(new(8 + ((i % 16) * 100) + ((frame & 1) * 4), 8 + ((i / 16) * 60), 24, 24), Color(0));
            }
        }
        else
        {
            int[] order = (frame % 4) switch { 1 => [0, 1, 2, 3], 2 => [1, 2, 0], 3 => [1, 0, 2], _ => [0, 1, 2] };
            foreach (int index in order)
            {
                canvas.FillRect(new(8, 8, 24, 24), Color(index));
            }
        }

        return canvas.End();
    }

    /// <summary>Checks known opaque center colors including order changes, independently of rendered reference.</summary>
    /// <param name="pixels">The tightly packed RGBA8 target.</param>
    /// <param name="scenario">The trace ID.</param>
    /// <param name="frame">The logical frame.</param>
    /// <param name="width">The physical width.</param>
    public static void VerifyAnalytic(byte[] pixels, string scenario, int frame, int width = 1920)
    {
        int color = scenario == "D03" ? 0 : (frame % 4) switch { 1 => 3, 2 => 0, _ => 2 };
        int count = scenario == "D03" ? 256 : 1;
        for (int i = 0; i < count; i++)
        {
            int x = scenario == "D03" ? 16 + ((i % 16) * 100) + ((frame & 1) * 4) : 16;
            int y = scenario == "D03" ? 16 + ((i / 16) * 60) : 16;
            int offset = ((y * width) + x) * 4;
            byte[] expected = color switch { 0 => [255, 0, 0, 255], 1 => [0, 255, 0, 255], 2 => [0, 0, 255, 255], _ => [255, 255, 0, 255] };
            if (!pixels.AsSpan(offset, 4).SequenceEqual(expected))
            {
                throw new InvalidOperationException("Analytic placement/order color mismatch.");
            }
        }
    }

    /// <summary>Applies one next logical frame with actual public Scene mutations.</summary>
    /// <param name="frame">The next sequential logical frame, starting at one.</param>
    public void Apply(int frame)
    {
        if (frame != _frame + 1)
        {
            throw new ArgumentException("Scene mutation traces require sequential logical frames.", nameof(frame));
        }

        PropertyEdits = 0;
        StructuralEdits = 0;
        _frame = frame;
        if (_scenario == "D03")
        {
            if (_placement == "parent")
            {
                Scene.SetTransform(_groupA, Matrix3x2.CreateTranslation((frame & 1) * 4, 0));
                PropertyEdits = 1;
            }
            else
            {
                for (int i = 0; i < _nodes.Length; i++)
                {
                    Scene.SetTransform(_nodes[i], Placement(i, frame));
                    PropertyEdits++;
                }
            }

            return;
        }

        switch (frame % 4)
        {
            case 1:
                _inserted = Scene.Insert(_groupA, 3, Content(3));
                StructuralEdits = 1;
                break;
            case 2:
                Scene.Remove(_inserted);
                Scene.Move(_nodes[0], _groupA, 2);
                StructuralEdits = 2;
                break;
            case 3:
                Scene.Move(_nodes[2], _groupB, 0);
                StructuralEdits = 1;
                break;
            case 0:
                Scene.Move(_nodes[2], _groupA, 2);
                Scene.Move(_nodes[0], _groupA, 0);
                StructuralEdits = 2;
                break;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Scene.Dispose();

    private static Matrix3x2 Placement(int index, int frame) => Matrix3x2.CreateTranslation(((index % 16) * 100) + ((frame & 1) * 4), (index / 16) * 60);

    private static Color2D Color(int index) => index switch { 0 => new(1, 0, 0, 1), 1 => new(0, 1, 0, 1), 2 => new(0, 0, 1, 1), _ => new(1, 1, 0, 1) };

    private static DrawList2D Content(int color)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(8, 8, 24, 24), Color(color));
        return canvas.End();
    }
}
