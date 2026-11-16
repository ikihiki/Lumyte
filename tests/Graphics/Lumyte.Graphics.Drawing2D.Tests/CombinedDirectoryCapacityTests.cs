using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Admission bounds the physical paint/clip binding before native allocation.</summary>
public sealed class CombinedDirectoryCapacityTests
{
    /// <summary>Nested clips add both directory kinds, even when separated by opacity.</summary>
    /// <param name="maximum">The exact native binding limit.</param>
    /// <param name="accepted">Whether the complete draw fits.</param>
    /// <returns>The completed public preparation assertions.</returns>
    [Theory]
    [InlineData(1232ul, false)]
    [InlineData(1263ul, false)]
    [InlineData(1264ul, true)]
    public async Task NestedClipAdmissionUsesCombinedDirectoryAsync(ulong maximum, bool accepted)
    {
        ProfilingDevice device = Device(maximum);
        using var renderer = Renderer2D.CreateDefault(device);
        using var image = new DispatchTexture();
        using IGraphicsTextureView a = image.CreateView();
        using IGraphicsTextureView b = image.CreateView();
        using IGraphicsTextureView c = image.CreateView();
        using var target = new DispatchTexture();
        using IGraphicsTextureView targetView = target.CreateView();
        var paint = PaintGraph2D.Composite(Pattern(a), PaintGraph2D.Composite(Pattern(b), Pattern(c)));
        Path2D clip = Rectangle();
        for (int i = 0; i < 24; i++)
        {
            paint = PaintGraph2D.Opacity(0.5f, PaintGraph2D.Clip(clip, paint));
        }

        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillPath(Triangle(), paint);
        DrawList2D list = canvas.End();
        if (!accepted)
        {
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                using PreparedDraw2D unexpected = await renderer.PrepareAsync(list, new() { View = targetView });
            });
            Assert.Equal(0, device.NativeBuffers);
            Assert.Empty(device.Tables);
            return;
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = targetView });
        Assert.Equal(79ul, Assert.Single(device.ShaderUploads, upload => upload.Type == typeof(DrawDirectoryEntry2D)).Count);
        AssertBindingRanges(device, maximum);
    }

    /// <summary>Whole draws partition before overflow, with valid relocated paint and outer clip references.</summary>
    /// <param name="count">The number of distinct paint closures.</param>
    /// <returns>The completed page and sentinel assertions.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PartitionPreservesClipReferencesAsync(int count)
    {
        ProfilingDevice device = Device(1280);
        using var renderer = Renderer2D.CreateDefault(device);
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new(0.25f, 0, 4, 4));
        for (int draw = 0; draw < count; draw++)
        {
            var paint = PaintGraph2D.Solid(new((draw + 1) * 0.25f, 0, 0, 1));
            for (int i = 0; i < 24; i++)
            {
                paint = PaintGraph2D.Opacity(0.5f, PaintGraph2D.Clip(Rectangle(), paint));
            }

            canvas.FillPath(Triangle(), paint);
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        Assert.Equal(count, device.ShaderUploads.Count(upload => upload.Type == typeof(DrawDirectoryEntry2D)));
        AssertBindingRanges(device, 1280);
        Assert.Equal(count, device.Records.Count);
        Assert.All(device.Records, record => Assert.True(record.ClipId > record.PaintId));
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        Assert.Equal(count, commands.Events.OfType<RecordedDrawRange>().Count());
    }

    /// <summary>An unused clip sentinel is not appended to the physical paint directory.</summary>
    /// <returns>The completed sentinel assertions.</returns>
    [Fact]
    public async Task NoClipDoesNotChargeUnusedSentinelAsync()
    {
        ProfilingDevice device = Device(128);
        using var renderer = Renderer2D.CreateDefault(device);
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillPath(Triangle(), new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1));
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        Assert.Equal(2ul, Assert.Single(device.ShaderUploads, upload => upload.Type == typeof(DrawDirectoryEntry2D)).Count);
        Assert.Equal(0u, Assert.Single(device.Records).ClipId);
        AssertBindingRanges(device, 128);
    }

    private static ProfilingDevice Device(ulong maximum) => new()
    {
        Caps = new()
        {
            ShaderTarget = ShaderTarget.Wgsl,
            MaxBufferSize = 1UL << 30,
            MaxStorageBufferBindingSize = maximum,
            MaxComputeInvocationsPerWorkgroup = 256,
            MaxComputeWorkgroupSizeX = 16,
            MaxComputeWorkgroupSizeY = 16,
            MaxComputeWorkgroupSizeZ = 1,
            MaxComputeWorkgroupsPerDimension = 65535,
            ArgumentTableBindingLimits = new() { BuffersPerDraw = 7, BufferSlots = 7, MaximumBufferRangeBytes = maximum },
        },
    };

    private static void AssertBindingRanges(ProfilingDevice device, ulong maximum)
        => Assert.All(device.ShaderValues.SelectMany(value => value.Values), value =>
        {
            if (value.Reference is IShaderReference reference && reference.SizeInBytes != 0)
            {
                Assert.InRange(reference.RegistrationSizeInBytes, 1ul, maximum);
            }
        });

    private static PaintGraph2D Pattern(IGraphicsTextureView view) => PaintGraph2D.ImagePattern(new() { View = view, Source = new(0, 0, 1, 1) });

    private static Path2D Triangle()
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(0, 0));
        path.LineTo(new(2, 0));
        path.LineTo(new(0, 2));
        path.Close();
        return path.Build();
    }

    private static Path2D Rectangle()
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(0, 0));
        path.LineTo(new(4, 0));
        path.LineTo(new(4, 4));
        path.LineTo(new(0, 4));
        path.Close();
        return path.Build();
    }
}
