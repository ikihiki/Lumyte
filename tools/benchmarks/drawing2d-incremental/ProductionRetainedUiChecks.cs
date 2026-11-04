using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.TextEditing;
using Lumyte.UI;
using Lumyte.UI.TextEditing;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class ProductionRetainedUiChecks
{
    internal static async Task RunAsync(VulkanDevice device)
    {
        using var text = new UiWorkloadText();
        foreach (TextureFormat format in new[] { TextureFormat.Rgba8Srgb, TextureFormat.Rgba16Float })
        {
            using var surface = new UiSurface(UiWorkloadText.Column("retained-ui", 210, text.Label("first editor", "first", 190, true), text.Label("second editor", "second", 190)));
            surface.SetViewport(new(256, 128));
            using UiDrawingScene2D drawing = surface.CreateDrawingScene();
            using Renderer2D legacyRenderer = ProductionRetainedProbe.NewRenderer(device);
            using Renderer2D retainedRenderer = ProductionRetainedProbe.NewRenderer(device);
            using var reference = new ProductionRetainedProbe.Slot(device, 256, format, 128);
            using var actual = new ProductionRetainedProbe.Slot(device, 256, format, 128);
            for (int step = 0; step < 5; step++)
            {
                if (step == 2)
                {
                    var editor = (TextBoxElement)surface.RootElement.Children[0];
                    TextDocumentSnapshot snapshot = editor.Document.CaptureSnapshot();
                    if (!editor.Document.TryReplace(snapshot.Revision, new(0, snapshot.Text.Length), "changed leaf", new(12, 12), TextEditOrigin.Keyboard))
                    {
                        throw new InvalidOperationException("The real UI document edit was rejected.");
                    }
                }

                if (step == 3)
                {
                    surface.SetViewport(new(173, 91));
                }

                drawing.Update();
                if (step == 1 && drawing.LastRecordingCount != 0)
                {
                    throw new InvalidOperationException("Unchanged real UI content was recorded again.");
                }

                DrawList2D oldDrawing = UiWorkloadText.Capture(surface);
                reference.Legacy = await legacyRenderer.PrepareAsync(oldDrawing, reference.Target);
                reference.EncodeSubmit(device);
                await reference.CompleteAsync(null);
                using (DrawingSceneReadLease2D lease = drawing.Scene.Borrow())
                {
                    actual.Retained = await retainedRenderer.PrepareAsync(lease, actual.Target);
                }

                actual.EncodeSubmit(device);
                await actual.CompleteAsync(null);
                ProductionRetainedProbe.Equal(await reference.ReadAsync(device), await actual.ReadAsync(device), "production-ui-adapter/" + format + "/step" + step);
            }
        }
    }
}
