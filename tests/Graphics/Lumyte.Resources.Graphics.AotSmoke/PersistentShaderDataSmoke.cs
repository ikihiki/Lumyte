using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Shared;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal static class PersistentShaderDataSmoke
{
    internal static void Run()
    {
        var artifact = ShaderArtifact.LoadEmbedded(typeof(BindingMaterial).Assembly, "Lumyte.Shaders.binding-fragment.lshader");
        var layout = ShaderDataLayout.Data(artifact, ShaderTarget.SpirV, "BindingMaterial");
        object owner = new();
        var texture = new Reference<IGraphicsTextureView>();
        var sampler = new Reference<IGraphicsSampler>();
        ShaderDataStorage<BindingMaterial> storage = layout.CreateStorage<BindingMaterial>(owner, [new(Vector4.One, texture, sampler)], static (_, _) => new byte[16]);
        storage.SetValue(0, BindingMaterial.StorageMembers.Color, new Vector4(2, 3, 4, 5));
        storage.SetReference(0, BindingMaterial.StorageMembers.Sampler, sampler);
        using var target = new MappedBytes(layout.Size);
        ShaderValueSnapshot old = layout.Upload(storage, owner, target, 0, 1, 0)[0];
        storage.SetValue(0, BindingMaterial.StorageMembers.Color, Vector4.Zero);
        layout.Upload(storage, owner, target, 0, 1, 0);
        if (MemoryMarshal.Read<Vector4>(old.Values[0].Data.Span) != new Vector4(2, 3, 4, 5) || !ReferenceEquals(old.Values[1].Reference, texture) || !ReferenceEquals(old.Values[2].Reference, sampler))
        {
            throw new InvalidOperationException("NativeAOT persistent shader metadata or numeric snapshot differs.");
        }

        var vectors = ShaderArtifact.LoadEmbedded(typeof(BindingNestedVector).Assembly, "Lumyte.Shaders.binding-layout.lshader");
        var nestedLayout = ShaderDataLayout.Data(vectors, ShaderTarget.Wgsl, "BindingNestedVector");
        ShaderDataStorage<BindingNestedVector> nested = nestedLayout.CreateStorage<BindingNestedVector>(owner, [new(default, 7)], static (_, _) => throw new InvalidOperationException());
        nested.SetValue(0, BindingNestedVector.StorageMembers.Child.Value, new Vector3(11, 12, 13));
        using var nestedTarget = new MappedBytes(nestedLayout.Size);
        ShaderValueSnapshot snapshot = nestedLayout.Upload(nested, owner, nestedTarget, 0, 1, 0)[0];
        if (MemoryMarshal.Read<Vector3>(snapshot.Values[0].Data.Span) != new Vector3(11, 12, 13))
        {
            throw new InvalidOperationException("NativeAOT nested generated member update differs.");
        }

        var matrixArtifact = ShaderArtifact.LoadEmbedded(typeof(BindingMatrixData).Assembly, "Lumyte.Shaders.binding-persistent.lshader");
        var matrixLayout = ShaderDataLayout.Data(matrixArtifact, ShaderTarget.SpirV, "BindingMatrixData");
        var node = new Reference<BindingNode>();
        ShaderDataStorage<BindingMatrixData> matrix = matrixLayout.CreateStorage<BindingMatrixData>(owner, [new(default, default, node)], static (_, _) => new byte[16]);
        Matrix4x4 transform = new(2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53);
        matrix.SetValue(0, BindingMatrixData.StorageMembers.Transform, transform);
        matrix.SetReference(0, BindingMatrixData.StorageMembers.Node, node);
        using var matrixTarget = new MappedBytes(matrixLayout.Size);
        ShaderValueSnapshot matrixSnapshot = matrixLayout.Upload(matrix, owner, matrixTarget, 0, 1, 0)[0];
        if (MemoryMarshal.Read<Matrix4x4>(matrixSnapshot.Values[0].Data.Span) != transform || !ReferenceEquals(matrixSnapshot.Values[2].Reference, node))
        {
            throw new InvalidOperationException("NativeAOT matrix and typed buffer reference storage differs.");
        }

        Console.WriteLine("NativeAOT persistent generated matrix/nested/reference storage and frozen snapshots passed.");
    }

    private sealed class Reference<T> : IGpuRef<T>
    {
        public ulong Count => 1;

        public IGpuRef<T> GetElement(ulong index) => index == 0 ? this : throw new ArgumentOutOfRangeException(nameof(index));
    }

    private sealed class MappedBytes(int count) : IGraphicsBuffer<byte>
    {
        private readonly byte[] _bytes = new byte[count];

        public ulong SizeInBytes => (ulong)_bytes.Length;

        public BufferLayout<byte> Layout => new(1, 1, 1, 1);

        public ulong Count => SizeInBytes;

        public BufferUsage Usage => BufferUsage.CopySource;

        public MemoryPreference Memory => MemoryPreference.Upload;

        public bool IsMapped => true;

        public void ValidateByteRange(ulong offset, ulong length)
        {
            if (length == 0 || offset > Count || length > Count - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
        }

        public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
        {
            ValidateByteRange(offset, length);
            source.CopyTo(_bytes.AsSpan(checked((int)offset), checked((int)length)));
        }

        public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => _bytes.AsSpan(checked((int)offset), checked((int)length)).CopyTo(destination);

        public ValueTask MapAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Unmap()
        {
        }

        public BufferSlice<byte> SliceElements(ulong offset, ulong count) => new(this, offset, count);

        public void CopyFrom(ReadOnlySpan<byte> source) => CopyBytesFrom(source, 0, (ulong)source.Length);

        public void CopyTo(Span<byte> destination) => _bytes.AsSpan().CopyTo(destination);

        public void Dispose()
        {
        }
    }
}
