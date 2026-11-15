using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Exercises typed transfers through the public validation decorator.</summary>
public sealed class TypedDecoratorTests
{
    /// <summary>The backend receives a native adapter, never the application type.</summary>
    [Fact]
    public void FactoryAndTableRouteThroughSameNativeType()
    {
        var backend = new TypedBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = Create<TypedNumber>(device, TypedBackend.Artifact(), 8);
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 5, SamplerCapacity = 3, TextureCapacity = 2 });
        IGpuRef<TypedNumber> reference = table.WriteBuffer(2, buffer.SliceElements(1, 4));
        Assert.NotEqual(typeof(TypedNumber), backend.CreatedType);
        Assert.Equal(backend.CreatedType, backend.Table!.LastElementType);
        Assert.Same(((IShaderDataSource)buffer).ShaderHandle, backend.Table.LastResource);
        Assert.Equal(5u, table.BufferCapacity);
        Assert.Equal(3u, table.SamplerCapacity);
        Assert.Equal(2u, table.TextureCapacity);
        IShaderReference metadata = Assert.IsAssignableFrom<IShaderReference>(reference);
        Assert.Same(table, metadata.Table);
        Assert.Same(buffer, metadata.Resource);
        Assert.Equal(4UL, metadata.OffsetInBytes);
        Assert.Equal(16UL, metadata.SizeInBytes);
        Assert.Equal(ShaderBufferAccess.ReadOnly, metadata.BufferAccess);
        IShaderReference element = Assert.IsAssignableFrom<IShaderReference>(reference.GetElement(2));
        Assert.Equal(12UL, element.OffsetInBytes);
        Assert.Equal(4UL, element.SizeInBytes);
        Assert.Equal(4UL, element.RegistrationOffsetInBytes);
        Assert.Equal(16UL, element.RegistrationSizeInBytes);
        Assert.Equal(1UL, element.Count);
    }

    /// <summary>Released and overwritten registrations invalidate derived references.</summary>
    /// <param name="overwrite">Whether the slot is overwritten instead of released.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlotMutationInvalidatesParentAndElement(bool overwrite)
    {
        using var device = new ValidationDevice(new TypedBackend());
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = Create<TypedNumber>(device, TypedBackend.Artifact(), 4);
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 1 });
        IGpuRef<TypedNumber> reference = table.WriteBuffer(0, buffer.SliceElements(0, 4));
        IGpuRef<TypedNumber> element = reference.GetElement(1);
        if (overwrite)
        {
            table.WriteBuffer(0, buffer.SliceElements(0, 4));
        }
        else
        {
            table.ReleaseBuffer(0);
        }

        Assert.Throws<InvalidOperationException>(() => reference.GetElement(0));
        Assert.Throws<InvalidOperationException>(() => element.GetElement(0));
    }

    /// <summary>Backend registration failure must not invalidate an existing slot.</summary>
    [Fact]
    public void FailedRegistrationPreservesRevision()
    {
        var backend = new TypedBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = Create<TypedNumber>(device, TypedBackend.Artifact(), 2);
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 1 });
        IGpuRef<TypedNumber> reference = table.WriteBuffer(0, buffer.SliceElements(0, 2));
        backend.Table!.FailRegistration = true;
        Assert.Throws<InvalidOperationException>(() => table.WriteBuffer(0, buffer.SliceElements(0, 2)));
        Assert.Equal(1UL, reference.GetElement(0).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => reference.GetElement(2));
    }

    /// <summary>Typed storage from another domain is rejected before native registration.</summary>
    [Fact]
    public void ForeignTypedBufferNeverReachesTable()
    {
        var backend = new TypedBackend();
        using var device = new ValidationDevice(backend);
        using var foreign = new ValidationDevice(new TypedBackend());
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = Create<TypedNumber>(foreign, TypedBackend.Artifact(), 2);
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 1 });
        Assert.Throws<ArgumentException>(() => table.WriteBuffer(0, buffer.SliceElements(0, 2)));
        Assert.Null(backend.Table!.LastResource);
    }

    /// <summary>Disposed resources cannot be referenced even while their table is alive.</summary>
    [Fact]
    public void DisposedTypedResourceInvalidatesReference()
    {
        using var device = new ValidationDevice(new TypedBackend());
        IGraphicsShaderDataBuffer<TypedNumber> buffer = Create<TypedNumber>(device, TypedBackend.Artifact(), 1);
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 1 });
        IGpuRef<TypedNumber> reference = table.WriteBuffer(0, buffer.SliceElements(0, 1));
        buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reference.GetElement(0));
    }

    /// <summary>Mapping, allocation bounds and empty copies are checked before native writes.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task CopyRequiresMappedUploadAndValidBoundsAsync()
    {
        var backend = new TypedBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = Create<TypedNumber>(device, TypedBackend.Artifact(), 2, MemoryPreference.Upload);
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom([new TypedNumber(1)]));
        await buffer.MapAsync();
        Assert.True(buffer.IsMapped);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.CopyFrom([new TypedNumber(1)], 2));
        buffer.CopyFrom([], 2);
        Assert.Equal(0, backend.Writes);
        buffer.CopyFrom([new TypedNumber(1), new TypedNumber(2)]);
        Assert.Equal(1, backend.Writes);
        buffer.Unmap();
        Assert.False(buffer.IsMapped);
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom([new TypedNumber(1)]));
    }

    /// <summary>Numeric-only storage starts with valid empty dependency metadata.</summary>
    [Fact]
    public void NumericStorageHasEmptyDependencies()
    {
        using var device = new ValidationDevice(new TypedBackend());
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = Create<TypedNumber>(device, TypedBackend.Artifact(), 2);
        IShaderDataSource source = Assert.IsAssignableFrom<IShaderDataSource>(buffer);
        Assert.Equal("TypedNumber", source.Layout.Name);
        Assert.Equal(4, source.Layout.Size);
        Assert.True(source.TryGetDependencies(out IShaderReferenceMetadata? metadata));
        Assert.Empty(metadata!.References);
    }

    /// <summary>Writer completion catches a transfer shorter than its static declaration.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task IncompleteWriterInvalidatesMetadataAsync()
    {
        using var device = new ValidationDevice(new TypedBackend());
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = Create<TypedNumber>(device, TypedBackend.Artifact(), 1, MemoryPreference.Upload);
        await buffer.MapAsync();
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom([new TypedNumber(1, 3)]));
        Assert.Throws<InvalidOperationException>(() => ((IShaderDataSource)buffer).TryGetDependencies(out _));
    }

    /// <summary>A partial retry cannot repair unknown metadata, but a complete retry can.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task FailedNativeWriteRequiresCompleteRewriteAsync()
    {
        var backend = new TypedBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsShaderDataBuffer<TypedNumber> buffer = Create<TypedNumber>(device, TypedBackend.Artifact(), 2, MemoryPreference.Upload);
        await buffer.MapAsync();
        backend.FailWrite = true;
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom([new TypedNumber(1), new TypedNumber(2)]));
        backend.FailWrite = false;
        buffer.CopyFrom([new TypedNumber(3)]);
        var source = (IShaderDataSource)buffer;
        Assert.Throws<InvalidOperationException>(() => source.TryGetDependencies(out _));
        buffer.CopyFrom([new TypedNumber(4), new TypedNumber(5)]);
        Assert.True(source.TryGetDependencies(out IShaderReferenceMetadata? metadata));
        Assert.Empty(metadata!.References);
    }

    /// <summary>References are unwrapped at the writer boundary while metadata retains validation identities.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task ReferenceWriterUsesNativeIdentityAndPublishesWrapperMetadataAsync()
    {
        var backend = new TypedBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsSampler sampler = device.CreateSampler(new());
        using IArgumentTable table = device.CreateArgumentTable(new() { SamplerCapacity = 2 });
        IGpuRef<IGraphicsSampler> reference = table.WriteSampler(0, sampler);
        object nativeReference = backend.Table!.LastReference!;
        using IGraphicsShaderDataBuffer<TypedSamplerValue> buffer = Create<TypedSamplerValue>(device, TypedBackend.Artifact(true), 2, MemoryPreference.Upload);
        var source = (IShaderDataSource)buffer;
        Assert.Throws<InvalidOperationException>(() => source.TryGetDependencies(out _));
        await buffer.MapAsync();
        buffer.CopyFrom([new TypedSamplerValue(reference), new TypedSamplerValue(reference)]);
        Assert.Equal(2, backend.NativeReferences.Count);
        Assert.All(backend.NativeReferences, value => Assert.Same(nativeReference, value));
        Assert.True(source.TryGetDependencies(out IShaderReferenceMetadata? metadata));
        Assert.Same(reference, Assert.Single(metadata!.References));
        table.ReleaseSampler(0);
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom([new TypedSamplerValue(reference)], 0));
    }

    /// <summary>Null, raw and foreign shader references are rejected before native encoding.</summary>
    /// <param name="kind">The invalid reference kind.</param>
    /// <returns>The asynchronous assertions.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidReferenceDomainsNeverReachNativeWriterAsync(int kind)
    {
        var backend = new TypedBackend();
        using var device = new ValidationDevice(backend);
        using var foreign = new ValidationDevice(new TypedBackend());
        using IGraphicsSampler sampler = foreign.CreateSampler(new());
        using IArgumentTable table = foreign.CreateArgumentTable(new() { SamplerCapacity = 1 });
        IGpuRef<IGraphicsSampler>? reference = kind switch
        {
            1 => new TypedReference<IGraphicsSampler>(new object(), new object(), 0, 0, 16, 1, 16),
            2 => table.WriteSampler(0, sampler),
            _ => null,
        };
        using IGraphicsShaderDataBuffer<TypedSamplerValue> buffer = Create<TypedSamplerValue>(device, TypedBackend.Artifact(true), 1, MemoryPreference.Upload);
        await buffer.MapAsync();
        Assert.Throws<ArgumentException>(() => buffer.CopyFrom([new TypedSamplerValue(reference)]));
        Assert.Empty(backend.NativeReferences);
    }

    private static IGraphicsShaderDataBuffer<T> Create<T>(ValidationDevice device, ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderArguments => device.CreateShaderDataBuffer<T>(artifact, count, memory);
}
