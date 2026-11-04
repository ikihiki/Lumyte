extern alias UiBenchmarks;

using System.Security.Cryptography;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Wgpu;
using UiProfile = UiBenchmarks::Lumyte.Graphics.UiScenarios.UiProfile;
using UiScenario = UiBenchmarks::Lumyte.Graphics.UiScenarios.UiScenario;

namespace Lumyte.Graphics.Presentation.Benchmarks;

internal static class UiPixelGate
{
    internal static object Verify(WgpuDevice device, UiProfile profile, TextureFormat format, int frame, RetirementScope rootOwnership)
    {
        using var live = new UiScenario(profile);
        live.Update(frame);
        live.Layout();
        return VerifyFixture(device, live, format, frame, rootOwnership);
    }

    internal static object VerifyFixture(WgpuDevice device, UiScenario live, TextureFormat format, int frame, RetirementScope rootOwnership)
    {
        UiProfile profile = live.Profile;
        object nativeState = live.Verify();
        IReadOnlyList<DrawList2D> lists = live.Record();

        // Fresh independent mount from pure logical input, not cached live recordings.
        IReadOnlyList<DrawList2D> reference = live.Reference();
        byte[] actual = Render(device, lists, format, rootOwnership, out object actualShaders);
        byte[] expected = Render(device, reference, format, rootOwnership, out object referenceShaders);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException("Independent fresh-mount U03 pixels differ.");
        }

        Check(ClearFixture.Width - 12, ClearFixture.Height - 12, 255, 0, 0);
        Check(ClearFixture.Width - 13, ClearFixture.Height - 13, 0, 0, 255);
        return new
        {
            status = "passed",
            profile.Key,
            logicalFrame = frame,
            live.Input.InputId,
            live.FontHash,
            nativeState,
            pixelSha256 = Convert.ToHexString(SHA256.HashData(actual)),
            independent = "fresh UiSurface mount from logical input; same product layout/render algorithms, not algorithm-independent",
            analytic = "exact red plus clipped blue/order sentinel after RGBA8-sRGB -> BGRA8-sRGB composite, raw BGRA channel-aware",
            intermediateFormat = "Rgba8Srgb",
            compositeTargetFormat = format.ToString(),
            actualShaders,
            referenceShaders,
        };

        void Check(uint x, uint y, byte red, byte green, byte blue)
        {
            int offset = checked((int)(((y * ClearFixture.Width) + x) * 4));
            bool bgra = format is TextureFormat.Bgra8Srgb or TextureFormat.Bgra8Unorm;
            if (actual[offset] != (bgra ? blue : red) || actual[offset + 1] != green || actual[offset + 2] != (bgra ? red : blue) || actual[offset + 3] != 255)
            {
                throw new InvalidOperationException("Independent analytic UI red/blue clip/order sentinel failed.");
            }
        }
    }

    private static byte[] Render(WgpuDevice device, IReadOnlyList<DrawList2D> lists, TextureFormat format, RetirementScope rootOwnership, out object shaderProvenance)
    {
        using var ownership = new RetirementScope { Parent = rootOwnership };
        Renderer2D renderer = ownership.Own(new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault()));
        UiCompositePass compositor = ownership.Own(new UiCompositePass(device, ownership));
        UiCompositeTarget intermediate = ownership.Own(compositor.CreateTarget(device, ownership));
        shaderProvenance = compositor.ShaderProvenance;
        IGraphicsTexture texture = ownership.Own(device.CreateTexture(new()
        {
            Width = ClearFixture.Width,
            Height = ClearFixture.Height,
            Format = format,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
        }));
        IGraphicsTextureView view = ownership.Own(texture.CreateView());
        const uint Pitch = ClearFixture.Width * 4;
        const ulong Count = (ulong)Pitch * ClearFixture.Height;
        IGraphicsBuffer<byte> buffer = ownership.Own(device.CreateBuffer<byte>(new()
        {
            Count = Count,
            Usage = BufferUsage.CopyDestination,
            Memory = MemoryPreference.Readback,
        }));
        var prepared = new List<PreparedDraw2D>();
        IGraphicsCommandBuffer commands = ownership.Own(device.CreateCommandBuffer(new()));
        IGraphicsSubmission? submission = null;
        bool uncertain = false;
        try
        {
            foreach (DrawList2D list in lists)
            {
                prepared.Add(renderer.PrepareAsync(list, new()
                {
                    View = intermediate.View,
                    LoadOp = prepared.Count == 0 ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
                }).AsTask().GetAwaiter().GetResult());
            }

            intermediate.Begin(commands);
            for (int layer = 0; layer < prepared.Count; layer++)
            {
                if (layer != 0)
                {
                    intermediate.BetweenLayers(commands);
                }

                prepared[layer].Record(commands);
            }

            compositor.Record(commands, intermediate, texture, view, present: false);
            commands.Barrier(new TextureBarrierDesc
            {
                Texture = texture,
                Range = new(0, 1, 0, 1),
                BeforeState = TextureState.ColorAttachment,
                AfterState = TextureState.CopySource,
                Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite),
                After = new(PipelineStage.Copy, ResourceAccess.CopyRead),
            });

            commands.CopyTextureToBuffer(new() { Texture = texture, Width = ClearFixture.Width, Height = ClearFixture.Height }, new()
            {
                Buffer = buffer.SliceElements(0, Count),
                BytesPerRow = Pitch,
                RowsPerImage = ClearFixture.Height,
            });
            commands.Barrier(new BufferBarrierDesc<byte>
            {
                Buffer = buffer.SliceElements(0, Count),
                Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
                After = new(PipelineStage.Host, ResourceAccess.HostRead),
            });
            commands.Finish();
            uncertain = true;
            submission = device.Queue.Submit([commands]);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            submission.WaitAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
            uncertain = false;
            uncertain = true;
            buffer.MapAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
            uncertain = false;
            try
            {
                byte[] bytes = new byte[checked((int)Count)];
                buffer.CopyTo(bytes);
                return bytes;
            }
            finally
            {
                buffer.Unmap();
            }
        }
        finally
        {
            if (uncertain)
            {
                ownership.Quarantine(prepared, submission!);
                rootOwnership.Quarantine(ownership);
            }
            else
            {
                try
                {
                    submission?.Dispose();
                    ownership.Release(commands);
                    foreach (PreparedDraw2D draw in prepared)
                    {
                        draw.Dispose();
                    }
                }
                catch
                {
                    ownership.Quarantine(prepared, submission!);
                    throw;
                }
            }
        }
    }
}
