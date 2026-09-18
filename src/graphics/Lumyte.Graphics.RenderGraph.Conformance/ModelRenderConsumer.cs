using System.Numerics;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.RenderGraph.Conformance;

/// <summary>One compiled feature consumer, with independent expected HDR colors for all providers.</summary>
public static class ModelRenderConsumer
{
    public static readonly string[] Cases = ["empty","indexed","nonindexed","strip","fan","depth","mask","blend","reflected","skin","morph","unlit-hdr","pbr-dark","pbr-dielectric","pbr-metal","retained",
        "texture-nearest","texture-linear","texture-repeat","texture-mirror","texture-clamp","texture-transform","texture-uv7",
        "texture-srgb","texture-premultiplied","texture-metallic","texture-emissive","texture-mip","texture-supplied-mip","texture-odd-mip","texture-trilinear","texture-retained"];
    public static ModelFixture Create(string scenario)
    {
        if (scenario.StartsWith("texture-",StringComparison.Ordinal)) { return ModelTextureReference.Create(scenario); }
        Vector3[] positions = [new(-1,-1,0),new(1,-1,0),new(1,1,0),new(-1,1,0)];
        uint[] indices = [0,1,2,0,2,3];
        if (scenario == "nonindexed") { positions = indices.Select(i => positions[i]).ToArray(); }
        var vertices = new ModelVertexData(new(new("positions",0),positions));
        var geometry = new ModelGeometryData(new("geometry",0),ModelTopology.Triangles,vertices,scenario == "nonindexed" ? null : new(new("indices",0),indices));
        if (scenario == "strip") { geometry = geometry with { Topology = ModelTopology.TriangleStrip,Indices = new(new("strip",0),[0,1,3,2]) }; }
        if (scenario == "fan") { geometry = geometry with { Topology = ModelTopology.TriangleFan,Indices = new(new("fan",0),[0,1,2,3]) }; }
        var material = new ModelMaterialData(new("material",0)) { Unlit = true,BaseColorFactor = new(.25f,.5f,.75f,1) };
        Vector4 expected = material.BaseColorFactor;
        var draw = new ModelDrawItem(geometry,material,Matrix4x4.Identity);
        var list = new ModelDrawList();
        if (scenario == "reflected") { draw = draw with { LocalToWorld = Matrix4x4.CreateScale(-1,1,1) }; }
        if (scenario == "skin")
        {
            var influences = new ModelSkinInfluenceData(new("influences",0),[0,5,10,15,20],Enumerable.Repeat(new ModelJointWeight(0,.2f),20));
            geometry = geometry with { Vertices = new(vertices.Positions,skinInfluences:influences) };
            draw = draw with { Geometry = geometry, Deformation = new(new(new("palette",0),[Matrix4x4.CreateScale(.5f)])) };
        }
        if (scenario == "morph")
        {
            geometry = geometry with { MorphTargets = [new(new("target",0),new(new("deltas",0),positions.Select(p => -.5f*p)))] };
            draw = draw with { Geometry = geometry, Deformation = new(Morph:new(new("weights",0),[1])) };
        }
        if (scenario == "unlit-hdr") { draw = draw with { Material = material with { BaseColorFactor = new(4,2,1,1) } }; expected = new(4,2,1,1); }
        if (scenario == "pbr-dark") { draw = draw with { Material = material with { Unlit = false } }; expected = new(0,0,0,1); }
        if (scenario is "depth" or "mask")
        {
            list.Add(draw);
            draw = draw with { LocalToWorld = Matrix4x4.CreateTranslation(0,0,scenario == "depth" ? -1 : 1),
                Material = material with { Key = new("foreground",0), BaseColorFactor = new(1,0,0,.1f),AlphaMode = scenario == "mask" ? ModelAlphaMode.Mask : ModelAlphaMode.Opaque } };
        }
        if (scenario == "blend")
        {
            // Add front before back deliberately; depth order must be derived, not list order.
            list.Add(draw with { LocalToWorld = Matrix4x4.CreateTranslation(0,0,1), Material = material with { Key = new("front",0),BaseColorFactor = new(1,0,0,.5f),AlphaMode = ModelAlphaMode.Blend } });
            draw = draw with { Material = material with { BaseColorFactor = new(0,0,1,.5f),AlphaMode = ModelAlphaMode.Blend } };
            expected = new(.5f,0,.25f,.75f);
        }
        if (scenario != "empty") { list.Add(draw); } else { expected = default; }
        ModelLighting lighting = ModelLighting.Empty;
        if (scenario is "pbr-dielectric" or "pbr-metal")
        {
            bool metal = scenario == "pbr-metal";
            list.Clear(); list.Add(draw with { Material = material with { Unlit = false,MetallicFactor = metal ? 1 : 0,RoughnessFactor = 1 } });
            lighting = new([ModelLight.Directional(-Vector3.UnitZ,Vector3.One,MathF.PI)]);
            // At normal incidence with roughness one: F0/4 plus diffuse*(1-F0).
            var rgb = new Vector3(expected.X,expected.Y,expected.Z);
            rgb = metal ? rgb/4 : rgb*.96f+new Vector3(.01f);
            expected = new(rgb,1);
        }
        var snapshot = new ModelRenderSnapshot(ModelCamera.Orthographic(new(0,0,3),Vector3.Zero,Vector3.UnitY,2,.1f,10),list.Snapshot(),lighting);
        var graph = new GpuRenderGraph(); var input = graph.CreateInput("model",ModelRenderInputContract.Instance,snapshot);
        var color = graph.CreateTexture("color",new(32,32,GpuFormat.Rgba16Float)); var depth = graph.CreateTexture("depth",new(32,32,GpuFormat.D32Float));
        graph.AddModelPass("model",new(input,color,depth)); graph.ExportTexture(color);
        return new(graph.Compile(),color,input,snapshot,expected,scenario is "skin" or "morph");
    }
    public static ModelFixture Changed(ModelFixture original) => original.Snapshot.Draws.Items.First().Material.BaseColorTexture is not null
        ? original with { Snapshot=ModelTextureReference.Create("texture-transform").Snapshot,ExpectedPixels=ModelTextureReference.Create("texture-transform").ExpectedPixels }
        : original with
    {
        Snapshot = original.Snapshot with { Draws = ModelDrawSnapshot.From(original.Snapshot.Draws.Items.Select(d => d with
            { LocalToWorld = Matrix4x4.CreateScale(.5f),Material = d.Material with { Key = new("changed",1),BaseColorFactor = new(1,0,0,1) } })) },
        Expected = new(1,0,0,1),HalfSize = true,
    };
    public static void Compare(ModelFixture fixture,ReadOnlySpan<byte> pixels,int pitch)
    {
        for (int y=0;y<32;y++)
        {
        for (int x=0;x<32;x++)
        {
            var p = pixels.Slice(y*pitch+x*8,8);
            var actual = new Vector4((float)BitConverter.ToHalf(p),(float)BitConverter.ToHalf(p[2..]),(float)BitConverter.ToHalf(p[4..]),(float)BitConverter.ToHalf(p[6..]));
            var expected = fixture.ExpectedPixels is { } reference ? reference[y*32+x]
                : fixture.HalfSize && (x<8 || x>=24 || y<8 || y>=24) ? Vector4.Zero : fixture.Expected;
            if (!float.IsFinite(actual.X+actual.Y+actual.Z+actual.W) || Vector4.Distance(actual,expected)>.006f)
            { throw new InvalidOperationException($"Model pixel ({x},{y}): expected {expected}, actual {actual}."); }
        }
        }
    }
}
public sealed record ModelFixture(GpuRenderGraphPlan Plan,GpuRenderGraphTexture Output,GpuGraphInput<ModelRenderSnapshot> Input,ModelRenderSnapshot Snapshot,Vector4 Expected,bool HalfSize)
{
    public Vector4[]? ExpectedPixels { get; init; }
}
