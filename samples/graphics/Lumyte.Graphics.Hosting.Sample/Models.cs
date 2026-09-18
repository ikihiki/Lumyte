using System.Numerics;
using Lumyte.Graphics;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.RenderGraph;

internal static partial class Program
{
    private static async Task RenderModelsAsync(GpuRenderContext context,GpuSurfacePresentation presentation,int frameLimit,CancellationToken stop)
    {
        Vector3[] vertices = [new(-1,-1,-1),new(1,-1,-1),new(1,1,-1),new(-1,1,-1),new(-1,-1,1),new(1,-1,1),new(1,1,1),new(-1,1,1)];
        uint[] indices = [4,5,6,4,6,7,1,0,3,1,3,2,0,4,7,0,7,3,5,1,2,5,2,6,3,7,6,3,6,2,0,1,5,0,5,4];
        var faceVertices=Enumerable.Range(0,6).SelectMany(face => new[] {0,1,2,5}.Select(corner => vertices[indices[face*6+corner]])).ToArray();
        var faceIndices=Enumerable.Range(0,6).SelectMany(face => new uint[] {0,1,2,0,2,3}.Select(index => (uint)face*4+index)).ToArray();
        var uv=Enumerable.Range(0,6).SelectMany(_ => new Vector2[] {new(0,1),new(1,1),new(1,0),new(0,0)}).ToArray();
        var geometry = new ModelGeometryData(new("cube",0),ModelTopology.Triangles,
            new(new(new("positions",0),faceVertices),texCoords:[new(0,new(new("uv",0),uv))]),new(new("indices",0),faceIndices));
        var checker=new GpuImageUploadData(new("checker",0),new(2,2,GpuFormat.Rgba8Unorm),GpuImageColorEncoding.Srgb,GpuImageAlphaMode.Opaque,
            [new(0,0,8,16,new byte[] {255,255,255,255,80,80,80,255,80,80,80,255,255,255,255,255})]);
        var material = new ModelMaterialData(new("copper",0)) { BaseColorFactor = new(.8f,.25f,.08f,1),MetallicFactor = .7f,RoughnessFactor = .35f,
            BaseColorTexture=new(new(checker,new())) { Transform=Matrix3x2.CreateScale(4) } };
        var draw = new ModelDrawItem(geometry,material,Matrix4x4.Identity);
        var draws = new ModelDrawList(); var moving = draws.Add(draw);
        var camera = ModelCamera.Perspective(new(3,2,5),Vector3.Zero,Vector3.UnitY,MathF.PI/3,.1f,100);
        var skyBytes = new byte[32 * 16 * 8];
        for (int y = 0; y < 16; y++)
        {
            var sky = new Vector4(Vector3.Lerp(new(.12f,.08f,.04f), new(1.2f,1.6f,2), (1 + MathF.Cos((y + .5f) / 16 * MathF.PI)) / 2), 1);
            for (int x = 0; x < 32; x++)
            { for (int c = 0; c < 4; c++) { BitConverter.TryWriteBytes(skyBytes.AsSpan((y * 32 + x) * 8 + c * 2, 2), (Half)sky[c]); } }
        }
        var skyImage = new GpuImageUploadData(new("sky",0),new(32,16,GpuFormat.Rgba16Float),GpuImageColorEncoding.Linear,GpuImageAlphaMode.Opaque,
            [new(0,0,32*8,(ulong)skyBytes.Length,skyBytes)]);
        var lighting = new ModelLighting([ModelLight.Directional(Vector3.Normalize(new(-1,-2,-3)),Vector3.One,4),
            ModelLight.Directional(Vector3.Normalize(new(1,0,1)),new(.3f,.5f,1),1)],new(skyImage,Quaternion.Identity,.5f));
        GpuGraphTextureDescription description;
        using (var first = await context.BeginFrameAsync(stop)) { description = first.TargetResource.Description; }
        await presentation.WaitForPresentationAsync();
        var compiled = Build(description);
        for (int frame=0;frame<frameLimit && !stop.IsCancellationRequested;frame++)
        {
            draws.Set(moving,draw with { LocalToWorld = Matrix4x4.CreateRotationY(frame*.01f) });
            var bindings = compiled.Plan.CreateBindings(); bindings.Set(compiled.Input,new(camera,draws.Snapshot(),lighting));
            try
            {
                using var execution = await context.SubmitAsync(compiled.Plan,bindings.Build(),compiled.Target,stop);
                await presentation.WaitForPresentationAsync();
            }
            catch (GpuPresentationTargetChangedException changed) { compiled = Build(changed.Description); }
        }
        static (GpuRenderGraphPlan Plan,GpuGraphInput<ModelRenderSnapshot> Input,GpuGraphTextureInput Target) Build(GpuGraphTextureDescription targetDescription)
        {
            var graph = new GpuRenderGraph(); var target = graph.CreateTextureInput("screen",targetDescription);
            var input = graph.CreateInput("model",ModelRenderInputContract.Instance);
            var color = graph.CreateTexture("hdr",new(targetDescription.Width,targetDescription.Height,GpuFormat.Rgba16Float));
            var depth = graph.CreateTexture("depth",new(targetDescription.Width,targetDescription.Height,GpuFormat.D32Float));
            graph.AddModelPass("model",new(input,color,depth) { ClearColor = new(.015f,.02f,.035f,1) });
            var mapped = graph.AddToneMapPass("tone map",new(color));
            graph.AddOutputPass("screen",new(mapped.Color,target.Texture)); graph.MarkOutput(target.Texture);
            return (graph.Compile(),input,target);
        }
    }
}
