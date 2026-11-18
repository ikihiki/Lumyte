using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Tests;

/// <summary>Real backend execution with only a smaller reported storage range, never changed hardware binding limits.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks two separately bound membership pages and graph effects on Vulkan.</summary>
    /// <returns>The completed full-pixel GPU readback.</returns>
    [GpuFact]
    public async Task VulkanMembershipMultipageGraphPixelsAsync()
    {
        using var raw = VulkanDevice.Create();
        await CheckMembershipMultipageGraphPixelsAsync(raw);
    }

    /// <summary>Checks two separately bound membership pages at the actual wgpu seven-buffer limit.</summary>
    /// <returns>The completed full-pixel GPU readback.</returns>
    [GpuFact]
    public async Task WgpuMembershipMultipageGraphPixelsAsync()
    {
        using var raw = WgpuDevice.Create();
        Assert.Equal(7u, raw.Caps.ArgumentTableBindingLimits.BuffersPerDraw);
        await CheckMembershipMultipageGraphPixelsAsync(raw);
    }

    private static PartitionDevice BoundedMembershipDevice(IGraphicDevice raw)
    {
        var device = new PartitionDevice(raw, raw.Caps.ArgumentTableBindingLimits.TextureSlots, 65536);
        Assert.Equal(raw.Caps.ArgumentTableBindingLimits with { MaximumBufferRangeBytes = 65536 }, device.Caps.ArgumentTableBindingLimits);
        Assert.Equal(65536UL, device.Caps.MaxStorageBufferBindingSize);
        return device;
    }

    private static async Task CheckMembershipMultipageGraphPixelsAsync(IGraphicDevice raw)
    {
        PartitionDevice device = BoundedMembershipDevice(raw);
        using IRenderer2D renderer = CreateMembershipOracleRenderer(device);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillPath(MultipageBox(0), new SRgba<float>(1, 0, 0, 0.5f));
        canvas.FillPath(MultipageBox(20), new SRgba<float>(0, 1, 0, 0.5f));
        using var graph = new Graph(device, "membership-two-pages-native-effects");
        (EffectImage image, PreparedDrawingPasses2D drawing) = await AppendMembershipImageAsync(graph, renderer, canvas.End(), new(0, 0, 128, 128));
        using IDisposable drawingOwner = drawing;
        EffectImage output = graph.AppendOpacity(image, 0.5f);
        renderer.Dispose();
        uint pitch = OracleRowPitch(device, 128);
        Lumyte.Graphics.RenderGraph.RenderGraphBuffer<byte> readback = graph.CreateBuffer<byte>(new() { Count = pitch * 128, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }, "multipage-readback");
        graph.AddPass("multipage-copy", context => context.Commands.CopyTextureToBuffer(new() { Texture = context.GetTexture(output.Texture), Width = 128, Height = 128 }, new() { Buffer = context.GetBuffer(readback).SliceElements(0, pitch * 128), BytesPerRow = pitch, RowsPerImage = 128 }))
            .UseTexture(output.Texture, TextureState.CopySource, _copyRead).UseBuffer(readback, _copyWrite);
        graph.ExportBuffer(readback, new(PipelineStage.Host, ResourceAccess.HostRead));
        using Lumyte.Graphics.RenderGraph.CompiledRenderGraph compiled = graph.Compile();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var observed = new PartitionCommands(commands);
        compiled.Record(observed);
        Assert.Equal(2, observed.ComputePasses);
        Assert.Equal(2, observed.ComputeDispatches);
        Assert.Equal(2, observed.ComputeRoots.Count);
        int firstCompute = observed.WorkSequence.IndexOf("compute");
        int secondCompute = observed.WorkSequence.IndexOf("compute", firstCompute + 1);
        Assert.Contains("copy", observed.WorkSequence.Take(firstCompute));
        Assert.Contains("raster", observed.WorkSequence.Skip(firstCompute + 1).Take(secondCompute - firstCompute - 1));
        Assert.Contains("copy", observed.WorkSequence.Skip(firstCompute + 1).Take(secondCompute - firstCompute - 1));
        Assert.Contains("raster", observed.WorkSequence.Skip(secondCompute + 1));
        Assert.Equal(2, observed.Roots.Count(root => root.Snapshot.ShaderTypeName == "DrawRangeArguments2D"));
        object[] tables = observed.Roots.Where(root => root.Snapshot.ShaderTypeName == "DrawRangeArguments2D").Select(root => (object)root.Table).ToArray();
        Assert.NotSame(tables[0], tables[1]);
        IShaderReference[] masks = observed.ComputeRoots.Select(root => root.Values.Select(value => Assert.IsAssignableFrom<IShaderReference>(value.Reference)).Single(reference => reference.Resource is IGraphicsBuffer<uint>)).ToArray();
        Assert.NotSame(masks[0].Resource, masks[1].Resource);
        Assert.All(masks, mask =>
        {
            Assert.InRange(mask.SizeInBytes, 40000UL, 65536UL);
            Assert.Equal(ShaderBufferAccess.ReadWrite, mask.BufferAccess);
        });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        Vector4[] pixels = await DecodeMembershipOraclePixelsAsync(compiled.GetBuffer(readback), 128, 128, pitch);
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                AssertPixel(pixels, 128, x, y, MultipagePixel(x, y, 0, false, blue: false) * 0.5f);
            }
        }
    }

    private static Path2D MultipageBox(float offset)
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(offset, offset));
        path.LineTo(new(offset + 100, offset));
        path.LineTo(new(offset + 100, offset + 100));
        path.LineTo(new(offset, offset + 100));
        path.Close();
        return path.Build();
    }

    private static Vector4 MultipagePixel(int x, int y, int translation, bool replaced, bool blue)
    {
        bool first = x >= translation && x < translation + 100 && y < 100;
        bool second = x >= 20 + translation && x < 120 + translation && y >= 20 && y < 120;
        Vector4 pixel = blue ? new(0, 0, 1, 1) : Vector4.Zero;
        if (first)
        {
            pixel = (replaced ? new Vector4(0, 0.5f, 0, 0.5f) : new Vector4(0.5f, 0, 0, 0.5f)) + (pixel * 0.5f);
        }

        if (second)
        {
            pixel = (replaced ? new Vector4(0.5f, 0, 0, 0.5f) : new Vector4(0, 0.5f, 0, 0.5f)) + (pixel * 0.5f);
        }

        return pixel;
    }
}
