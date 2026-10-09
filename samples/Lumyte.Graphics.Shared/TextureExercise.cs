using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Exercises texture and view ownership exclusively through common resource APIs.</summary>
public static class TextureExercise
{
    /// <summary>Checks allocation attributes, mip and layer selections, rejection and lifetime.</summary>
    /// <param name="device">The already created backend device.</param>
    /// <returns>A report after all allocation and view checks succeed.</returns>
    public static string Run(IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var desc = new TextureDesc
        {
            Width = 32,
            Height = 32,
            ArrayLayers = 12,
            MipLevels = 6,
            Format = TextureFormat.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.CopyDestination | TextureUsage.RenderAttachment,
        };
        Expect<ArgumentException>(() => device.CreateTexture(desc with { Width = 0 }));
        Expect<ArgumentException>(() => device.CreateTexture(desc with { Width = uint.MaxValue }));
        Expect<ArgumentException>(() => device.CreateTexture(desc with { Height = 0 }));
        Expect<ArgumentException>(() => device.CreateTexture(desc with { ArrayLayers = 0 }));
        Expect<ArgumentException>(() => device.CreateTexture(desc with { ArrayLayers = uint.MaxValue }));
        Expect<ArgumentException>(() => device.CreateTexture(desc with { MipLevels = 7 }));
        Expect<ArgumentException>(() => device.CreateTexture(desc with { MipLevels = 0 }));
        Expect<ArgumentException>(() => device.CreateTexture(desc with { Format = (TextureFormat)99 }));
        Expect<ArgumentException>(() => device.CreateTexture(desc with { Usage = 0 }));
        Expect<ArgumentException>(() => device.CreateTexture(desc with { Usage = (TextureUsage)128 }));
        using IGraphicsTexture texture = device.CreateTexture(desc);
        Require(texture.Width == 32 && texture.Height == 32 && texture.ArrayLayers == 12 && texture.MipLevels == 6 && texture.Format == desc.Format && texture.Usage == desc.Usage, "Texture attributes changed.");
        if (device is IDisposable owner)
        {
            Expect<InvalidOperationException>(owner.Dispose);
        }

        Require(texture.GetMipSize(0) == (32U, 32U) && texture.GetMipSize(5) == (1U, 1U), "Mip dimensions are incorrect.");
        Expect<ArgumentOutOfRangeException>(() => texture.GetMipSize(6));
        Expect<ArgumentOutOfRangeException>(() => texture.GetMipSize(uint.MaxValue));

        using IGraphicsTextureView full = texture.CreateView();
        Require(ReferenceEquals(full.Texture, texture), "View retained a different texture.");
        Require(full.Info.Dimension == TextureViewDimension.D2Array && full.Info.MipLevelCount == 6 && full.Info.ArrayLayerCount == 12 && full.Info.Format == desc.Format, "Default view range is incorrect.");
        Expect<InvalidOperationException>(texture.Dispose);
        using IGraphicsTextureView tail = texture.CreateView(new() { Dimension = TextureViewDimension.D2Array, BaseMipLevel = 2, BaseArrayLayer = 9 });
        Require(tail.Info.BaseMipLevel == 2 && tail.Info.MipLevelCount == 4 && tail.Info.BaseArrayLayer == 9 && tail.Info.ArrayLayerCount == 3, "View counts were not normalized.");
        using IGraphicsTextureView single = texture.CreateView(new() { BaseMipLevel = 5, MipLevelCount = 1, BaseArrayLayer = 11 });
        Require(single.Info.Dimension == TextureViewDimension.D2 && single.Info.ArrayLayerCount == 1, "Single-layer view is incorrect.");
        using IGraphicsTextureView cube = texture.CreateView(new() { Dimension = TextureViewDimension.Cube, BaseArrayLayer = 1 });
        Require(cube.Info.BaseArrayLayer == 1 && cube.Info.ArrayLayerCount == 6, "Cube did not select six consecutive faces.");
        try
        {
            using IGraphicsTextureView cubes = texture.CreateView(new() { Dimension = TextureViewDimension.CubeArray });
            Require(cubes.Info.ArrayLayerCount == 12 && cubes.Info.Dimension == TextureViewDimension.CubeArray, "Cube array range is incorrect.");
        }
        catch (NotSupportedException)
        {
            // Some devices do not support cube array views; other failures must propagate.
        }

        Expect<ArgumentException>(() => texture.CreateView(new() { BaseMipLevel = 6 }));
        Expect<ArgumentException>(() => texture.CreateView(new() { MipLevelCount = uint.MaxValue }));
        Expect<ArgumentException>(() => texture.CreateView(new() { MipLevelCount = 0 }));
        Expect<ArgumentException>(() => texture.CreateView(new() { BaseArrayLayer = 12 }));
        Expect<ArgumentException>(() => texture.CreateView(new() { ArrayLayerCount = uint.MaxValue }));
        Expect<ArgumentException>(() => texture.CreateView(new() { ArrayLayerCount = 0 }));
        Expect<ArgumentException>(() => texture.CreateView(new() { ArrayLayerCount = 2 }));
        Expect<ArgumentException>(() => texture.CreateView(new() { Dimension = (TextureViewDimension)99 }));
        Expect<ArgumentException>(() => texture.CreateView(new() { Dimension = TextureViewDimension.Cube, BaseArrayLayer = 7 }));
        Expect<ArgumentException>(() => texture.CreateView(new() { Dimension = TextureViewDimension.CubeArray, ArrayLayerCount = 7 }));
        using IGraphicsTexture rectangle = device.CreateTexture(desc with { Height = 16, MipLevels = 1 });
        Expect<ArgumentException>(() => rectangle.CreateView(new() { Dimension = TextureViewDimension.Cube }));
        using IGraphicsTexture copyOnly = device.CreateTexture(desc with { ArrayLayers = 1, MipLevels = 1, Usage = TextureUsage.CopySource | TextureUsage.CopyDestination });
        Expect<InvalidOperationException>(() => copyOnly.CreateView());
        foreach (TextureFormat format in Enum.GetValues<TextureFormat>())
        {
            using IGraphicsTexture color = device.CreateTexture(desc with { ArrayLayers = 1, Format = format });
            using IGraphicsTextureView view = color.CreateView();
            Require(view.Info.Format == format && view.Info.Dimension == TextureViewDimension.D2, "Storage format was substituted.");
        }

        for (int i = 0; i < 20; i++)
        {
            using IGraphicsTexture color = device.CreateTexture(desc with { Width = 1, Height = 1, ArrayLayers = 1, MipLevels = 1 });
            IGraphicsTextureView view = color.CreateView();
            view.Dispose();
            view.Dispose();
            color.Dispose();
            color.Dispose();
            Expect<ObjectDisposedException>(() => color.CreateView());
        }

        single.Dispose();
        cube.Dispose();
        tail.Dispose();
        full.Dispose();
        texture.Dispose();
        texture.Dispose();
        Expect<ObjectDisposedException>(() => texture.CreateView());
        Expect<ObjectDisposedException>(() => texture.GetMipSize(0));
        return "Texture checks passed: allocation, formats, mip/layer views, cubes, ranges and lifetime.";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Expect<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
