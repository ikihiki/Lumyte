using System.Collections.ObjectModel;
using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class Content2DWorkloads
{
    internal static IReadOnlyList<string> Cases { get; } = Array.AsReadOnly(new[]
    {
        "2d-transform-all",
        "2d-geometry-stream",
        "2d-camera-pan",
        "2d-static",
    });

    internal static IRenderingWorkload Create(string name, int count)
    {
        if (!Cases.Contains(name))
        {
            throw new ArgumentException("Unknown 2D workload.", nameof(name));
        }

        // A caller can select 10,000 paths explicitly; never silently expand a
        // default stream into a 10,000-path tessellation/gradient workload.
        if (count == 0)
        {
            count = name == "2d-geometry-stream" ? 200 : 10000;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 100000);
        return new ContentWorkload(name, count);
    }

    private sealed class ContentWorkload : IRenderingWorkload
    {
        private static readonly Rect2D _sharedRectangle = new(-5.25f, -3.75f, 10.5f, 7.5f);
        private static readonly PaintGraph2D[] _palette =
        [
            PaintGraph2D.Solid(new(0.16f, 0.56f, 0.84f, 0.72f)),
            PaintGraph2D.Solid(new(0.93f, 0.38f, 0.15f, 0.61f)),
            PaintGraph2D.LinearGradient(new(-6, -5), new(7, 6), [new(0, new(0.18f, 0.76f, 0.45f, 0.8f)), new(1, new(0.63f, 0.24f, 0.88f, 0.55f))]),
        ];

        private readonly int _count;
        private readonly Canvas2D _canvas = new();
        private readonly Matrix3x2[] _transforms;
        private readonly Path2D[] _paths;
        private readonly Dictionary<string, long> _counters;
        private readonly IReadOnlyDictionary<string, long> _counterView;
        private IReadOnlyList<DrawList2D>? _layers;
        private DrawList2D? _initial;
        private DrawingCommand2D[]? _initialCommands;
        private PathSegment2D[][]? _initialSegments;
        private int _recordedFrame = -1;
        private bool _disposed;

        internal ContentWorkload(string name, int count)
        {
            Name = name;
            _count = count;
            _transforms = new Matrix3x2[count];
            _paths = new Path2D[name is "2d-camera-pan" or "2d-geometry-stream" ? count : 0];
            _counters = new Dictionary<string, long>
            {
                ["logicalObjects"] = count,
                ["viewportWidth"] = 1280,
                ["viewportHeight"] = 720,
                ["physicalUI"] = 0,
                ["layoutPasses"] = 0,
                ["layoutNoOpCalls"] = 0,
                ["updateCalls"] = 0,
                ["recordCalls"] = 0,
                ["canvasRecordings"] = 0,
                ["cachedRecordReturns"] = 0,
                ["changedTransforms"] = 0,
                ["changedGeometry"] = 0,
                ["changedOrder"] = 0,
                ["changedPaints"] = 0,
                ["changedCameraTransforms"] = 0,
                ["totalChangedTransforms"] = 0,
                ["totalChangedGeometry"] = 0,
                ["totalChangedCameraTransforms"] = 0,
            };
            _counterView = new ReadOnlyDictionary<string, long>(_counters);
            SetInputs();
        }

        public string Suite => "2d";

        public string Name { get; }

        public int Frame { get; private set; }

        public IReadOnlyDictionary<string, long> Counters => _counterView;

        public string Description => Name switch
        {
            "2d-transform-all" => "All untextured rectangle positions/rotations change; shared rectangle/paint definitions; full Canvas recording, not resident GPU instances.",
            "2d-geometry-stream" => "Every compact curved path changes each update; stable shared paints include a real gradient; full Canvas recording.",
            "2d-camera-pan" => "Static curved map paths and paints reused under one changing camera transform; full Canvas recording/renderer preparation remain measurable, not a GPU compiled-map cache.",
            _ => "Immutable rectangle DrawList reused after first recording; no layout or changing inputs.",
        };

        public void Update()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Frame = checked(Frame + 1);
            if (Name is "2d-transform-all" or "2d-geometry-stream")
            {
                SetInputs();
            }

            long transforms = Name == "2d-transform-all" ? _count : 0;
            long geometry = Name == "2d-geometry-stream" ? _count : 0;
            long camera = Name == "2d-camera-pan" ? 1 : 0;
            _counters["updateCalls"]++;
            _counters["changedTransforms"] = transforms;
            _counters["changedGeometry"] = geometry;
            _counters["changedCameraTransforms"] = camera;
            _counters["totalChangedTransforms"] += transforms;
            _counters["totalChangedGeometry"] += geometry;
            _counters["totalChangedCameraTransforms"] += camera;
        }

        public void Layout()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _counters["layoutNoOpCalls"]++;
        }

        public IReadOnlyList<DrawList2D> Record()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _counters["recordCalls"]++;
            if (_layers is not null && (Name == "2d-static" || _recordedFrame == Frame))
            {
                _counters["cachedRecordReturns"]++;
                return _layers;
            }

            _canvas.Begin();
            if (Name == "2d-camera-pan")
            {
                _canvas.Translate(CameraOffset(Frame));
            }

            for (int i = 0; i < _count; i++)
            {
                _canvas.Save();
                _canvas.ConcatTransform(_transforms[i]);
                if (_paths.Length != 0)
                {
                    _canvas.FillPath(_paths[i], _palette[i % _palette.Length]);
                }
                else
                {
                    _canvas.FillRect(_sharedRectangle, _palette[i % _palette.Length]);
                }

                _canvas.Restore();
            }

            DrawList2D drawing = _canvas.End();
            _layers = Array.AsReadOnly(new[] { drawing });
            _recordedFrame = Frame;
            _counters["canvasRecordings"]++;
            return _layers;
        }

        public void Verify()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_count < 1 || Frame < 0 || _transforms.Length != _count)
            {
                throw new InvalidOperationException("Invalid 2D workload input bounds.");
            }

            // Verify outside the recording stage: retaining/checking old data
            // must not add artificial snapshot copies to the measured baseline.
            if (_initial is not null)
            {
                CheckSnapshot(_initial, _initialCommands!, _initialSegments!);
            }

            if (_layers is null)
            {
                return;
            }

            if (_layers.Count != 1 || _layers[0].Commands.Count != _count)
            {
                throw new InvalidOperationException("Wrong ordered layer/command count.");
            }

            DrawList2D drawing = _layers[0];
            for (int i = 0; i < _count; i++)
            {
                DrawingCommand2D command = drawing.Commands[i];
                Matrix3x2 expected = TransformFor(i, _recordedFrame);
                if (Name == "2d-camera-pan")
                {
                    expected *= Matrix3x2.CreateTranslation(CameraOffset(_recordedFrame));
                }

                if (!Finite(command.State.Transform) || command.State.Transform != expected || command.State.Clip.Elements.Count != 0)
                {
                    throw new InvalidOperationException("Invalid command transform/clip.");
                }

                PaintGraph2D paint = _palette[i % _palette.Length];
                if (_paths.Length == 0)
                {
                    if (command is not FillRectCommand2D rect || rect.Rect != _sharedRectangle || !ReferenceEquals(rect.Paint, paint))
                    {
                        throw new InvalidOperationException("Rectangle definition/order/paint changed.");
                    }
                }
                else
                {
                    if (command is not FillPathCommand2D path || !ReferenceEquals(path.Paint, paint))
                    {
                        throw new InvalidOperationException("Path type/order/paint changed.");
                    }

                    Path2D expectedPath = Name == "2d-camera-pan" ? _paths[i] : BuildPath(i, _recordedFrame);
                    if (!path.Path.Segments.SequenceEqual(expectedPath.Segments))
                    {
                        throw new InvalidOperationException("Path geometry differs from deterministic frame inputs.");
                    }

                    foreach (PathSegment2D segment in path.Path.Segments)
                    {
                        if (!Finite(segment.P0) || !Finite(segment.P1) || !Finite(segment.P2))
                        {
                            throw new InvalidOperationException("Nonfinite path geometry.");
                        }
                    }
                }
            }

            if (_initial is null)
            {
                _initial = drawing;
                _initialCommands = drawing.Commands.ToArray();
                _initialSegments = drawing.Commands.Select(command => command is FillPathCommand2D path ? path.Path.Segments.ToArray() : Array.Empty<PathSegment2D>()).ToArray();
            }

            if (Name == "2d-static" && !ReferenceEquals(_initial, drawing))
            {
                throw new InvalidOperationException("Static recording was not reused.");
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _layers = null;
            _initial = null;
            _initialCommands = null;
            _initialSegments = null;
        }

        private static Vector2 CameraOffset(int frame) => new(-(((frame % 4096) * 0.375f) % 48), MathF.Sin((frame % 4096) * 0.025f) * 9.5f);

        private static Path2D BuildPath(int index, int frame)
        {
            float wave = MathF.Sin(((frame % 4096) * 0.031f) + ((index % 37) * 0.17f)) * 1.75f;
            var builder = new PathBuilder2D();
            builder.MoveTo(new(-5.25f, -3.5f));
            builder.CubicTo(new(-2.5f, -6.25f + wave), new(4.75f, -4 + wave), new(5.5f, 0.25f));
            builder.CubicTo(new(4.5f, 5 + wave), new(-3.75f, 5.25f - wave), new(-5.25f, -3.5f));
            builder.Close();
            return builder.Build();
        }

        private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

        private static bool Finite(Matrix3x2 value) => float.IsFinite(value.M11) && float.IsFinite(value.M12)
            && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M31) && float.IsFinite(value.M32);

        private static void CheckSnapshot(DrawList2D drawing, DrawingCommand2D[] commands, PathSegment2D[][] segments)
        {
            if (drawing.Commands.Count != commands.Length)
            {
                throw new InvalidOperationException("Old snapshot count mutated.");
            }

            for (int i = 0; i < commands.Length; i++)
            {
                if (!ReferenceEquals(drawing.Commands[i], commands[i]) || drawing.Commands[i] != commands[i])
                {
                    throw new InvalidOperationException("Old snapshot command reference mutated.");
                }

                if (commands[i] is FillPathCommand2D path && !path.Path.Segments.SequenceEqual(segments[i]))
                {
                    throw new InvalidOperationException("Old snapshot path segments mutated.");
                }
            }
        }

        private void SetInputs()
        {
            for (int i = 0; i < _count; i++)
            {
                _transforms[i] = TransformFor(i, Frame);
                if (_paths.Length != 0 && (Name == "2d-geometry-stream" || _paths[i] is null))
                {
                    _paths[i] = BuildPath(i, Name == "2d-camera-pan" ? 0 : Frame);
                }
            }
        }

        private Matrix3x2 TransformFor(int index, int frame)
        {
            // A bounded phase avoids growing float inputs; frame advancement is
            // checked rather than overflowing the deterministic frame counter.
            float phase = (frame % 4096) * 0.017f;
            float x = 18.25f + ((index % 100) * 12.5f);
            float y = 15.75f + (((index / 100) % 60) * 11.5f);
            if (Name != "2d-transform-all")
            {
                return Matrix3x2.CreateTranslation(x, y);
            }

            return Matrix3x2.CreateRotation(phase + ((index % 31) * 0.09f))
                * Matrix3x2.CreateTranslation(x + (MathF.Sin(phase + (index * 0.13f)) * 7.5f), y + (MathF.Cos(phase + (index * 0.07f)) * 5.25f));
        }
    }
}
