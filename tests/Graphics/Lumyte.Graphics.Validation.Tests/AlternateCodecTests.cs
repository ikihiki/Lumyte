using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Checks alternate codecs through the public validation decorator.</summary>
public sealed class AlternateCodecTests
{
    /// <summary>Alternate wrappers preserve mapping, bounds, completion and empty-copy rules.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task AlternateCopiesRetainTransferGuardsAsync()
    {
        var backend = new AlternateCodecBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = device.CreateShaderDataBuffer<TypedNumber>(AlternateCodecBackend.Artifact(), 2, MemoryPreference.Upload);
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom<Number>([new(7)]));
        await buffer.MapAsync();
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.CopyFrom<Number>([new(7)], 2));
        Assert.Throws<ArgumentException>(() => buffer.CopyFrom<TypedSamplerValue>([new(null)], 0));
        Assert.Equal(0, backend.Writes);
        buffer.CopyFrom<Number>([], 2);
        Assert.Equal(0, backend.Writes);
        GraphicsValidation tracker = Assert.IsType<GraphicsValidation>(typeof(ValidationDevice).GetProperty("Tracker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(device));
        using (GraphicsValidation.HostWriteLease lease = tracker.BeginHostWrite(buffer, 4, 4))
        {
            Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom<Number>([new(7)], 1));
        }

        Assert.Equal(0, backend.Writes);
        buffer.CopyFrom<Number>([new(7)], 1);
        Assert.Equal(1, backend.Writes);
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom<Number>([new(7, 3)]));
        IShaderDataSource metadata = Assert.IsAssignableFrom<IShaderDataSource>(buffer);
        Assert.Throws<InvalidOperationException>(() => metadata.TryGetDependencies(out _));
        buffer.CopyFrom<Number>([new(1), new(2)]);
        Assert.True(metadata.TryGetDependencies(out IShaderReferenceMetadata? dependencies));
        Assert.Empty(dependencies!.References);
        buffer.Unmap();
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom<Number>([]));
    }

    /// <summary>Alternate adapters unwrap references while capturing the validation closure.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task AlternateReferencesKeepDependencyClosureAndGenerationAsync()
    {
        var backend = new AlternateCodecBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsSampler sampler = device.CreateSampler(new());
        using IArgumentTable table = device.CreateArgumentTable(new() { SamplerCapacity = 1 });
        IGpuRef<IGraphicsSampler> reference = table.WriteSampler(0, sampler);
        using IGraphicsShaderDataBuffer<TypedSamplerValue> buffer = device.CreateShaderDataBuffer<TypedSamplerValue>(AlternateCodecBackend.Artifact(true), 1, MemoryPreference.Upload);
        await buffer.MapAsync();
        buffer.CopyFrom<Sampler>([new(reference)]);
        Assert.Single(backend.NativeReferences);
        Assert.NotSame(reference, backend.NativeReferences[0]);
        IShaderDataSource metadata = Assert.IsAssignableFrom<IShaderDataSource>(buffer);
        Assert.True(metadata.TryGetDependencies(out IShaderReferenceMetadata? dependencies));
        Assert.Same(reference, Assert.Single(dependencies!.References));
        table.ReleaseSampler(0);
        Assert.Same(reference, Assert.Single(dependencies.References));
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom<Sampler>([new(reference)]));
    }

    /// <summary>Legacy implementations must fail explicitly, never silently discard alternate values.</summary>
    [Fact]
    public void LegacyImplementationRejectsAlternateCodecExplicitly()
    {
        var backend = new TypedBackend();
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = backend.CreateShaderDataBuffer<TypedNumber>(TypedBackend.Artifact(), 1, MemoryPreference.Upload);
        Assert.Throws<NotSupportedException>(() => buffer.CopyFrom<Number>([new(1)]));
        Assert.Equal(0, backend.Writes);
    }

    private readonly struct Number(uint value, int bytes = 4) : IShaderArguments
    {
        public static string ShaderTypeName => TypedNumber.ShaderTypeName;

        public static int TransferSize => TypedNumber.TransferSize;

        public static int ReferenceCount => TypedNumber.ReferenceCount;

        public static IReadOnlyList<ShaderTransferField> TransferFields => throw new InvalidOperationException("Alternate diagnostic metadata is not required.");

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => writer.WriteValue(BitConverter.GetBytes(value).AsSpan(0, bytes));
    }

    private readonly struct Sampler(IGpuRef<IGraphicsSampler> reference) : IShaderArguments
    {
        public static string ShaderTypeName => TypedSamplerValue.ShaderTypeName;

        public static int TransferSize => TypedSamplerValue.TransferSize;

        public static int ReferenceCount => TypedSamplerValue.ReferenceCount;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => writer.WriteReference(reference);
    }
}
