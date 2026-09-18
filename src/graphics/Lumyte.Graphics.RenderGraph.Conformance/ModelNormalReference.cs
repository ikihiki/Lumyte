using System.Numerics;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.RenderGraph.Conformance;

internal static class ModelNormalReference
{
    internal static ModelFixture Create(string scenario)
    {
        var original=ModelRenderConsumer.Create("indexed");var draw=original.Snapshot.Draws.Items.Single();
        Vector2[] uv=[new(0,1),new(1,1),new(1,0),new(0,0)];
        Vector4? tangent=scenario is "normal-provided" or "normal-morph" ? new(1,0,0,-1) : null;
        if(scenario is "normal-handedness" or "normal-flat") { tangent=new(1,0,0,1); }
        if(scenario=="normal-mirrored-uv") { uv=uv.Select(p=>new Vector2(1-p.X,p.Y)).ToArray(); }
        var vertices=new ModelVertexData(draw.Geometry.Vertices.Positions,
            normals:scenario=="normal-flat" ? null : new(new("normals",0),Enumerable.Repeat(Vector3.UnitZ,4)),
            tangents:tangent is { } t ? new(new("tangents",0),Enumerable.Repeat(t,4)) : null,
            texCoords:[new(0,new(new("uv",0),uv))],
            skinInfluences:scenario=="normal-skin" ? new(new("influence",0),[0,1,2,3,4],Enumerable.Repeat(new ModelJointWeight(0,1),4)) : null);
        var image=new GpuImageUploadData(new("normal",0),new(1,1,GpuFormat.Rgba8Unorm),GpuImageColorEncoding.Srgb,GpuImageAlphaMode.Opaque,
            [new(0,0,4,4,new byte[] {204,179,230,255})]);
        float scale=scenario=="normal-zero-scale" ? 0 : scenario=="normal-half-scale" ? .5f : 1;
        var material=draw.Material with { Unlit=false,MetallicFactor=0,RoughnessFactor=1,
            NormalTexture=new(new(image,new())) { Transform=scenario=="normal-uv-transform" ? new(0,1,-1,0,1,0) : Matrix3x2.Identity },NormalScale=scale };
        var geometry=draw.Geometry with { Vertices=vertices };
        draw=draw with { Geometry=geometry,Material=material };
        if(scenario=="normal-reflected") { draw=draw with { LocalToWorld=Matrix4x4.CreateScale(-1,1,1) }; }
        if(scenario=="normal-skin") { draw=draw with { Deformation=new(new(new("palette",0),[Matrix4x4.CreateRotationZ(MathF.PI/2)])) }; }
        if(scenario=="normal-morph")
        {
            draw=draw with { Geometry=geometry with { MorphTargets=[new(new("morph",0),TangentDeltas:new(new("delta",0),Enumerable.Repeat(new Vector3(-1,1,0),4)))] },
                Deformation=new(Morph:new(new("weights",0),[1])) };
        }
        var light=Vector3.Normalize(new Vector3(.4f,.5f,1));
        var snapshot=original.Snapshot with { Draws=ModelDrawSnapshot.From([draw]),Lighting=new([ModelLight.Directional(-light,Vector3.One,MathF.PI)]) };
        var decoded=new Vector3((float)(Half)(204/255f)*2-1,(float)(Half)(179/255f)*2-1,(float)(Half)(230/255f)*2-1);
        decoded.X*=scale;decoded.Y*=scale;
        var normal=scenario switch
        {
            "normal-handedness" => decoded,
            "normal-mirrored-uv" or "normal-reflected" => new(-decoded.X,-decoded.Y,decoded.Z),
            "normal-skin" or "normal-morph" or "normal-uv-transform" => new(decoded.Y,decoded.X,decoded.Z),
            _ => new(decoded.X,-decoded.Y,decoded.Z),
        };
        normal=Vector3.Normalize(normal);
        // Independent evaluation of the roughness-one dielectric equation.
        float nl=Math.Max(0,Vector3.Dot(normal,light)),nv=Math.Max(0,normal.Z);
        var halfway=Vector3.Normalize(light+Vector3.UnitZ);
        float f=.04f+.96f*MathF.Pow(1-halfway.Z,5);
        var baseRgb=new Vector3(material.BaseColorFactor.X,material.BaseColorFactor.Y,material.BaseColorFactor.Z);
        var expected=new Vector4(baseRgb*((1-f)*nl)+new Vector3(f*.5f*nl/(nl+nv)),1);
        var graph=new GpuRenderGraph();var input=graph.CreateInput("model",ModelRenderInputContract.Instance,snapshot);
        var color=graph.CreateTexture("color",new(32,32,GpuFormat.Rgba16Float));var depth=graph.CreateTexture("depth",new(32,32,GpuFormat.D32Float));
        graph.AddModelPass("model",new(input,color,depth));graph.ExportTexture(color);
        return new(graph.Compile(),color,input,snapshot,expected,false);
    }
}
