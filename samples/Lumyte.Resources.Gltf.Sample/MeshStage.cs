using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Lumyte.Memory;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class MeshStage : IResourceTransform<GltfDocument, CpuMesh>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    public ResourceAddress GetInput(ResourceAddress output) => new(output.Id);

    public async ValueTask<CpuMesh> BuildAsync(GltfDocument input, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        JsonElement primitives = input.Root.GetProperty("meshes")[GltfDocument.Select(context.Address, "mesh")].GetProperty("primitives");
        if (primitives.GetArrayLength() != 1)
        {
            throw new NotSupportedException("The sample supports one primitive per mesh.");
        }

        JsonElement primitive = primitives[0];
        if ((primitive.TryGetProperty("mode", out JsonElement mode) && mode.GetInt32() != 4) || primitive.TryGetProperty("targets", out _))
        {
            throw new NotSupportedException("Only static TRIANGLES are supported.");
        }

        JsonElement attributes = primitive.GetProperty("attributes");
        Accessor positions = await ReadAccessorAsync(input, attributes.GetProperty("POSITION").GetInt32(), context, cancellationToken);
        Accessor normals = await ReadAccessorAsync(input, attributes.GetProperty("NORMAL").GetInt32(), context, cancellationToken);
        Accessor coords = await ReadAccessorAsync(input, attributes.GetProperty("TEXCOORD_0").GetInt32(), context, cancellationToken);
        Accessor indices = await ReadAccessorAsync(input, primitive.GetProperty("indices").GetInt32(), context, cancellationToken);
        if (positions.Type != "VEC3" || normals.Type != "VEC3" || coords.Type != "VEC2" ||
            positions.Component != 5126 || normals.Component != 5126 || coords.Component != 5126 ||
            indices.Type != "SCALAR" || indices.Component is not (5121 or 5123 or 5125) || indices.Count % 3 != 0 ||
            positions.Count != normals.Count || positions.Count != coords.Count)
        {
            throw new NotSupportedException("Expected float positions/normals/UVs and unsigned triangle indices.");
        }

        var materialKey = new ResourceKey<CpuMaterial>(context.Address.Id, $"material/{primitive.GetProperty("material").GetInt32()}");
        CpuMaterial material = await context.RequireAsync(materialKey, ResourceDependencyLifetime.Build, cancellationToken);
        IMemoryBuffer<GltfVertex> vertices = await context.GetMemoryScope(ResourceDependencyLifetime.Resident).RentAsync<GltfVertex>(new("gltf.cpu"), indices.Count, MemoryPurpose.Resident, cancellationToken);
        for (int i = 0; i < indices.Count; i++)
        {
            uint index = indices.Index(i);
            if (index >= positions.Count)
            {
                throw new InvalidDataException("A triangle index is outside the vertex range.");
            }

            int vertex = checked((int)index);
            vertices.Memory.Span[i] = new(positions.Vector3(vertex), normals.Vector3(vertex), coords.Vector2(vertex));
        }

        // Copy the material values: the build-only dependency is not retained as an owned result.
        return new(vertices, new(material.Color, material.Texture));
    }

    public ValueTask ReleaseAsync(CpuMesh resource) => ValueTask.CompletedTask;

    private static async ValueTask<Accessor> ReadAccessorAsync(GltfDocument document, int index, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        JsonElement accessor = document.Root.GetProperty("accessors")[index];
        if (accessor.TryGetProperty("sparse", out _) || (accessor.TryGetProperty("normalized", out JsonElement normalized) && normalized.GetBoolean()))
        {
            throw new NotSupportedException("Sparse and normalized accessors are outside this sample's subset.");
        }

        JsonElement view = document.Root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        JsonElement buffer = document.Root.GetProperty("buffers")[view.GetProperty("buffer").GetInt32()];
        SourceBytes bytes = await context.RequireAsync(new ResourceKey<SourceBytes>(document.Resolve(buffer.GetProperty("uri").GetString()!)), ResourceDependencyLifetime.Build, cancellationToken);
        int bufferLength = buffer.GetProperty("byteLength").GetInt32();
        int viewOffset = OptionalInt(view, "byteOffset", 0);
        int viewLength = view.GetProperty("byteLength").GetInt32();
        int offset = OptionalInt(accessor, "byteOffset", 0);
        int count = accessor.GetProperty("count").GetInt32();
        int component = accessor.GetProperty("componentType").GetInt32();
        string type = accessor.GetProperty("type").GetString()!;
        int size = component switch { 5121 => 1, 5123 => 2, 5125 or 5126 => 4, _ => throw new NotSupportedException("Unsupported component type.") };
        int width = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, _ => throw new NotSupportedException("Unsupported accessor shape.") };
        int elementSize = size * width;
        int stride = OptionalInt(view, "byteStride", elementSize);
        if (count <= 0 || offset < 0 || viewOffset < 0 || viewLength < 0 || bufferLength < 0 || bufferLength > bytes.Buffer.Memory.Length ||
            (long)viewOffset + viewLength > bufferLength || stride < elementSize || stride % size != 0 || offset % size != 0 ||
            (long)offset + ((long)(count - 1) * stride) + elementSize > viewLength)
        {
            throw new InvalidDataException("An accessor is outside its external buffer view.");
        }

        return new(bytes.Buffer.Memory.Slice(viewOffset + offset, viewLength - offset), count, stride, component, type);
    }

    private static int OptionalInt(JsonElement element, string property, int fallback) => element.TryGetProperty(property, out JsonElement value) ? value.GetInt32() : fallback;

    private sealed record Accessor(ReadOnlyMemory<byte> Bytes, int Count, int Stride, int Component, string Type)
    {
        internal uint Index(int index) => Component switch
        {
            5121 => Bytes.Span[index * Stride],
            5123 => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.Span[(index * Stride)..]),
            _ => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.Span[(index * Stride)..]),
        };

        internal Vector3 Vector3(int index) => new(Float(index, 0), Float(index, 1), Float(index, 2));

        internal Vector2 Vector2(int index) => new(Float(index, 0), Float(index, 1));

        private float Float(int index, int component) => BinaryPrimitives.ReadSingleLittleEndian(Bytes.Span[((index * Stride) + (component * 4))..]);
    }
}
