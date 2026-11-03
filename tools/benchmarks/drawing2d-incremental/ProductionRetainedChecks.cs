using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class ProductionRetainedChecks
{
    internal static async Task RunAsync(VulkanDevice device)
    {
        foreach (TextureFormat format in new[] { TextureFormat.Rgba8Srgb, TextureFormat.Rgba16Float })
        {
            // Unsupported formats/commands throw with the named check; never pass by fallback.
            try
            {
                await CheckFormatAsync(device, format);
            }
            catch (NotSupportedException error)
            {
                throw new NotSupportedException("production-retained-mixed/" + format + ": " + error.Message, error);
            }
        }
    }

    private static async Task CheckFormatAsync(VulkanDevice device, TextureFormat format)
    {
        using var fixture = new MixedScene();
        using Renderer2D baselineRenderer = ProductionRetainedProbe.NewRenderer(device);
        using Renderer2D renderer = ProductionRetainedProbe.NewRenderer(device);
        using var baseline = new ProductionRetainedProbe.Slot(device, 64, format);
        using var actual = new ProductionRetainedProbe.Slot(device, 64, format);
        DrawList2D oldReference = fixture.Reference();
        using DrawingSnapshot2D oldRoot = fixture.Scene.CaptureSnapshot();
        byte[] oldPixels = await BaselineAsync(device, baselineRenderer, baseline, oldReference);
        for (int step = 0; step < 9; step++)
        {
            if (step != 0)
            {
                fixture.Edit(step);
            }

            DrawList2D reference = fixture.Reference();
            byte[] expected = await BaselineAsync(device, baselineRenderer, baseline, reference);
            using (DrawingSceneReadLease2D lease = fixture.Scene.Borrow())
            {
                actual.Retained = await renderer.PrepareAsync(lease, actual.Target);
            }

            // A real CPU edit AFTER await preparation and BEFORE Record must not alter
            // the prepared frame. Restore only after it has been encoded/submitted.
            Matrix3x2 saved = fixture.ChildTransform;
            fixture.Scene.SetTransform(fixture.Child, Matrix3x2.CreateTranslation(31.75f, 27.125f));
            actual.EncodeSubmit(device);
            fixture.Scene.SetTransform(fixture.Child, saved);
            await actual.CompleteAsync(null);
            ProductionRetainedProbe.Equal(expected, await actual.ReadAsync(device), "mixed/" + format + "/step" + step + "/borrow-released-update-before-record");
        }

        // Explicit oldRoot remains renderable after topology/content/clip changes and scene disposal.
        fixture.Scene.Dispose();
        actual.Retained = await renderer.PrepareAsync(oldRoot, actual.Target);
        actual.EncodeSubmit(device);
        await actual.CompleteAsync(null);
        ProductionRetainedProbe.Equal(oldPixels, await actual.ReadAsync(device), "old-snapshot/" + format);

        // Renderer disposal cannot destroy an outstanding prepared frame's assets.
        Renderer2D disposedRenderer = ProductionRetainedProbe.NewRenderer(device);
        actual.Retained = await disposedRenderer.PrepareAsync(oldRoot, actual.Target);
        disposedRenderer.Dispose();
        actual.EncodeSubmit(device);
        await actual.CompleteAsync(null);
        ProductionRetainedProbe.Equal(oldPixels, await actual.ReadAsync(device), "renderer-dispose-before-record/" + format);
    }

    private static async Task<byte[]> BaselineAsync(VulkanDevice device, Renderer2D renderer, ProductionRetainedProbe.Slot slot, DrawList2D reference)
    {
        slot.Legacy = await renderer.PrepareAsync(reference, slot.Target);
        slot.EncodeSubmit(device);
        await slot.CompleteAsync(null);
        return await slot.ReadAsync(device);
    }

    // Independent reference records the known tree's transforms/clips using the
    // existing Canvas2D API. No private retained types/reflection/geometry copy.
    private sealed class MixedScene : IDisposable
    {
        private readonly DrawList2D _shared = Shared();
        private readonly DrawList2D _boundary = Boundary();
        private readonly DrawingNodeId2D _group;
        private readonly DrawingNodeId2D _other;
        private readonly DrawingNodeId2D _boundaryNode;
        private readonly Path2D _parentClip = Rectangle(2.25f, 1.75f, 47, 46);
        private readonly Path2D _childClip = Rectangle(1, 2, 32, 33);
        private readonly Matrix3x2 _groupTransform = Matrix3x2.CreateRotation(0.035f) * Matrix3x2.CreateTranslation(4.125f, 3.375f);
        private readonly Matrix3x2 _otherTransform = Matrix3x2.CreateTranslation(13.25f, 9.75f);
        private DrawingNodeId2D _inserted;
        private DrawingNodeId2D _temporaryGroup;
        private bool _otherNested;
        private bool _otherRemoved;
        private bool _reversed;
        private bool _reparented;
        private bool _extra;
        private bool _clip = true;
        private DrawList2D _currentBoundary;
        private DrawingContent2D _content;

        internal MixedScene()
        {
            _currentBoundary = _boundary;
            _content = DrawingContent2D.FromDrawList(_boundary);
            if (_content.Count != 129)
            {
                throw new InvalidOperationException("Canonical page boundary fixture must have 129 commands.");
            }

            _boundaryNode = Scene.Insert(Scene.Root, 0, _content);
            _group = Scene.AddGroup(Scene.Root, 1);
            Scene.SetTransform(_group, _groupTransform);
            Scene.SetClip(_group, _parentClip);
            var shared = DrawingContent2D.FromDrawList(_shared);
            Child = Scene.Insert(_group, 0, shared);
            Scene.SetTransform(Child, ChildTransform);
            Scene.SetClip(Child, _childClip);
            _other = Scene.Insert(Scene.Root, 2, shared);
            Scene.SetTransform(_other, _otherTransform);
        }

        internal DrawingScene2D Scene { get; } = new();

        internal DrawingNodeId2D Child { get; }

        internal Matrix3x2 ChildTransform { get; private set; } = Matrix3x2.CreateTranslation(1.375f, 2.625f);

        public void Dispose() => Scene.Dispose();

        internal void Edit(int step)
        {
            switch (step)
            {
                case 1:
                    ChildTransform = Matrix3x2.CreateRotation(-0.07f) * Matrix3x2.CreateTranslation(9.375f, 3.125f);
                    Scene.SetTransform(Child, ChildTransform); break;
                case 2:
                    // Replace the final one-command page of 129, preserving pages 0/1.
                    var page = new Canvas2D(); page.Begin();
                    page.FillRect(new(24.125f, 29.375f, 18.5f, 14.25f), new Color2D(0.1f, 0.9f, 0.3f, 0.7f));
                    DrawList2D replacement = page.End();
                    _content = _content.WithPage(2, replacement); Scene.ReplaceContent(_boundaryNode, _content);

                    // Source reference uses the same 128-command prefix and replacement.
                    var prefix = new Canvas2D(); prefix.Begin();
                    RecordBoundary(prefix, 128); prefix.Replay(replacement); _currentBoundary = prefix.End();
                    break;
                case 3:
                    Scene.Move(_other, Scene.Root, 1); _reversed = true; break;
                case 4:
                    Scene.Move(Child, Scene.Root, 3); _reparented = true; break;
                case 5:
                    _inserted = Scene.Insert(_group, 0, DrawingContent2D.FromDrawList(_shared)); _extra = true;
                    Scene.SetClip(_group, null); _clip = false; break;
                case 6:
                    Scene.Remove(_inserted); _extra = false;
                    Scene.Move(Child, _group, 0); _reparented = false; break;
                case 7:
                    _temporaryGroup = Scene.AddGroup(Scene.Root, 3);
                    Scene.Move(_other, _temporaryGroup, 0); _otherNested = true; break;
                case 8:
                    Scene.Remove(_temporaryGroup); _otherRemoved = true; break;
                default: throw new ArgumentOutOfRangeException(nameof(step));
            }
        }

        internal DrawList2D Reference()
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            canvas.Replay(_currentBoundary);
            if (!_otherRemoved && !_otherNested && _reversed)
            {
                Other(canvas);
            }

            Group(canvas);
            if (!_otherRemoved && (_otherNested || !_reversed))
            {
                Other(canvas);
            }

            if (_reparented)
            {
                ChildDrawing(canvas);
            }

            return canvas.End();
        }

        private static DrawList2D Shared()
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            canvas.FillRect(new(0.125f, 0.375f, 28.5f, 23.25f), new Color2D(0.9f, 0.2f, 0.3f, 0.5f));
            canvas.Save();
            canvas.Translate(new(2.25f, 1.75f));
            canvas.ClipRect(new(1.5f, 2.5f, 28, 25));
            canvas.Rotate(0.09f);
            var gradient = PaintGraph2D.LinearGradient(new(0, 0), new(30, 20), [new(0, new(0.2f, 0.7f, 0.9f, 0.75f)), new(1, new(0.9f, 0.8f, 0.1f, 0.5f))]);
            var path = new PathBuilder2D();
            path.MoveTo(new(3, 2));
            path.CubicTo(new(30, 0), new(35, 30), new(5, 27));
            path.Close();
            canvas.FillPath(path.Build(), gradient);
            canvas.FillRect(new(7.25f, 9.125f, 20, 17), gradient);
            canvas.Restore();
            return canvas.End();
        }

        private static DrawList2D Boundary()
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            RecordBoundary(canvas, 129);
            return canvas.End();
        }

        private static void RecordBoundary(Canvas2D canvas, int count)
        {
            for (int i = 0; i < count; i++)
            {
                canvas.FillRect(
                    new(((i % 16) * 3.25f) + 0.125f, ((i / 16) * 5.5f) + 0.375f, 4.5f, 6.25f),
                    new Color2D(0.2f, (i % 7) * 0.1f, 0.6f, 0.45f));
            }
        }

        private static Path2D Rectangle(float x, float y, float width, float height)
        {
            var path = new PathBuilder2D();
            path.MoveTo(new(x, y));
            path.LineTo(new(x + width, y));
            path.LineTo(new(x + width, y + height));
            path.LineTo(new(x, y + height));
            path.Close();
            return path.Build();
        }

        private void Group(Canvas2D canvas)
        {
            canvas.Save();
            canvas.ConcatTransform(_groupTransform);
            if (_clip)
            {
                canvas.ClipPath(_parentClip);
            }

            if (!_reparented)
            {
                ChildDrawing(canvas);
            }

            if (_extra)
            {
                canvas.Replay(_shared);
            }

            canvas.Restore();
        }

        private void Other(Canvas2D canvas)
        {
            canvas.Save();
            canvas.ConcatTransform(_otherTransform);
            canvas.Replay(_shared);
            canvas.Restore();
        }

        private void ChildDrawing(Canvas2D canvas)
        {
            canvas.Save();
            canvas.ConcatTransform(ChildTransform);
            canvas.ClipPath(_childClip);
            canvas.Replay(_shared);
            canvas.Restore();
        }
    }
}
