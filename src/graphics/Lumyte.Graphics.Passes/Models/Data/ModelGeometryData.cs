using System.Numerics;
using System.Collections.Immutable;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

public enum ModelTopology { Points, Lines, LineLoop, LineStrip, Triangles, TriangleStrip, TriangleFan }

/// <summary>Owned, persistent attribute values. Range edits copy only affected tree nodes.</summary>
public sealed class ModelVertexAttributeData<T> : IGpuUploadData where T : struct
{
    public ModelVertexAttributeData(GpuUploadDataKey key, IEnumerable<T> values) : this(key, values.ToImmutableList()) { }
    private ModelVertexAttributeData(GpuUploadDataKey key, ImmutableList<T> values)
    {
        if (typeof(T) != typeof(Vector2) && typeof(T) != typeof(Vector3) && typeof(T) != typeof(Vector4))
        { throw new NotSupportedException("Model attributes require Vector2, Vector3 or Vector4."); }
        Key = key; Values = values;
    }
    public GpuUploadDataKey Key { get; }
    public ImmutableList<T> Values { get; }
    public ModelVertexAttributeData<T> WithRange(GpuUploadDataKey nextKey, int first, IEnumerable<T> values)
    {
        if (nextKey == Key) { throw new ArgumentException("A changed attribute requires a new key.", nameof(nextKey)); }
        T[] replacement = values.ToArray();
        if (first < 0 || first > Values.Count - replacement.Length) { throw new ArgumentOutOfRangeException(nameof(first)); }
        var result = Values;
        for (int i = 0; i < replacement.Length; i++) { result = result.SetItem(first + i, replacement[i]); }
        return new(nextKey, result);
    }
}

public sealed class ModelIndexData : IGpuUploadData
{
    public ModelIndexData(GpuUploadDataKey key, IEnumerable<uint> values) { Key = key; Values = values.ToImmutableList(); }
    public GpuUploadDataKey Key { get; }
    public ImmutableList<uint> Values { get; }
    public ModelIndexData WithRange(GpuUploadDataKey nextKey, int first, IEnumerable<uint> values)
    {
        if (nextKey == Key) { throw new ArgumentException("Changed indices require a new key.", nameof(nextKey)); }
        uint[] replacement = values.ToArray();
        if (first < 0 || first > Values.Count - replacement.Length) { throw new ArgumentOutOfRangeException(nameof(first)); }
        var result = Values;
        for (int i = 0; i < replacement.Length; i++) { result = result.SetItem(first + i, replacement[i]); }
        return new(nextKey, result);
    }
}

public sealed record ModelTexCoordSet(int SetIndex, ModelVertexAttributeData<Vector2> Values);
public sealed record ModelColorSet(int SetIndex, ModelVertexAttributeData<Vector4> Values);
public sealed class ModelVertexData
{
    public ModelVertexData(ModelVertexAttributeData<Vector3> positions, ModelVertexAttributeData<Vector3>? normals = null,
        ModelVertexAttributeData<Vector4>? tangents = null, IEnumerable<ModelTexCoordSet>? texCoords = null,
        IEnumerable<ModelColorSet>? colors = null, ModelSkinInfluenceData? skinInfluences = null)
    {
        Positions = positions; Normals = normals; Tangents = tangents;
        TexCoords = (texCoords ?? []).ToImmutableArray(); Colors = (colors ?? []).ToImmutableArray(); SkinInfluences = skinInfluences;
        int count = positions.Values.Count;
        if ((normals is not null && normals.Values.Count != count) || (tangents is not null && tangents.Values.Count != count)
            || TexCoords.Any(x => x.Values.Values.Count != count) || Colors.Any(x => x.Values.Values.Count != count)
            || (skinInfluences is not null && skinInfluences.VertexOffsets.Length != count + 1))
        { throw new ArgumentException("Every attribute must match the position count."); }
        if (TexCoords.Select(x => x.SetIndex).Distinct().Count() != TexCoords.Length || Colors.Select(x => x.SetIndex).Distinct().Count() != Colors.Length)
        { throw new ArgumentException("Attribute set indices must be unique."); }
    }
    public ModelVertexAttributeData<Vector3> Positions { get; }
    public ModelVertexAttributeData<Vector3>? Normals { get; }
    public ModelVertexAttributeData<Vector4>? Tangents { get; }
    public ImmutableArray<ModelTexCoordSet> TexCoords { get; }
    public ImmutableArray<ModelColorSet> Colors { get; }
    public ModelSkinInfluenceData? SkinInfluences { get; }
}

public sealed record ModelGeometryData(GpuUploadDataKey Key, ModelTopology Topology, ModelVertexData Vertices,
    ModelIndexData? Indices = null) : IGpuUploadData
{
    public ImmutableArray<ModelMorphTargetData> MorphTargets { get; init; } = [];
}
public readonly record struct ModelDrawRange(int First, int Count);
public readonly record struct ModelJointWeight(int JointIndex, float Weight);
public sealed class ModelSkinInfluenceData : IGpuUploadData
{
    public ModelSkinInfluenceData(GpuUploadDataKey key, IEnumerable<int> vertexOffsets, IEnumerable<ModelJointWeight> influences)
    {
        Key = key; VertexOffsets = vertexOffsets.ToImmutableArray(); Influences = influences.ToImmutableArray();
        if (VertexOffsets.IsEmpty || VertexOffsets[0] != 0 || VertexOffsets[^1] != Influences.Length
            || VertexOffsets.Zip(VertexOffsets.Skip(1)).Any(x => x.First > x.Second))
        { throw new ArgumentException("Offsets must partition the complete influence array.", nameof(vertexOffsets)); }
        if (Influences.Any(x => x.JointIndex < 0 || !float.IsFinite(x.Weight) || x.Weight < 0))
        { throw new ArgumentException("Influences require nonnegative joints and finite nonnegative weights.", nameof(influences)); }
    }
    public GpuUploadDataKey Key { get; }
    public ImmutableArray<int> VertexOffsets { get; }
    public ImmutableArray<ModelJointWeight> Influences { get; }
}
public sealed class ModelSkinPaletteData(GpuUploadDataKey key, IEnumerable<Matrix4x4> jointMatrices) : IGpuUploadData
{
    public GpuUploadDataKey Key { get; } = key;
    public ImmutableArray<Matrix4x4> JointMatrices { get; } = jointMatrices.ToImmutableArray();
}
public sealed class ModelMorphWeightsData(GpuUploadDataKey key, IEnumerable<float> weights) : IGpuUploadData
{
    public GpuUploadDataKey Key { get; } = key;
    public ImmutableArray<float> Weights { get; } = weights.ToImmutableArray();
}
public sealed record ModelMorphTargetData(GpuUploadDataKey Key, ModelVertexAttributeData<Vector3>? PositionDeltas = null,
    ModelVertexAttributeData<Vector3>? NormalDeltas = null, ModelVertexAttributeData<Vector3>? TangentDeltas = null) : IGpuUploadData;
public sealed record ModelDeformationData(ModelSkinPaletteData? Skin = null, ModelMorphWeightsData? Morph = null);
