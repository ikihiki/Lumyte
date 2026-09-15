using System.Numerics;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.ModelPreparation;

// Compiled independently into each provider. This is CPU geometry math, not a shared public GPU ABI.
internal sealed record GeometryKey(ModelGeometryData Geometry, ModelDeformationData? Deformation, ModelDrawRange? Range);
internal sealed record PreparedGeometry(Vector4[] Vertices, Vector3 Minimum, Vector3 Maximum)
{
    internal uint VertexCount => (uint)(Vertices.Length / 3);
}
internal static class ModelPreparation
{
    internal static PreparedGeometry Prepare(GeometryKey key)
    {
        var g = key.Geometry; var v = g.Vertices; int count = v.Positions.Values.Count;
        var range = key.Range ?? new ModelDrawRange(0, g.Indices?.Values.Count ?? count);
        if (range.First < 0 || range.Count < 0 || range.First > (g.Indices?.Values.Count ?? count) - range.Count)
        { throw new ArgumentOutOfRangeException(nameof(key), "Draw range exceeds the geometry."); }
        if (g.Topology is not (ModelTopology.Triangles or ModelTopology.TriangleStrip or ModelTopology.TriangleFan))
        { throw new NotSupportedException("The initial model renderer supports triangle, strip and fan topology."); }
        var positions = new Vector3[count]; var normals = new Vector3[count];
        var colors = v.Colors.FirstOrDefault(x => x.SetIndex == 0)?.Values;
        var morph = key.Deformation?.Morph;
        if (morph is not null && morph.Weights.Length != g.MorphTargets.Length)
        { throw new ArgumentException("Morph weights must match the target count.", nameof(key)); }
        for (int i = 0; i < count; i++)
        {
            Vector3 p = v.Positions.Values[i], n = v.Normals?.Values[i] ?? default;
            if (morph is not null)
            {
                for (int j = 0; j < morph.Weights.Length; j++)
                {
                    var target = g.MorphTargets[j];
                    if ((target.PositionDeltas is { } pd && pd.Values.Count != count) || (target.NormalDeltas is { } nd && nd.Values.Count != count))
                    { throw new ArgumentException("Morph attributes must match the vertex count.", nameof(key)); }
                    p += (target.PositionDeltas?.Values[i] ?? default) * morph.Weights[j];
                    n += (target.NormalDeltas?.Values[i] ?? default) * morph.Weights[j];
                }
            }
            if (key.Deformation?.Skin is { } skin && v.SkinInfluences is { } influences)
            {
                Matrix4x4 matrix = default; float weight = 0;
                for (int j = influences.VertexOffsets[i]; j < influences.VertexOffsets[i + 1]; j++)
                {
                    var influence = influences.Influences[j];
                    if (influence.JointIndex >= skin.JointMatrices.Length) { throw new ArgumentException("Skin joint exceeds the palette.", nameof(key)); }
                    matrix += skin.JointMatrices[influence.JointIndex] * influence.Weight; weight += influence.Weight;
                }
                if (weight > 0)
                {
                    matrix *= 1 / weight; p = Vector3.Transform(p, matrix);
                    if (v.Normals is not null)
                    {
                        if (!Matrix4x4.Invert(matrix, out var inverse)) { throw new ArgumentException("Skin normal transform is singular.", nameof(key)); }
                        n = Vector3.TransformNormal(n, Matrix4x4.Transpose(inverse));
                    }
                }
            }
            positions[i] = p; normals[i] = n;
        }
        int Index(int offset)
        {
            uint index = g.Indices is { } indices ? indices.Values[range.First + offset] : (uint)(range.First + offset);
            if (index >= count) { throw new ArgumentException("Index exceeds the vertex count.", nameof(key)); }
            return (int)index;
        }
        List<Vector4> result = []; Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        void Triangle(int a, int b, int c)
        {
            int ia = Index(a), ib = Index(b), ic = Index(c);
            var flat = Vector3.Cross(positions[ib] - positions[ia], positions[ic] - positions[ia]);
            foreach (int index in new[] { ia, ib, ic })
            {
                var p = positions[index]; var normal = v.Normals is null ? flat : normals[index];
                normal = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitZ;
                result.Add(new(p, 1)); result.Add(new(normal, 0)); result.Add(colors?.Values[index] ?? Vector4.One);
                minimum = Vector3.Min(minimum, p); maximum = Vector3.Max(maximum, p);
            }
        }
        if (g.Topology == ModelTopology.Triangles) { for (int i = 0; i + 2 < range.Count; i += 3) { Triangle(i, i + 1, i + 2); } }
        else if (g.Topology == ModelTopology.TriangleFan) { for (int i = 1; i + 1 < range.Count; i++) { Triangle(0, i, i + 1); } }
        else { for (int i = 0; i + 2 < range.Count; i++) { Triangle(i + (i & 1), i + 1 - (i & 1), i + 2); } }
        return new(result.ToArray(), minimum, maximum);
    }
    internal static float Depth(PreparedGeometry geometry, Matrix4x4 world, Matrix4x4 view)
    {
        // Bounds of actual referenced vertices after deformation and world transformation.
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = 0; i < geometry.Vertices.Length; i += 3)
        {
            Vector4 p = geometry.Vertices[i]; var w = Vector3.Transform(new(p.X, p.Y, p.Z), world);
            min = Vector3.Min(min, w); max = Vector3.Max(max, w);
        }
        return Vector3.Transform((min + max) * .5f, view).Z;
    }
    internal static Vector4[] Parameters(ModelDrawItem draw, ModelRenderSnapshot snapshot, float aspect)
    {
        Matrix4x4 world = draw.LocalToWorld;
        if (!Matrix4x4.Invert(world, out var inverse)) { throw new ArgumentException("Model normal transform is singular.", nameof(draw)); }
        var m = draw.Material;
        List<Vector4> data = [];
        void Matrix(Matrix4x4 value)
        { data.Add(new(value.M11, value.M12, value.M13, value.M14)); data.Add(new(value.M21, value.M22, value.M23, value.M24)); data.Add(new(value.M31, value.M32, value.M33, value.M34)); data.Add(new(value.M41, value.M42, value.M43, value.M44)); }
        Matrix(world); Matrix(snapshot.Camera.GetViewProjection(aspect)); Matrix(Matrix4x4.Transpose(inverse));
        data.Add(m.BaseColorFactor); data.Add(new(m.EmissiveFactor * m.EmissiveStrength, m.Unlit ? 1 : 0));
        data.Add(new(m.MetallicFactor, m.RoughnessFactor, (float)m.AlphaMode, m.AlphaCutoff));
        data.Add(new(snapshot.Camera.Eye, snapshot.Lighting.Lights.Length));
        data.Add(new(snapshot.Camera.View.M13,snapshot.Camera.View.M23,snapshot.Camera.View.M33,snapshot.Camera.IsOrthographic ? 1 : 0));
        foreach (var light in snapshot.Lighting.Lights)
        {
            data.Add(new(light.Position, (float)light.Kind)); data.Add(new(light.Direction, light.Range ?? 0));
            data.Add(new(light.Color * light.Intensity, MathF.Cos(light.InnerAngle))); data.Add(new(MathF.Cos(light.OuterAngle), 0, 0, 0));
        }
        return data.ToArray();
    }
}
