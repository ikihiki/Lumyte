using System.Numerics;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.ModelPreparation;

// Compiled independently into each provider. This is CPU geometry math, not a shared public GPU ABI.
internal sealed record ModelUvLayout(int BaseColor, int MetallicRoughness, int Normal, int Occlusion, int Emissive,Matrix3x2 NormalTransform)
{
    internal static ModelUvLayout From(ModelMaterialData m) => new(m.BaseColorTexture?.TexCoordSet ?? -1,
        m.MetallicRoughnessTexture?.TexCoordSet ?? -1, m.NormalTexture?.TexCoordSet ?? -1,
        m.OcclusionTexture?.TexCoordSet ?? -1, m.EmissiveTexture?.TexCoordSet ?? -1,m.NormalTexture?.Transform ?? Matrix3x2.Identity);
    internal int[] Sets => [BaseColor, MetallicRoughness, Normal, Occlusion, Emissive];
}
internal sealed record GeometryKey(ModelGeometryData Geometry, ModelDeformationData? Deformation, ModelDrawRange? Range, ModelUvLayout Uv);
internal sealed record PreparedGeometry(Vector4[] Vertices, Vector3 Minimum, Vector3 Maximum)
{
    internal uint VertexCount => (uint)(Vertices.Length / 7);
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
        var skinTransforms = key.Uv.Normal>=0 && key.Deformation?.Skin is not null && v.SkinInfluences is not null
            ? Enumerable.Repeat(Matrix4x4.Identity,count).ToArray() : null;
        var colors = v.Colors.FirstOrDefault(x => x.SetIndex == 0)?.Values;
        var uvAttributes = key.Uv.Sets.Select(set => set < 0 ? null : v.TexCoords.FirstOrDefault(uv => uv.SetIndex == set)?.Values
            ?? throw new ArgumentException($"Required texture coordinate set {set} is missing.", nameof(key))).ToArray();
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
                    matrix *= 1 / weight;
                    if(skinTransforms is not null) { skinTransforms[i]=matrix; }
                    p = Vector3.Transform(p, matrix);
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
        List<Vector4> result = []; List<int> sourceIndices=[];List<Vector3> sourceNormals=[];
        Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        void Triangle(int a, int b, int c)
        {
            int ia = Index(a), ib = Index(b), ic = Index(c);
            var flat = Vector3.Cross(positions[ib] - positions[ia], positions[ic] - positions[ia]);
            var originalFlat=Vector3.Cross(v.Positions.Values[ib]-v.Positions.Values[ia],v.Positions.Values[ic]-v.Positions.Values[ia]);
            originalFlat=originalFlat.LengthSquared()>1e-20f ? Vector3.Normalize(originalFlat) : Vector3.UnitZ;
            foreach (int index in new[] { ia, ib, ic })
            {
                var p = positions[index]; var normal = v.Normals is null ? flat : normals[index];
                normal = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitZ;
                result.Add(new(p, 1)); result.Add(new(normal, 0)); result.Add(colors?.Values[index] ?? Vector4.One);
                Vector2 Uv(int slot) => uvAttributes[slot]?.Values[index] ?? default;
                var uv0 = Uv(0); var uv1 = Uv(1); var uv2 = Uv(2); var uv3 = Uv(3); var uv4 = Uv(4);
                result.Add(new(uv0.X, uv0.Y, uv1.X, uv1.Y));
                result.Add(new(uv2.X, uv2.Y, uv3.X, uv3.Y)); result.Add(new(uv4, 0, 0));
                result.Add(new(1,0,0,1));
                if(key.Uv.Normal>=0)
                {
                    sourceIndices.Add(index);
                    var sourceNormal=v.Normals?.Values[index] ?? originalFlat;
                    sourceNormals.Add(sourceNormal.LengthSquared()>1e-20f ? Vector3.Normalize(sourceNormal) : Vector3.UnitZ);
                }
                minimum = Vector3.Min(minimum, p); maximum = Vector3.Max(maximum, p);
            }
        }
        if (g.Topology == ModelTopology.Triangles) { for (int i = 0; i + 2 < range.Count; i += 3) { Triangle(i, i + 1, i + 2); } }
        else if (g.Topology == ModelTopology.TriangleFan) { for (int i = 1; i + 1 < range.Count; i++) { Triangle(0, i, i + 1); } }
        else { for (int i = 0; i + 2 < range.Count; i++) { Triangle(i + (i & 1), i + 1 - (i & 1), i + 2); } }
        if(key.Uv.Normal>=0)
        {
            bool flatNormals=v.Normals is null;
            Vector3 Xyz(Vector4 value) => new(value.X,value.Y,value.Z);
            var generated=v.Tangents is null || flatNormals ? MikkTangents.Generate(
                flatNormals ? Enumerable.Range(0,sourceIndices.Count).Select(i=>Xyz(result[i*7])).ToArray() : sourceIndices.Select(i=>v.Positions.Values[i]).ToArray(),
                flatNormals ? Enumerable.Range(0,sourceIndices.Count).Select(i=>Xyz(result[i*7+1])).ToArray() : sourceNormals.ToArray(),
                sourceIndices.Select(i=>Vector2.Transform(uvAttributes[2]!.Values[i],key.Uv.NormalTransform)).ToArray()) : null;
            for(int corner=0;corner<sourceIndices.Count;corner++)
            {
                int index=sourceIndices[corner];var value=generated is null ? v.Tangents!.Values[index] : generated[corner];
                var tangent=new Vector3(value.X,value.Y,value.Z);
                if(morph is not null && !flatNormals)
                {
                    for(int target=0;target<morph.Weights.Length;target++)
                    {
                        if(g.MorphTargets[target].TangentDeltas is not { } delta) { continue; }
                        if(delta.Values.Count!=count) { throw new ArgumentException("Morph tangents must match the vertex count.",nameof(key)); }
                        tangent+=delta.Values[index]*morph.Weights[target];
                    }
                }
                var matrix=flatNormals ? Matrix4x4.Identity : skinTransforms?[index] ?? Matrix4x4.Identity;
                tangent=Vector3.TransformNormal(tangent,matrix);
                result[corner*7+6]=new(tangent,value.W*(matrix.GetDeterminant()<0 ? -1 : 1));
            }
        }
        return new(result.ToArray(), minimum, maximum);
    }
    internal static float Depth(PreparedGeometry geometry, Matrix4x4 world, Matrix4x4 view)
    {
        // Bounds of actual referenced vertices after deformation and world transformation.
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = 0; i < geometry.Vertices.Length; i += 7)
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
        foreach (var texture in m.Textures)
        {
            var sampler = texture?.Texture.Sampler ?? new ModelSamplerData(); var uv = texture?.Transform ?? Matrix3x2.Identity;
            data.Add(new(0, texture is null ? 0 : 1, (float)sampler.MinFilter, (float)sampler.MagFilter));
            data.Add(new((float)sampler.MipFilter, (float)sampler.WrapU, (float)sampler.WrapV, 0));
            data.Add(new(uv.M11, uv.M12, uv.M21, uv.M22)); data.Add(new(uv.M31, uv.M32, 0, 0));
        }
        data[28]=new(data[28].X,data[28].Y,m.NormalScale,world.GetDeterminant()<0 ? -1 : 1);
        var environment = snapshot.Lighting.Environment;
        var rotation = Matrix4x4.Identity;
        if (environment is not null)
        {
            float length = environment.Rotation.LengthSquared();
            if (!float.IsFinite(length) || length <= 0 || !float.IsFinite(environment.Intensity) || environment.Intensity < 0)
            { throw new ArgumentException("Environment rotation must be finite and nonzero and intensity must be finite and nonnegative.", nameof(snapshot)); }
            rotation = Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(Quaternion.Normalize(environment.Rotation)));
        }
        data.Add(new(0, 0, 0, environment?.Intensity ?? 0));
        data.Add(new(rotation.M11, rotation.M12, rotation.M13, 0));
        data.Add(new(rotation.M21, rotation.M22, rotation.M23, 0));
        data.Add(new(rotation.M31, rotation.M32, rotation.M33, 0));
        data.Add(new(environment is null ? 0 : 1, 8, m.OcclusionStrength, 0));
        foreach (var light in snapshot.Lighting.Lights)
        {
            data.Add(new(light.Position, (float)light.Kind)); data.Add(new(light.Direction, light.Range ?? 0));
            data.Add(new(light.Color * light.Intensity, MathF.Cos(light.InnerAngle))); data.Add(new(MathF.Cos(light.OuterAngle), 0, 0, 0));
        }
        return data.ToArray();
    }
}
