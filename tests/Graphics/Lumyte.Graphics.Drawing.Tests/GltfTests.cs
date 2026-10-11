using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing.Gltf;
using Xunit;

namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Checks actual glTF/GLB decoding, scene transforms, materials and malformed resources.</summary>
public sealed class GltfTests
{
    /// <summary>Checks normalized colors, UVs, indices and generated normal/tangent frames.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task EmbeddedTriangleDecodesAsync()
    {
        GltfModel model = await GltfLoader.LoadAsync(JsonBytes(Triangle()));
        GltfPrimitive mesh = model.Meshes[0].Primitives[0];
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices);
        Assert.Equal(new Vector3(1, 0, 0), mesh.Vertices[1].Position);
        Assert.Equal(new Vector4(1, 0, 0, 1), mesh.Vertices[0].Color);
        Assert.Equal(new Vector2(0, 1), mesh.Vertices[2].TexCoord);
        foreach (ModelVertex vertex in mesh.Vertices)
        {
            Assert.Equal(Vector3.UnitZ, vertex.Normal);
            Assert.Equal(new Vector4(1, 0, 0, 1), vertex.Tangent);
        }

        Assert.Equal(Matrix4x4.Identity, model.Instances[0].World);
        Assert.Equal(ModelDrawing.DefaultMaterial, model.Materials[mesh.MaterialIndex].Factors);
    }

    /// <summary>Checks interleaved attributes honor byteStride and an external-resource resolver.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task InterleavedExternalBufferDecodesAsync()
    {
        JsonObject json = Triangle();
        byte[] bytes = BinaryData();
        byte[] interleaved = new byte[3 * 24];
        for (int i = 0; i < 3; i++)
        {
            bytes.AsSpan(i * 12, 12).CopyTo(interleaved.AsSpan(i * 24));
            BinaryPrimitives.WriteSingleLittleEndian(interleaved.AsSpan((i * 24) + 20), 1);
        }

        json["buffers"]![0]!["uri"] = "triangle.bin";
        json["buffers"]![0]!["byteLength"] = interleaved.Length;
        json["bufferViews"] = new JsonArray(new JsonObject { ["buffer"] = 0, ["byteLength"] = interleaved.Length, ["byteStride"] = 24 });
        json["accessors"] = new JsonArray(
            new JsonObject { ["bufferView"] = 0, ["componentType"] = 5126, ["count"] = 3, ["type"] = "VEC3" },
            new JsonObject { ["bufferView"] = 0, ["byteOffset"] = 12, ["componentType"] = 5126, ["count"] = 3, ["type"] = "VEC3" });
        json["meshes"]![0]!["primitives"]![0]!["attributes"] = new JsonObject { ["POSITION"] = 0, ["NORMAL"] = 1 };
        json["meshes"]![0]!["primitives"]![0]!.AsObject().Remove("indices");
        int calls = 0;
        GltfModel model = await GltfLoader.LoadAsync(JsonBytes(json), (uri, _) =>
        {
            Assert.Equal("triangle.bin", uri);
            calls++;
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(interleaved);
        });
        Assert.Equal(1, calls);
        Assert.Equal(new Vector3(0, 1, 0), model.Meshes[0].Primitives[0].Vertices[2].Position);
        Assert.Equal(Vector3.UnitZ, model.Meshes[0].Primitives[0].Vertices[2].Normal);
    }

    /// <summary>Checks sparse accessors overlay tightly packed values on implicit zeros.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task SparseAccessorDecodesAsync()
    {
        JsonObject json = Triangle();
        byte[] bytes = new byte[28];
        bytes[0] = 1;
        bytes[1] = 2;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(20), 1);
        json["buffers"] = new JsonArray(new JsonObject { ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(bytes), ["byteLength"] = bytes.Length });
        json["bufferViews"] = new JsonArray(
            new JsonObject { ["buffer"] = 0, ["byteOffset"] = 0, ["byteLength"] = 2 },
            new JsonObject { ["buffer"] = 0, ["byteOffset"] = 4, ["byteLength"] = 24 });
        json["accessors"] = new JsonArray(new JsonObject
        {
            ["componentType"] = 5126,
            ["count"] = 3,
            ["type"] = "VEC3",
            ["sparse"] = new JsonObject
            {
                ["count"] = 2,
                ["indices"] = new JsonObject { ["bufferView"] = 0, ["componentType"] = 5121 },
                ["values"] = new JsonObject { ["bufferView"] = 1 },
            },
        });
        json["meshes"]![0]!["primitives"]![0]!["attributes"] = new JsonObject { ["POSITION"] = 0 };
        json["meshes"]![0]!["primitives"]![0]!.AsObject().Remove("indices");
        GltfModel model = await GltfLoader.LoadAsync(JsonBytes(json));
        Assert.Equal(Vector3.Zero, model.Meshes[0].Primitives[0].Vertices[0].Position);
        Assert.Equal(Vector3.UnitX, model.Meshes[0].Primitives[0].Vertices[1].Position);
        Assert.Equal(Vector3.UnitY, model.Meshes[0].Primitives[0].Vertices[2].Position);
    }

    /// <summary>Checks a GLB with padded BIN data and rejects a truncated chunk.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task GlbContainerDecodesAsync()
    {
        JsonObject json = Triangle();
        json["buffers"]![0]!.AsObject().Remove("uri");
        byte[] glb = Glb(JsonBytes(json), BinaryData());
        GltfModel model = await GltfLoader.LoadAsync(glb);
        Assert.Equal(3, model.Meshes[0].Primitives[0].Vertices.Length);
        await Assert.ThrowsAsync<InvalidDataException>(() => GltfLoader.LoadAsync(glb.AsMemory(0, glb.Length - 1)));
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), uint.MaxValue);
        await Assert.ThrowsAsync<InvalidDataException>(() => GltfLoader.LoadAsync(glb));
    }

    /// <summary>Checks selected-scene traversal, parent TRS and explicit column-major node matrices.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task SceneTransformsUseRowVectorsAsync()
    {
        JsonObject json = Triangle();
        json["nodes"] = JsonNode.Parse("""[{"translation":[10,0,0],"scale":[2,2,2],"children":[1]},{"mesh":0,"matrix":[1,0,0,0,0,1,0,0,0,0,1,0,0,3,0,1]},{"mesh":0}]""");
        GltfModel model = await GltfLoader.LoadAsync(JsonBytes(json));
        GltfInstance instance = Assert.Single(model.Instances);
        Assert.Equal(new Vector3(12, 6, 0), Vector3.Transform(Vector3.UnitX, instance.World));
    }

    /// <summary>Checks five texture slots, glTF sampler flags, alpha and unlit materials.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task MaterialsAndImagesDecodeAsync()
    {
        JsonObject json = Triangle();
        json["images"] = JsonNode.Parse("""[{"uri":"data:image/png;base64,AQID","mimeType":"image/png"}]""");
        json["samplers"] = JsonNode.Parse("""[{"wrapS":33071,"wrapT":33648,"minFilter":9728,"magFilter":9729}]""");
        json["textures"] = JsonNode.Parse("""[{"source":0,"sampler":0}]""");
        json["materials"] = JsonNode.Parse("""[{"pbrMetallicRoughness":{"baseColorFactor":[0.2,0.3,0.4,0.5],"metallicFactor":0.2,"roughnessFactor":0.7,"baseColorTexture":{"index":0},"metallicRoughnessTexture":{"index":0}},"normalTexture":{"index":0,"scale":0.6},"occlusionTexture":{"index":0,"strength":0.8},"emissiveTexture":{"index":0},"emissiveFactor":[0.1,0.2,0.3],"alphaMode":"MASK","alphaCutoff":0.25,"doubleSided":true,"extensions":{"KHR_materials_unlit":{}}}]""");
        json["meshes"]![0]!["primitives"]![0]!["material"] = 0;
        GltfModel model = await GltfLoader.LoadAsync(JsonBytes(json));
        GltfMaterial material = model.Materials[0];
        Assert.Equal(new Vector4(0.2f, 0.3f, 0.4f, 0.5f), material.Factors.BaseColor);
        Assert.Equal(1u, material.Factors.AlphaMode);
        Assert.Equal(1u, material.Factors.Unlit);
        Assert.True(material.DoubleSided);
        Assert.Equal(0, material.NormalTexture);
        Assert.Equal(new byte[] { 1, 2, 3 }, model.Images[0].Content.ToArray());
        Assert.Equal(AddressMode.ClampToEdge, model.Textures[0].Sampler.AddressU);
        Assert.Equal(AddressMode.MirrorRepeat, model.Textures[0].Sampler.AddressV);
        Assert.Equal(0, model.Textures[0].Sampler.LodMaxClamp);
    }

    /// <summary>Checks unsupported required extensions and deformation fail explicitly.</summary>
    /// <param name="feature">The unsupported root feature.</param>
    /// <param name="value">The JSON feature data.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData("extensionsRequired", "[\"KHR_draco_mesh_compression\"]")]
    [InlineData("skins", "[{}]")]
    [InlineData("animations", "[{}]")]
    public async Task UnsupportedFeaturesAreRejectedAsync(string feature, string value)
    {
        JsonObject json = Triangle();
        json[feature] = JsonNode.Parse(value);
        await Assert.ThrowsAsync<NotSupportedException>(() => GltfLoader.LoadAsync(JsonBytes(json)));
    }

    /// <summary>Checks buffer ranges and cyclic hierarchies fail instead of producing corrupt meshes.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task MalformedRangesAndScenesAreRejectedAsync()
    {
        JsonObject json = Triangle();
        json["bufferViews"]![0]!["byteLength"] = 8;
        await Assert.ThrowsAsync<InvalidDataException>(() => GltfLoader.LoadAsync(JsonBytes(json)));
        json = Triangle();
        json["nodes"] = JsonNode.Parse("""[{"children":[1]},{"mesh":0,"children":[0]}]""");
        await Assert.ThrowsAsync<InvalidDataException>(() => GltfLoader.LoadAsync(JsonBytes(json)));
        json = Triangle();
        json["buffers"]![0]!["uri"] = "missing.bin";
        await Assert.ThrowsAsync<InvalidDataException>(() => GltfLoader.LoadAsync(JsonBytes(json)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GltfLoader.LoadAsync(JsonBytes(Triangle()), cancellationToken: new(true)));
    }

    /// <summary>Checks flat normals split shared vertices at hard edges.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task MissingNormalsProduceFlatShadingAsync()
    {
        JsonObject json = Triangle();
        byte[] data = new byte[60];
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1];
        for (int i = 0; i < positions.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 4), positions[i]);
        }

        ushort[] indices = [0, 1, 2, 0, 3, 1];
        for (int i = 0; i < indices.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(48 + (i * 2)), indices[i]);
        }

        json["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = data.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(data) });
        json["bufferViews"] = JsonNode.Parse("""[{"buffer":0,"byteLength":48},{"buffer":0,"byteOffset":48,"byteLength":12}]""");
        json["accessors"] = JsonNode.Parse("""[{"bufferView":0,"componentType":5126,"count":4,"type":"VEC3"},{"bufferView":1,"componentType":5123,"count":6,"type":"SCALAR"}]""");
        json["meshes"]![0]!["primitives"]![0]!["attributes"] = new JsonObject { ["POSITION"] = 0 };
        GltfPrimitive mesh = (await GltfLoader.LoadAsync(JsonBytes(json))).Meshes[0].Primitives[0];
        Assert.Equal(6, mesh.Vertices.Length);
        Assert.Equal(Vector3.UnitZ, mesh.Vertices[0].Normal);
        Assert.Equal(Vector3.UnitY, mesh.Vertices[3].Normal);
    }

    /// <summary>Checks local file loading resolves adjacent resources and rejects escaping paths.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task LocalFilesResolveRelativeBuffersAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "lumyte-gltf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            JsonObject json = Triangle();
            json["buffers"]![0]!["uri"] = "triangle%20data.bin";
            await File.WriteAllBytesAsync(Path.Combine(directory, "triangle data.bin"), BinaryData());
            string path = Path.Combine(directory, "triangle.gltf");
            await File.WriteAllBytesAsync(path, JsonBytes(json));
            Assert.Single((await GltfLoader.LoadFileAsync(path)).Meshes);
            json["buffers"]![0]!["uri"] = "../outside.bin";
            await File.WriteAllBytesAsync(path, JsonBytes(json));
            await Assert.ThrowsAsync<InvalidDataException>(() => GltfLoader.LoadFileAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks supplied material indices cannot refer to the synthesized fallback material.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task OutOfRangeMaterialIsRejectedAsync()
    {
        JsonObject json = Triangle();
        json["meshes"]![0]!["primitives"]![0]!["material"] = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => GltfLoader.LoadAsync(JsonBytes(json)));
    }

    private static JsonObject Triangle()
    {
        JsonObject json = JsonNode.Parse("""
            {"asset":{"version":"2.0"},"buffers":[{"byteLength":80}],
             "bufferViews":[{"buffer":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":6},{"buffer":0,"byteOffset":44,"byteLength":12},{"buffer":0,"byteOffset":56,"byteLength":24}],
             "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"},{"bufferView":2,"componentType":5121,"normalized":true,"count":3,"type":"VEC4"},{"bufferView":3,"componentType":5126,"count":3,"type":"VEC2"}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0,"COLOR_0":2,"TEXCOORD_0":3},"indices":1}]}],
             "nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
            """)!.AsObject();
        json["buffers"]![0]!["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(BinaryData());
        return json;
    }

    private static byte[] BinaryData()
    {
        byte[] bytes = new byte[80];
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        for (int i = 0; i < positions.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), positions[i]);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(38), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(40), 2);
        bytes[44] = 255;
        bytes[47] = 255;
        bytes[49] = 255;
        bytes[51] = 255;
        bytes[54] = 255;
        bytes[55] = 255;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64), 1);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(76), 1);
        return bytes;
    }

    private static byte[] JsonBytes(JsonObject json) => Encoding.UTF8.GetBytes(json.ToJsonString());

    private static byte[] Glb(byte[] json, byte[] binary)
    {
        int jsonLength = (json.Length + 3) & ~3;
        int binaryLength = (binary.Length + 3) & ~3;
        byte[] bytes = new byte[28 + jsonLength + binaryLength];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46546c67);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)jsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0x4e4f534a);
        bytes.AsSpan(20, jsonLength).Fill(32);
        json.CopyTo(bytes.AsSpan(20));
        int offset = 20 + jsonLength;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), (uint)binaryLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), 0x004e4942);
        binary.CopyTo(bytes.AsSpan(offset + 8));
        return bytes;
    }
}
