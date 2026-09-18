using System.Numerics;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.RenderGraph.Conformance;

// Tiny analytically specified images exercise the public model API on every provider.
internal static class ModelTextureReference
{
    internal static ModelFixture Create(string scenario)
    {
        var original = ModelRenderConsumer.Create("indexed");
        var draw = original.Snapshot.Draws.Items.Single();
        Vector4[] colors = [new(1,0,0,1),new(0,1,0,1),new(0,0,1,1),Vector4.One];
        byte[] bytes = [255,0,0,255,0,255,0,255,0,0,255,255,255,255,255,255];
        uint width=2,height=2;
        var encoding=GpuImageColorEncoding.Linear; var alpha=GpuImageAlphaMode.Opaque;
        var sampling = new ModelSamplerData(ModelTextureFilter.Nearest,ModelTextureFilter.Nearest,ModelMipFilter.None,ModelTextureWrap.ClampToEdge,ModelTextureWrap.ClampToEdge);
        var transform=Matrix3x2.Identity;
        int set = scenario=="texture-uv7" ? 7 : 0;
        Vector4? constant = null;
        var material=draw.Material with { BaseColorFactor=Vector4.One };
        var lighting=ModelLighting.Empty;
        if (scenario=="texture-linear") { sampling=sampling with { MinFilter=ModelTextureFilter.Linear,MagFilter=ModelTextureFilter.Linear }; }
        if (scenario is "texture-repeat" or "texture-mirror" or "texture-clamp")
        {
            transform=Matrix3x2.CreateScale(3)*Matrix3x2.CreateTranslation(-1,-1);
            var wrap=scenario=="texture-repeat" ? ModelTextureWrap.Repeat : scenario=="texture-mirror" ? ModelTextureWrap.MirroredRepeat : ModelTextureWrap.ClampToEdge;
            sampling=sampling with { WrapU=wrap,WrapV=wrap };
        }
        if (scenario=="texture-transform") { transform=new(0,1,-1,0,1,0); }
        if (scenario=="texture-trilinear")
        { transform=Matrix3x2.CreateScale(16*MathF.Sqrt(2));sampling=sampling with { MipFilter=ModelMipFilter.Linear }; }
        if (scenario=="texture-srgb")
        { width=height=1; bytes=[128,128,128,255]; encoding=GpuImageColorEncoding.Srgb; constant=new(.2158605f,.2158605f,.2158605f,1); }
        if (scenario=="texture-premultiplied")
        { width=height=1; bytes=[64,0,0,128]; alpha=GpuImageAlphaMode.Premultiplied; material=material with { AlphaMode=ModelAlphaMode.Blend }; constant=new(64/255f,0,0,128/255f); }
        if (scenario=="texture-metallic")
        {
            width=height=1; bytes=[0,255,128,255]; encoding=GpuImageColorEncoding.Srgb;
            material=material with { Unlit=false,BaseColorFactor=new(.25f,.5f,.75f,1),MetallicFactor=1,RoughnessFactor=1 };
            lighting=new([ModelLight.Directional(-Vector3.UnitZ,Vector3.One,MathF.PI)]);
            var baseRgb=new Vector3(.25f,.5f,.75f); float m=128/255f; var f0=Vector3.Lerp(new(.04f),baseRgb,m);
            constant=new((Vector3.One-f0)*(1-m)*baseRgb+f0/4,1);
        }
        if (scenario=="texture-emissive")
        {
            width=height=1; bytes=[128,0,255,255]; encoding=GpuImageColorEncoding.Srgb;
            material=material with { Unlit=false,EmissiveFactor=new(2,1,.5f),EmissiveStrength=2 };
            constant=new(.2158605f*4,0,1,1);
        }
        if (scenario is "texture-mip" or "texture-supplied-mip" or "texture-odd-mip")
        {
            transform=Matrix3x2.CreateScale(64); sampling=sampling with { MipFilter=ModelMipFilter.Nearest };
            constant=new(.5f,.5f,.5f,1);
            if (scenario=="texture-supplied-mip") { constant=new(1,1,0,1); }
            if (scenario=="texture-odd-mip") { width=3;height=1;bytes=[255,0,0,255,0,255,0,255,0,0,255,255];constant=new(1/3f,1/3f,1/3f,1); }
        }
        List<GpuImageSubresourceData> levels=[new(0,0,width*4,width*height*4,bytes)];
        if (scenario=="texture-supplied-mip") { levels.Add(new(1,0,4,4,new byte[] {255,255,0,255})); }
        var image=new GpuImageUploadData(new(scenario,0),new(width,height,GpuFormat.Rgba8Unorm,MipLevelCount:(uint)levels.Count),encoding,alpha,levels);
        var use=new ModelTextureUse(new(image,sampling),set) { Transform=transform };
        material=scenario switch
        {
            "texture-metallic" => material with { MetallicRoughnessTexture=use },
            "texture-emissive" => material with { EmissiveTexture=use },
            _ => material with { BaseColorTexture=use },
        };
        var vertices=new ModelVertexData(draw.Geometry.Vertices.Positions,texCoords:[new(set,new(new("uv",0),new Vector2[] {new(0,1),new(1,1),new(1,0),new(0,0)}))]);
        draw=draw with { Geometry=draw.Geometry with { Vertices=vertices },Material=material };
        var snapshot=original.Snapshot with { Draws=ModelDrawSnapshot.From([draw]),Lighting=lighting };
        var graph=new GpuRenderGraph(); var input=graph.CreateInput("model",ModelRenderInputContract.Instance,snapshot);
        var color=graph.CreateTexture("color",new(32,32,GpuFormat.Rgba16Float));var depth=graph.CreateTexture("depth",new(32,32,GpuFormat.D32Float));
        graph.AddModelPass("model",new(input,color,depth));graph.ExportTexture(color);
        var expected=new Vector4[1024];
        for(int y=0;y<32;y++)
        {
        for(int x=0;x<32;x++)
        {
            var uv=Vector2.Transform(new((x+.5f)/32,(y+.5f)/32),transform);
            expected[y*32+x]=constant ?? Sample(colors,uv,sampling);
            if(scenario=="texture-trilinear") { expected[y*32+x]=Vector4.Lerp(expected[y*32+x],new(.5f,.5f,.5f,1),.5f); }
        }
        }
        return new(graph.Compile(),color,input,snapshot,default,false) { ExpectedPixels=expected };
    }
    private static Vector4 Sample(Vector4[] colors,Vector2 uv,ModelSamplerData sampling)
    {
        // Reference uses normalized wrap followed by reconstruction at texel centers.
        int Address(int index,ModelTextureWrap wrap)
        {
            if(wrap==ModelTextureWrap.ClampToEdge) { return Math.Clamp(index,0,1); }
            if(wrap==ModelTextureWrap.Repeat) { return index&1; }
            int p=index&3;return p<2 ? p : 3-p;
        }
        Vector4 At(int x,int y) => colors[Address(y,sampling.WrapV)*2+Address(x,sampling.WrapU)];
        if(sampling.MagFilter==ModelTextureFilter.Nearest) { return At((int)MathF.Floor(uv.X*2),(int)MathF.Floor(uv.Y*2)); }
        var p=uv*2-new Vector2(.5f);int ix=(int)MathF.Floor(p.X),iy=(int)MathF.Floor(p.Y);
        return Vector4.Lerp(Vector4.Lerp(At(ix,iy),At(ix+1,iy),p.X-ix),Vector4.Lerp(At(ix,iy+1),At(ix+1,iy+1),p.X-ix),p.Y-iy);
    }
}
