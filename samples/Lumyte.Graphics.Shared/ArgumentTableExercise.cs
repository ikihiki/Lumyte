using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Exercises logical registration, opaque element references and caller-owned resources.</summary>
public static class ArgumentTableExercise
{
    /// <summary>Checks slots, usages, references, replacement and typed ranges through common APIs.</summary>
    /// <param name="device">The already created backend device.</param>
    /// <returns>A report after all registration checks succeed.</returns>
    public static string Run(IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        Expect<ArgumentNullException>(() => device.CreateArgumentTable(null!));
        Expect<ArgumentException>(() => device.CreateArgumentTable(new()));
        using IArgumentTable table = device.CreateArgumentTable(new() { TextureCapacity = 2, SamplerCapacity = 2, BufferCapacity = 2 });
        Require(table.TextureCapacity == 2 && table.SamplerCapacity == 2 && table.BufferCapacity == 2, "Logical capacities changed.");
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new());
        using IGraphicsBuffer<uint> buffer = device.CreateBuffer(new BufferDesc<uint> { Count = 16, Usage = BufferUsage.ShaderRead });
        IGpuRef<IGraphicsTextureView> image = table.WriteTexture(0, view);
        IGpuRef<IGraphicsSampler> sampling = table.WriteSampler(0, sampler);
        IGpuRef<uint> range = table.WriteBuffer(0, buffer.Slice(3, 5));
        IGpuRef<uint> element = range.GetElement(4);
        Require(image.Count == 1 && sampling.Count == 1 && range.Count == 5 && element.Count == 1, "Logical element counts are incorrect.");
        Require(element.GetElement(0).Count == 1, "An element reference changed its range.");
        Expect<ArgumentOutOfRangeException>(() => range.GetElement(5));
        Expect<ArgumentOutOfRangeException>(() => range.GetElement(ulong.MaxValue));
        Expect<ArgumentOutOfRangeException>(() => element.GetElement(1));
        Expect<ArgumentOutOfRangeException>(() => image.GetElement(1));
        Expect<ArgumentOutOfRangeException>(() => table.WriteTexture(2, view));
        Expect<ArgumentOutOfRangeException>(() => table.WriteSampler(uint.MaxValue, sampler));
        Expect<ArgumentOutOfRangeException>(() => table.WriteBuffer(2, buffer.Slice(0, 1)));
        Expect<ArgumentOutOfRangeException>(() => table.ReleaseTexture(2));
        Expect<ArgumentOutOfRangeException>(() => table.ReleaseSampler(2));
        Expect<ArgumentOutOfRangeException>(() => table.ReleaseBuffer(2));
        table.ReleaseTexture(1);
        table.ReleaseTexture(1);
        Expect<ArgumentException>(() => table.WriteBuffer<uint>(1, default));
        using IGraphicsBuffer<uint> staging = device.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        Expect<ArgumentException>(() => table.WriteBuffer(1, staging.Slice(0, 1)));
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView attachment = target.CreateView();
        Expect<ArgumentException>(() => table.WriteTexture(1, attachment));
        Expect<ArgumentException>(() => table.WriteSampler(1, new UnregisteredSampler()));
        Expect<ArgumentNullException>(() => table.WriteTexture(1, null!));
        Expect<ArgumentNullException>(() => table.WriteSampler(1, null!));

        using IArgumentTable other = device.CreateArgumentTable(new() { TextureCapacity = 1 });
        IGpuRef<IGraphicsTextureView> independent = other.WriteTexture(0, view);
        IGpuRef<IGraphicsTextureView> replacement = table.WriteTexture(0, view);
        Require(independent.GetElement(0).Count == 1 && replacement.GetElement(0).Count == 1, "Tables or resource slot namespaces were confused.");
        table.ReleaseTexture(0);
        other.Dispose();
        other.Dispose();
        view.Dispose();

        using IGraphicsBuffer<ushort> words = device.CreateBuffer(new BufferDesc<ushort> { Count = 8, Usage = BufferUsage.ShaderRead });
        IGpuRef<ushort> differentlyTyped = table.WriteBuffer(0, words.Slice(1, 3));
        Require(differentlyTyped.GetElement(2).Count == 1 && sampling.GetElement(0).Count == 1, "Replacement invalidated a different resource kind.");
        buffer.Dispose();
        table.ReleaseBuffer(0);
        IGpuRef<ushort> fresh = table.WriteBuffer(0, words.Slice(2, 2));
        Require(fresh.Count == 2, "New registration has an incorrect range.");
        table.Dispose();
        table.Dispose();
        Expect<ObjectDisposedException>(() => table.ReleaseSampler(0));
        Expect<ObjectDisposedException>(() => table.WriteSampler(0, sampler));
        words.Dispose();
        sampler.Dispose();
        Require(range.Count == 5, "Replacement changed immutable range metadata.");
        return "Argument table checks passed: independent slots, typed element references, replacement and resource-independent slot namespaces.";
    }

    /// <summary>Checks that resources created by another device cannot enter the target table.</summary>
    /// <param name="device">The device owning the registration table.</param>
    /// <param name="foreign">A different backend device.</param>
    public static void CheckForeignDevice(IGraphicDevice device, IGraphicDevice foreign)
    {
        using IArgumentTable table = device.CreateArgumentTable(new() { TextureCapacity = 1, SamplerCapacity = 1, BufferCapacity = 1 });
        using IGraphicsTexture texture = foreign.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsSampler sampler = foreign.CreateSampler(new());
        using IGraphicsBuffer<uint> buffer = foreign.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.ShaderRead });
        Expect<ArgumentException>(() => table.WriteTexture(0, view));
        Expect<ArgumentException>(() => table.WriteSampler(0, sampler));
        Expect<ArgumentException>(() => table.WriteBuffer(0, buffer.Slice(0, 1)));
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

    private sealed class UnregisteredSampler : IGraphicsSampler
    {
        public SamplerDesc Desc { get; } = new();

        public void Dispose()
        {
        }
    }
}
