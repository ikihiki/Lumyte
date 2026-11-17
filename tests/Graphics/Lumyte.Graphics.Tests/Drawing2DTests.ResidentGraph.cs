using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises submitted resident frames inside an actual shared graph.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Graph initialization executes once and completion enables immutable reuse on Vulkan.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task VulkanResidentGraphAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckResidentGraphAsync(device);
    }

    /// <summary>Graph initialization executes once and completion enables immutable reuse on wgpu.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task WgpuResidentGraphAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckResidentGraphAsync(device);
    }

    private static async Task CheckResidentGraphAsync(IGraphicDevice device)
    {
        foreach (bool standard in new[] { false, true })
        {
            using Renderer2D renderer = standard ? Renderer2D.CreateDefault(device, retentionOptions: new() { EnableResidentInstances = true }) : new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
            using var scene = new DrawingScene2D();
            scene.Insert(scene.Root, 0, ResidentPrimitive(false));
            scene.Insert(scene.Root, 1, ResidentCorrelatedPath());
            using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
            object? originalPayload = null;
            for (int frameIndex = 0; frameIndex < 2; frameIndex++)
            {
                using var slot = new SharedArgumentFrame(device);
                slot.Frame = await renderer.PrepareAsync(snapshot, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear });
                using var graph = new Graph(device);
                RenderGraphTexture target = graph.ImportTexture(slot.Texture, TextureState.Undefined, default);
                var bindings = new DrawingGraphBindings2D(graph);
                bindings.BindTexture(slot.Texture, target);
                graph.AppendResident2D(slot.Frame, new() { Texture = target, Description = new() { Width = 8, Height = 8, Format = slot.Texture.Format, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource }, PixelScale = 1 }, bindings);
                graph.MarkOutput(target);
                using CompiledRenderGraph plan = graph.Compile();
                var observed = new PartitionCommands(slot.Commands);
                plan.Record(observed);
                ResidentTargetReadback(observed, slot);
                observed.Finish();
                Assert.Throws<InvalidOperationException>(slot.Frame.Retire);
                slot.Submission = device.Queue.Submit([slot.Commands]);
                slot.Frame.AttachSubmission(slot.Submission);
                await slot.Submission.WaitAsync();
                slot.Commands.Dispose();
                plan.Dispose();
                graph.Dispose();
                slot.Frame.Retire();
                slot.Retired = true;
                IShaderReference reference = Assert.IsAssignableFrom<IShaderReference>(observed.Roots[0].Snapshot.Values.Single(value => value.Offset == 0).Reference);
                if (frameIndex == 0)
                {
                    Assert.True(observed.PayloadCopyBytes > 0);
                    originalPayload = reference.Resource;
                }
                else
                {
                    Assert.Equal(0ul, observed.PayloadCopyBytes);
                    Assert.Equal(0, observed.ComputePasses);
                    Assert.Same(originalPayload, reference.Resource);
                }

                Console.WriteLine($"residentGraph standard={standard} frame={frameIndex} payloadBytes={observed.PayloadCopyBytes} compute={observed.ComputePasses}");
                Vector4[] actual = await DecodeOraclePixelsAsync(slot.Readback, 8, 8, slot.Pitch);
                Assert.Equal(await FreshResidentReferenceAsync(device, 0, standard), actual);
            }
        }
    }
}
