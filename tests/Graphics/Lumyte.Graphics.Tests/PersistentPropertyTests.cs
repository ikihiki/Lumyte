using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises genuine generated write-through properties against retained Shared storage.</summary>
public sealed partial class PersistentPropertyTests
{
    /// <summary>Temporary, copied and cached nested views all retain their original element owner.</summary>
    [Fact]
    public void NestedPropertiesWriteThroughTemporaryCopiedAndCachedViews()
    {
        var fixture = new Fixture();
        Reference<BindingNode> first = fixture.Reference<BindingNode>(1);
        Reference<BindingNode> second = fixture.Reference<BindingNode>(2);
        ShaderDataStorage<BindingMatrixData> data = fixture.MatrixLayout.CreateStorage<BindingMatrixData>(
            fixture.Owner,
            [new(Matrix4x4.Identity, new BindingVector(Vector3.One), first), new(Matrix4x4.Identity, new BindingVector(Vector3.Zero), second)],
            fixture.Encode);
        var storage = new BindingMatrixData.Storage(data);
        BindingMatrixData.Storage.Element root = storage[0];
        BindingMatrixData.Storage.ChildAccessor child = root.Child;
        ref readonly BindingMatrixData.Storage.Element cachedRoot = ref storage[0];
        ref readonly BindingMatrixData.Storage.ChildAccessor cachedChild = ref cachedRoot.Child;
        storage[0].Child.Value = new Vector3(2, 3, 4);
        Assert.Equal(new Vector3(2, 3, 4), child.Value);
        child.Value = new Vector3(5, 6, 7);
        Assert.Equal(child.Value, cachedChild.Value);
        cachedChild.Value = new Vector3(8, 9, 10);
        var transform = Matrix4x4.CreateTranslation(3, 4, 5);
        cachedRoot.Transform = transform;
        root.Node = second;
        Assert.Equal(transform, storage[0].Transform);
        Assert.Same(second, storage[0].Node);
        Assert.Equal(Vector3.Zero, storage[1].Child.Value);
        Assert.Equal(Matrix4x4.Identity, storage[1].Transform);
        var replacement = new BindingMatrixData(Matrix4x4.Identity, new BindingVector(new Vector3(11)), first);
        storage.Set(0, replacement);
        Assert.Equal(new Vector3(11), child.Value);
        Assert.Equal(Matrix4x4.Identity, root.Transform);
        Assert.Same(first, cachedRoot.Node);
        ShaderDataStorage<BindingMatrixData> unwrapped = storage;
        Assert.Same(data, storage.Data);
        Assert.Same(data, unwrapped);
        Assert.Equal(2, storage.Count);
        Assert.Equal(4, fixture.Encodes);
    }

    /// <summary>Changed resource identities and numeric properties allocate no managed bytes after warmup.</summary>
    [Fact]
    public void WarmedPropertyAccessAndChangedReferenceSettersAllocateNothing()
    {
        var fixture = new Fixture();
        Reference<IGraphicsTextureView> first = fixture.Reference<IGraphicsTextureView>(1);
        Reference<IGraphicsTextureView> second = fixture.Reference<IGraphicsTextureView>(2);
        Reference<IGraphicsSampler> sampler = fixture.Reference<IGraphicsSampler>(3);
        ShaderDataStorage<BindingMaterial> data = fixture.MaterialLayout.CreateStorage<BindingMaterial>(
            fixture.Owner,
            [new(Vector4.One, first, sampler)],
            fixture.Encode);
        var storage = new BindingMaterial.Storage(data);
        for (int index = 0; index < 1024; index++)
        {
            storage[0].Color = new Vector4(index);
            storage[0].Texture = (index & 1) == 0 ? second : first;
            _ = storage[0].Color;
            _ = storage[0].Texture;
            _ = storage[0].Sampler;
        }

        int encodes = fixture.Encodes;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1024; index++)
        {
            storage[0].Color = new Vector4(index);
            storage[0].Texture = (index & 1) == 0 ? second : first;
            _ = storage[0].Color;
            _ = storage[0].Texture;
            _ = storage[0].Sampler;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated);
        Assert.Equal(encodes + 1024, fixture.Encodes);
        Assert.Equal(new Vector4(1023), storage[0].Color);
        Assert.Same(first, storage[0].Texture);
        Assert.Same(sampler, storage[0].Sampler);
        storage[0].Sampler = fixture.Reference<IGraphicsSampler>(4);
        Assert.Equal(encodes + 1025, fixture.Encodes);
    }

    /// <summary>Failed reference validation preserves bytes, identities and previously frozen reads.</summary>
    [Fact]
    public void FailedPropertyUpdatesAreAtomicAndOldSnapshotsRemainUnchanged()
    {
        var fixture = new Fixture();
        Reference<IGraphicsTextureView> first = fixture.Reference<IGraphicsTextureView>(1);
        Reference<IGraphicsSampler> sampler = fixture.Reference<IGraphicsSampler>(2);
        ShaderDataStorage<BindingMaterial> data = fixture.MaterialLayout.CreateStorage<BindingMaterial>(
            fixture.Owner,
            [new(Vector4.One, first, sampler)],
            fixture.Encode);
        var storage = new BindingMaterial.Storage(data);
        using var destination = new MappedBuffer(fixture.MaterialLayout.Size);
        ShaderValueSnapshot old = Assert.Single(fixture.MaterialLayout.Upload(data, fixture.Owner, destination, 0, 1, 0));
        byte[] bytes = destination.Bytes.ToArray();
        Assert.Throws<ArgumentNullException>(() => storage[0].Texture = null!);
        var foreign = new Reference<IGraphicsTextureView>(new object(), 9);
        Assert.Throws<ArgumentException>(() => storage[0].Texture = foreign);
        fixture.ShortEncoding = true;
        Assert.Throws<ArgumentException>(() => storage[0].Texture = fixture.Reference<IGraphicsTextureView>(3));
        fixture.ShortEncoding = false;
        var foreignSampler = new Reference<IGraphicsSampler>(new object(), 10);
        var invalidComplete = new BindingMaterial(new Vector4(99), first, foreignSampler);
        Assert.Throws<ArgumentException>(() => storage.Set(0, invalidComplete));
        Assert.Throws<ArgumentException>(() => data.GetValue(0, new ShaderDataValueMember<BindingMaterial, float>("Color")));
        Assert.Throws<ArgumentException>(() => data.GetReference(0, new ShaderDataReferenceMember<BindingMaterial, IGraphicsTextureView>("Sampler")));
        fixture.MaterialLayout.Upload(data, fixture.Owner, destination, 0, 1, 0);
        Assert.Equal(bytes, destination.Bytes);
        Assert.Same(first, storage[0].Texture);
        Reference<IGraphicsTextureView> replacement = fixture.Reference<IGraphicsTextureView>(4);
        storage[0].Texture = replacement;
        storage[0].Color = new Vector4(7);
        ShaderValueSnapshot updated = Assert.Single(fixture.MaterialLayout.Upload(data, fixture.Owner, destination, 0, 1, 0));
        Assert.Same(first, Assert.Single(old.Values, value => value.Path == "Texture").Reference);
        Assert.Equal(Vector4.One, MemoryMarshal.Read<Vector4>(Assert.Single(old.Values, value => value.Path == "Color").Data.Span));
        Assert.Same(replacement, Assert.Single(updated.Values, value => value.Path == "Texture").Reference);
        Assert.Equal(new Vector4(7), storage[0].Color);
        Assert.Same(sampler, storage[0].Sampler);
    }

    /// <summary>Default views and invalid indices fail rather than silently modifying element zero.</summary>
    [Fact]
    public void DefaultViewsNullAdaptersAndInvalidIndicesAreRejected()
    {
        BindingMatrixData.Storage.Element invalid = default;
        BindingMatrixData.Storage.ChildAccessor child = default;
        Assert.Throws<InvalidOperationException>(() => { _ = invalid.Child; });
        Assert.Throws<InvalidOperationException>(() => invalid.Transform);
        Assert.Throws<InvalidOperationException>(() => invalid.Transform = Matrix4x4.Identity);
        Assert.Throws<InvalidOperationException>(() => invalid.Node);
        Assert.Throws<InvalidOperationException>(() => invalid.Node = null!);
        Assert.Throws<InvalidOperationException>(() => child.Value);
        Assert.Throws<InvalidOperationException>(() => child.Value = Vector3.One);
        Assert.Throws<ArgumentNullException>(() => new BindingMatrixData.Storage(null!));
        Assert.Throws<ArgumentNullException>(() => BindingMatrixData.CreateStorage(null!, []));
        var fixture = new Fixture();
        var storage = new BindingMatrixData.Storage(fixture.MatrixLayout.CreateStorage<BindingMatrixData>(fixture.Owner, [], fixture.Encode));
        Assert.Equal(0, storage.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => storage[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => storage[0]);
    }
}
