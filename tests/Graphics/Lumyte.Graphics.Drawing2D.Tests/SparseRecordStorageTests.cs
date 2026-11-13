using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks allocation follows unique payload values rather than command count or a nominal page size.</summary>
public sealed class SparseRecordStorageTests
{
    /// <summary>A thousand mixed simple shapes use only a record stream and two palette entries.</summary>
    /// <returns>The completed preparation and actual command assertions.</returns>
    [Fact]
    public async Task MixedSimpleThousandIsOneDrawAndTwoStreamsAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView target = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 1000; i++)
        {
            if ((i & 1) == 0)
            {
                canvas.FillRect(new(0, 0, 8, 8), new Color2D(1, 0, 0, 1));
            }
            else
            {
                canvas.FillCircle(new(4), 3, new Color2D(0, 1, 0, 1));
            }
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = target });
        Assert.Equal(4, device.NativeBuffers);
        Assert.Equal(2, device.Copies);
        Assert.Equal(96032, device.CopiedBytes);
        Assert.Equal(1000, device.Records.Count);
        Assert.All(device.ShaderValues, snapshot => Assert.Equal("DrawRecord2D", snapshot.ShaderTypeName));
        ArgumentTableDesc table = Assert.Single(device.Tables);
        Assert.Equal(2u, table.BufferCapacity);
        Assert.Equal(0u, table.TextureCapacity);
        Assert.Equal(0u, table.SamplerCapacity);
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        RecordedDrawRange range = Assert.Single(commands.Events.OfType<RecordedDrawRange>());
        Assert.Equal(1000u, range.RecordCount);
        Assert.Equal(1000ul, range.Records.Count);
        Assert.Single(commands.Events.OfType<ValueTuple<uint, uint>>(), draw => draw == (6u, 1000u));
        Assert.Single(commands.Events.OfType<string>(), item => item == "copy");
        Assert.Single(commands.Events.OfType<string>(), item => item == "shader-copy");
    }

    /// <summary>Only one shared gradient, no image/mask buffers, and no separate NONE clip buffer are materialized.</summary>
    /// <returns>The completed sparse schema checks.</returns>
    [Fact]
    public async Task GradientUsesOnlyNecessarySharedEntriesAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView target = texture.CreateView();
        var paint = PaintGraph2D.LinearGradient(new(0), new(8, 0), [new(0, new(1, 0, 0, 1)), new(1, new(0, 0, 1, 1))]);
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 1000; i++)
        {
            canvas.FillRect(new(0, 0, 8, 8), paint);
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = target });
        Assert.Equal(8, device.NativeBuffers);
        Assert.Equal(4, device.Copies);
        Assert.Equal(2, device.ShaderValues.Count(snapshot => snapshot.ShaderTypeName == "DrawDirectoryEntry2D"));
        ShaderValueSnapshot metadata = Assert.Single(device.ShaderValues, snapshot => snapshot.ShaderTypeName == "DrawPageMetadata2D");
        Assert.Equal(3, metadata.Values.Count);
        Assert.Same(metadata.Values[0].Reference, metadata.Values[1].Reference);
        Assert.DoesNotContain(device.ShaderValues, snapshot => snapshot.ShaderTypeName is "MaskDescriptor2D" or "ImagePaintData2D");
        Assert.Single(device.Records.Select(record => record.PaintId).Distinct());
    }

    /// <summary>One immutable source/view/sampler descriptor is shared by all repeated mask commands.</summary>
    /// <returns>The completed descriptor identity checks.</returns>
    [Fact]
    public async Task RepeatedMaskNeedsOneDescriptorAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView target = texture.CreateView();
        using var image = new DispatchTexture { Format = TextureFormat.R8Unorm, Usage = TextureUsage.Sampled };
        using IGraphicsTextureView view = image.CreateView();
        using var sampler = new DispatchSampler();
        var paint = PaintGraph2D.Solid(new(1, 0, 0, 1));
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 1000; i++)
        {
            canvas.DrawCoverageMask(new(view, sampler), new(0, 0, 1, 1), new(0, 0, 8, 8), paint);
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = target });
        Assert.Equal(10, device.NativeBuffers);
        Assert.Equal(5, device.Copies);
        Assert.Single(device.ShaderValues, snapshot => snapshot.ShaderTypeName == "MaskDescriptor2D");
        Assert.DoesNotContain(device.ShaderValues, snapshot => snapshot.ShaderTypeName == "ImagePaintData2D");
        Assert.All(device.Records, record => Assert.Equal(0u, record.PayloadOffset16));
    }

    /// <summary>Equal immutable flattened geometry occupies one payload interval.</summary>
    /// <returns>The completed shared-geometry checks.</returns>
    [Fact]
    public async Task RepeatedPathNeedsOneGeometryPayloadAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView target = texture.CreateView();
        var builder = new PathBuilder2D();
        builder.MoveTo(new(0, 0));
        builder.LineTo(new(8, 0));
        builder.LineTo(new(0, 8));
        builder.Close();
        Path2D path = builder.Build();
        var paint = PaintGraph2D.Solid(new(1, 0, 0, 1));
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 1000; i++)
        {
            canvas.FillPath(path, paint);
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = target });
        Assert.Equal(8, device.NativeBuffers);
        Assert.Equal(4, device.Copies);
        Assert.Single(device.Records.Select(record => record.PayloadOffset16).Distinct());
        Assert.All(device.Records, record => Assert.Equal(3u, record.PayloadCount));
        Assert.Equal(96144, device.CopiedBytes);
    }

    /// <summary>Equal separately constructed gradients fit the real192-byte record limit without a synthetic payload split.</summary>
    /// <param name="change">Changes endpoints, transform or stops in the second draw, or leaves semantics equal.</param>
    /// <returns>The completed exact-capacity semantic sharing checks.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EqualIndependentGradientsShareAtExactPhysicalLimitAsync(int change)
    {
        var device = new ProfilingDevice { Caps = new() { ShaderTarget = ShaderTarget.SpirV, MaxBufferSize = 192 } };
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView target = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 2; i++)
        {
            if (i == 1 && change == 2)
            {
                canvas.Translate(new(1, 0));
            }

            Color2D end = i == 1 && change == 3 ? new(0, 1, 0, 1) : new(0, 0, 1, 1);
            canvas.FillRect(new(0, 0, 8, 8), PaintGraph2D.LinearGradient(new(0), new(i == 1 && change == 1 ? 9 : 8, 0), [new(0, new(1, 0, 0, 1)), new(1, end)]));
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = target });
        Assert.Equal(change == 0 ? 8 : 16, device.NativeBuffers);
        Assert.Equal(change == 0 ? 4 : 8, device.Copies);
        Assert.Equal(2, device.Records.Count);
        Assert.Equal(change == 0 ? 2 : 4, device.ShaderValues.Count(snapshot => snapshot.ShaderTypeName == "DrawDirectoryEntry2D"));
        Assert.Single(device.Records.Select(record => record.PaintId).Distinct());
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        Assert.Equal(change == 0 ? 1 : 2, commands.Events.OfType<RecordedDrawRange>().Count());
        Assert.Equal(2u, commands.Events.OfType<RecordedDrawRange>().Sum(range => range.RecordCount));
    }

    /// <summary>Idle numeric capacity remains charged even after all compiled assets are evicted.</summary>
    /// <param name="budget">The retained-memory budget.</param>
    /// <returns>The completed frame and pooled-capacity budget assertions.</returns>
    [Theory]
    [InlineData(0L)]
    [InlineData(1024L)]
    public async Task PooledCapacityCountsAfterAssetEvictionAsync(long budget)
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxCachedBytes = budget });
        using var texture = new DispatchTexture();
        using IGraphicsTextureView target = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 8, 8), new Color2D(1, 0, 0, 1));
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, canvas.End());
        using DrawingSceneReadLease2D read = scene.Borrow();
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(read, new() { View = target });
        using var commands = new ProfilingCommands();
        frame.Record(commands);
        commands.Submit();
        frame.AttachSubmission(new Completed());
        commands.Dispose();
        frame.Retire();
        Assert.Equal(0, renderer.RetentionStatistics.CachedAssets);
        Assert.Equal(budget == 0 ? 0 : 32, renderer.RetentionStatistics.CachedBytes);
        Assert.InRange(renderer.RetentionStatistics.CachedBytes, 0, budget);
    }

    /// <summary>A partially disposed idle pair is never leased after cleanup throws.</summary>
    [Fact]
    public void FailedTrimQuarantinesPartlyDisposedNumericPair()
    {
        var device = new ProfilingDevice { FailUploadDisposeOnce = true };
        using var pool = new DrawNumericPool2D(device, 1024);
        using DrawNumericPool2D.Lease first = pool.Rent(3);
        first.Dispose();
        Assert.Throws<InvalidOperationException>(() => pool.Trim(0));
        Assert.Contains(first.Data, device.DisposedBuffers);
        using DrawNumericPool2D.Lease second = pool.Rent(3);
        Assert.DoesNotContain(second.Data, device.DisposedBuffers);
        Assert.NotSame(first.Data, second.Data);
        pool.Trim(0);
    }

    private sealed class Completed : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Completed;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
