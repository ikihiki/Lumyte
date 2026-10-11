using System.Numerics;
using System.Text.Json;

namespace Lumyte.Graphics.Drawing.Gltf;

internal sealed partial class GltfReader
{
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Vector3 Vector3Value(AccessorValues data, int index)
    {
        int offset = index * data.Components;
        return new((float)data.Values[offset], (float)data.Values[offset + 1], (float)data.Values[offset + 2]);
    }

    private static Vector4 Vector4Value(AccessorValues data, int index, float defaultW)
    {
        Vector3 xyz = Vector3Value(data, index);
        return new(xyz, data.Components == 4 ? (float)data.Values[(index * data.Components) + 3] : defaultW);
    }

    private static (ModelVertex[] Vertices, uint[] Indices) FlatNormals(ModelVertex[] source, uint[] indices)
    {
        var vertices = new ModelVertex[indices.Length];
        uint[] result = new uint[indices.Length];
        for (int i = 0; i < indices.Length; i += 3)
        {
            ModelVertex a = source[indices[i]];
            ModelVertex b = source[indices[i + 1]];
            ModelVertex c = source[indices[i + 2]];
            var cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            Vector3 normal = cross.LengthSquared() > 1e-12f ? Vector3.Normalize(cross) : Vector3.UnitZ;
            vertices[i] = a with { Normal = normal };
            vertices[i + 1] = b with { Normal = normal };
            vertices[i + 2] = c with { Normal = normal };
            result[i] = (uint)i;
            result[i + 1] = (uint)(i + 1);
            result[i + 2] = (uint)(i + 2);
        }

        return (vertices, result);
    }

    private static void GenerateTangents(ModelVertex[] vertices, uint[] indices)
    {
        var tangents = new Vector3[vertices.Length];
        var bitangents = new Vector3[vertices.Length];
        for (int i = 0; i < indices.Length; i += 3)
        {
            uint a = indices[i];
            uint b = indices[i + 1];
            uint c = indices[i + 2];
            Vector3 edge1 = vertices[b].Position - vertices[a].Position;
            Vector3 edge2 = vertices[c].Position - vertices[a].Position;
            Vector2 uv1 = vertices[b].TexCoord - vertices[a].TexCoord;
            Vector2 uv2 = vertices[c].TexCoord - vertices[a].TexCoord;
            float determinant = (uv1.X * uv2.Y) - (uv1.Y * uv2.X);
            if (MathF.Abs(determinant) > 1e-12f)
            {
                Vector3 tangent = ((edge1 * uv2.Y) - (edge2 * uv1.Y)) / determinant;
                Vector3 bitangent = ((edge2 * uv1.X) - (edge1 * uv2.X)) / determinant;
                tangents[a] += tangent;
                tangents[b] += tangent;
                tangents[c] += tangent;
                bitangents[a] += bitangent;
                bitangents[b] += bitangent;
                bitangents[c] += bitangent;
            }
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 normal = vertices[i].Normal;
            Vector3 tangent = tangents[i] - (normal * Vector3.Dot(normal, tangents[i]));
            if (tangent.LengthSquared() <= 1e-12f)
            {
                tangent = Vector3.Cross(MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY, normal);
            }

            tangent = Vector3.Normalize(tangent);
            float handedness = Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[i]) < 0 ? -1 : 1;
            vertices[i] = vertices[i] with { Tangent = new(tangent, handedness) };
        }
    }

    private GltfMesh[] ReadMeshes(CancellationToken token)
    {
        JsonElement[] entries = Array(_root, "meshes");
        var meshes = new GltfMesh[entries.Length];
        for (int mesh = 0; mesh < meshes.Length; mesh++)
        {
            token.ThrowIfCancellationRequested();
            if (entries[mesh].TryGetProperty("weights", out _))
            {
                throw new NotSupportedException("Morph weights are not supported by the static model renderer.");
            }

            JsonElement[] primitives = Array(entries[mesh], "primitives");
            if (primitives.Length == 0)
            {
                throw new InvalidDataException("A mesh must contain primitives.");
            }

            var result = new GltfPrimitive[primitives.Length];
            for (int i = 0; i < primitives.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                result[i] = ReadPrimitive(primitives[i]);
            }

            meshes[mesh] = new(String(entries[mesh], "name"), result);
        }

        return meshes;
    }

    private GltfPrimitive ReadPrimitive(JsonElement primitive)
    {
        if (Int(primitive, "mode", 4) != 4 || Array(primitive, "targets").Length != 0)
        {
            throw new NotSupportedException("The static model renderer requires triangle lists without morph targets.");
        }

        JsonElement attributes = primitive.GetProperty("attributes");
        if (attributes.TryGetProperty("JOINTS_0", out _) || attributes.TryGetProperty("WEIGHTS_0", out _))
        {
            throw new NotSupportedException("Skinned mesh attributes are not supported.");
        }

        AccessorValues positions = Attribute(attributes, "POSITION", 3, 0, true)!;
        AccessorValues? normals = Attribute(attributes, "NORMAL", 3, positions.Count, true);
        AccessorValues? tangents = Attribute(attributes, "TANGENT", 4, positions.Count, true);
        AccessorValues? uv = Attribute(attributes, "TEXCOORD_0", 2, positions.Count, false);
        AccessorValues? colors = Attribute(attributes, "COLOR_0", 0, positions.Count, false);
        var vertices = new ModelVertex[positions.Count];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 normal = normals == null ? Vector3.Zero : Vector3Value(normals, i);
            if (normals != null && normal.LengthSquared() <= 1e-12f)
            {
                throw new InvalidDataException("A vertex normal must be nonzero.");
            }

            Vector4 tangent = tangents == null ? Vector4.Zero : Vector4Value(tangents, i, 1);
            if (tangents != null && (MathF.Abs(tangent.W) != 1 || new Vector3(tangent.X, tangent.Y, tangent.Z).LengthSquared() <= 1e-12f))
            {
                throw new InvalidDataException("Invalid vertex tangent or handedness.");
            }

            vertices[i] = new(Vector3Value(positions, i), normals == null ? normal : Vector3.Normalize(normal), tangent, uv == null ? Vector2.Zero : new((float)uv.Values[i * 2], (float)uv.Values[(i * 2) + 1]), colors == null ? Vector4.One : Vector4Value(colors, i, 1));
        }

        uint[] indices;
        if (primitive.TryGetProperty("indices", out JsonElement indexId))
        {
            AccessorValues data = ReadAccessor(indexId.GetInt32());
            if (data.Components != 1 || data.Normalized || data.ComponentType is not (5121 or 5123 or 5125))
            {
                throw new InvalidDataException("Mesh indices must be unsigned scalar integers.");
            }

            indices = data.Values.Select(x => checked((uint)x)).ToArray();
        }
        else
        {
            indices = Enumerable.Range(0, vertices.Length).Select(x => (uint)x).ToArray();
        }

        if (indices.Length % 3 != 0 || indices.Any(x => x >= vertices.Length))
        {
            throw new InvalidDataException("Triangle indices must be in range and divisible by three.");
        }

        // glTF requires flat normals when NORMAL is missing: split shared triangle vertices.
        if (normals == null)
        {
            (vertices, indices) = FlatNormals(vertices, indices);
        }

        if (tangents == null || normals == null)
        {
            GenerateTangents(vertices, indices);
        }

        if (vertices.Any(vertex => !Finite(vertex.Normal) || !Finite(new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z))))
        {
            throw new InvalidDataException("Model normal/tangent computation produced nonfinite values.");
        }

        int material = Int(primitive, "material", _materials.Length - 1);
        CheckIndex(material, primitive.TryGetProperty("material", out _) ? _materials.Length - 1 : _materials.Length, "material");
        GltfMaterial surface = _materials[material];
        if (uv == null && (surface.BaseColorTexture != null || surface.MetallicRoughnessTexture != null || surface.NormalTexture != null || surface.OcclusionTexture != null || surface.EmissiveTexture != null))
        {
            throw new InvalidDataException("A textured primitive requires TEXCOORD_0.");
        }

        return new(vertices, indices, material);
    }

    private AccessorValues? Attribute(JsonElement attributes, string name, int components, int count, bool floating)
    {
        if (!attributes.TryGetProperty(name, out JsonElement id))
        {
            if (name == "POSITION")
            {
                throw new InvalidDataException("A primitive requires POSITION.");
            }

            return null;
        }

        AccessorValues values = ReadAccessor(id.GetInt32());
        if ((components != 0 && values.Components != components) || (components == 0 && values.Components is not (3 or 4)) ||
            (count != 0 && values.Count != count) ||
            (floating && (values.ComponentType != 5126 || values.Normalized)) ||
            (!floating && values.ComponentType != 5126 && !(values.ComponentType is 5121 or 5123 && values.Normalized)))
        {
            throw new InvalidDataException("Invalid vertex attribute layout: " + name);
        }

        return values;
    }
}
