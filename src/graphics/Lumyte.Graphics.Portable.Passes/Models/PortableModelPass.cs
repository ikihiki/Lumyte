using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.ModelPreparation;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.Portable.Resources;
using Lumyte.Graphics.Portable.Shaders;
using Preparation = Lumyte.Graphics.ModelPreparation.ModelPreparation;

namespace Lumyte.Graphics.Portable.Passes;

public static class PortableModelRendering
{
    public static PortableRenderPassRegistry AddModelRendering(this PortableRenderPassRegistry registry)
        => registry.AddModelRendering(PortableModelShaders.CreatePackage());
    public static PortableRenderPassRegistry AddModelRendering(this PortableRenderPassRegistry registry, PortableShaderPackage shaders)
    { registry.Register(ModelPassContract.Instance, services => new PortableModelPass(services, shaders)); return registry; }
}
public static class PortableModelShaders
{
    public static PortableShaderPackage CreatePackage()
    {
        using var stream = typeof(PortableModelShaders).Assembly.GetManifestResourceStream("Lumyte.Graphics.Portable.Passes.Models.Model.wgsl")!;
        using var reader = new StreamReader(stream);
        return new(1, reader.ReadToEnd(), [new(GpuShaderStage.Vertex,"vertex"),new(GpuShaderStage.Pixel,"fragment")],
            PortableShaderFeatures.ImmediateAddressSpace,
            [new([new(0,GpuShaderStage.Vertex,new GpuBufferBindingLayout(GpuBufferBindingType.ReadOnlyStorage)),
                new(1,GpuShaderStage.Vertex | GpuShaderStage.Pixel,new GpuBufferBindingLayout(GpuBufferBindingType.ReadOnlyStorage)),
                .. Enumerable.Range(2,8).Select(binding => new GpuBindingLayoutEntry((uint)binding,GpuShaderStage.Pixel,new GpuTextureBindingLayout(GpuTextureSampleType.UnfilterableFloat)))])],
            new("Root",8,4,[new("offset","u32",0,4,4),new("reserved","u32",4,4,4)]),[],new([]),"Lumyte.Portable.Model.v1");
    }
}
internal sealed class PortableModelPass(PortablePassServices services, PortableShaderPackage shaders) : IPortableRenderPass<ModelPassRequest, ModelPassResult>
{
    private readonly Dictionary<GeometryKey, Geometry> geometries = [];
    private readonly Dictionary<ModelImageKey, PortablePassContentGeneration<GpuTextureRef>> images = [];
    private readonly PortableFilterPass imageFilter = new(services);
    private readonly Dictionary<(bool Blend, bool DoubleSided, bool Reflected), GpuRasterPipelineHandle> pipelines = [];
    private PortableShaderProgram? program;
    private int sequence;
    private readonly PortableModelEnvironment environmentImages = new(services);
    public async ValueTask BuildAsync(PortablePassBuildContext context, ModelPassRequest request, ModelPassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); sequence = 0;
        var snapshot = context.GetInput(request.Data);
        var color = context.ImportTexture(request.Color); var depth = context.ImportTexture(request.Depth);
        var cv = context.CreateView("model color", color); var dv = context.CreateView("model depth", depth);
        context.AddPass(Name("clear"), (cv,dv,request.ClearColor,request.ClearDepth), static (record,s) =>
        {
            Vector4 c = s.ClearColor;
            record.Commands.BeginRendering([new(record.GetTextureView(s.cv), GpuAttachmentLoadOperation.Clear, ClearColor: new(c.X*c.W,c.Y*c.W,c.Z*c.W,c.W))],
                new(record.GetTextureView(s.dv), DepthLoadOperation: GpuAttachmentLoadOperation.Clear, DepthStoreOperation: GpuAttachmentStoreOperation.Store, ClearValue: new(s.ClearDepth,0)));
            record.Commands.EndRendering();
        }).Write(color, PortablePassUsage.ColorAttachment).Write(depth, PortablePassUsage.DepthStencilAttachment);
        List<(ModelDrawItem Draw, Geometry Geometry, float Depth)> draws = [];
        foreach (var draw in snapshot.Draws.Items)
        {
            cancellationToken.ThrowIfCancellationRequested(); if (!draw.Visible) { continue; }
            var key = new GeometryKey(draw.Geometry, draw.Deformation, draw.Range, ModelUvLayout.From(draw.Material));
            if (!geometries.TryGetValue(key, out var geometry)) { geometry = new(Preparation.Prepare(key)); geometries.Add(key, geometry); }
            if (geometry.Data.VertexCount != 0) { draws.Add((draw, geometry, draw.Material.AlphaMode == ModelAlphaMode.Blend ? Preparation.Depth(geometry.Data, draw.LocalToWorld, snapshot.Camera.View) : 0)); }
        }
        PortablePassTexture[] environment = [];
        if (snapshot.Lighting.Environment is { } lighting && draws.Count != 0)
        {
            var source = await ImageAsync(context, new(lighting.Image, true), cancellationToken);
            environment = environmentImages.Prepare(context, source, lighting.Image);
        }
        foreach (var entry in draws.OrderBy(x => x.Draw.Material.AlphaMode == ModelAlphaMode.Blend).ThenBy(x => x.Depth))
        {
            var draw = entry.Draw; var geometry = entry.Geometry;
            if (!context.TryUseContent(geometry.Generation, out GpuBufferRef buffer))
            {
                geometry.Generation?.Dispose();
                geometry.Generation = await Upload(context, geometry.Data.Vertices, cancellationToken);
                _ = context.TryUseContent(geometry.Generation, out buffer);
            }
            var vertices = context.ImportBuffer(buffer);
            using var parameters = await Upload(context, Preparation.Parameters(draw, snapshot, (float)color.Description.Width / color.Description.Height), cancellationToken);
            _ = context.TryUseContent(parameters, out var parameterBuffer);
            var parameterData = context.ImportBuffer(parameterBuffer);
            bool blend = draw.Material.AlphaMode == ModelAlphaMode.Blend;
            var pipeline = Pipeline(blend, draw.Material.DoubleSided, draw.LocalToWorld.GetDeterminant() < 0);
            var textures = new List<PortablePassTexture>(); int slot = 0;
            foreach (var texture in draw.Material.Textures)
            { textures.Add(await ImageAsync(context, new(texture?.Texture.Image ?? ModelImages.White, slot++ is 0 or 4), cancellationToken)); }
            if (environment.Length != 0) { textures.AddRange(environment); }
            else { textures.AddRange(Enumerable.Repeat(textures[0], 3)); }
            var views = textures.Select(t => context.CreateView(Name("image"), t)).ToArray();
            var bindings = context.CreateBindings(Name("bindings"), program!, 0, new Inputs(vertices,parameterData,views));
            var state = (cv,dv,pipeline,bindings,geometry.Data.VertexCount);
            var pass = context.AddPass(Name("draw"), state, static (record,s) =>
            {
                record.Commands.BeginRendering([new(record.GetTextureView(s.cv), GpuAttachmentLoadOperation.Load)],
                    new(record.GetTextureView(s.dv), DepthLoadOperation: GpuAttachmentLoadOperation.Load, DepthStoreOperation: GpuAttachmentStoreOperation.Store));
                record.Commands.SetPipeline(s.pipeline); record.Commands.SetBindings(0,record.GetBindings(s.bindings));
                Root root = default; record.Commands.SetRootData(in root); record.Commands.Draw(s.VertexCount); record.Commands.EndRendering();
            }).Read(vertices,PortablePassUsage.StorageRead).Read(parameterData,PortablePassUsage.StorageRead)
                .ReadWrite(color,PortablePassUsage.ColorAttachment).ReadWrite(depth,PortablePassUsage.DepthStencilAttachment);
            foreach (var texture in textures.Distinct()) { pass.Read(texture, PortablePassUsage.SampledRead); }
        }
        while (geometries.Count > 1024) { var key = geometries.Keys.First(); geometries[key].Generation?.Dispose(); geometries.Remove(key); }
        while (images.Count > 256) { var key = images.Keys.First(); images[key].Dispose(); images.Remove(key); }
    }
    private async ValueTask<PortablePassTexture> ImageAsync(PortablePassBuildContext context, ModelImageKey key, CancellationToken cancellationToken)
    {
        images.TryGetValue(key, out var generation);
        if (context.TryUseContent(generation, out GpuTextureRef texture)) { return context.ImportTexture(texture); }
        generation?.Dispose(); var prepared = ModelImages.Prepare(key); var first = prepared.Levels[0];
        using var scope = services.Resources.CreateScope();
        texture = scope.CreateTexture(new(GpuTextureDimension.Texture2D, first.Width, first.Height, 1, (uint)prepared.Levels.Length, 1, 1,
            GpuFormat.Rgba16Float, GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination));
        var destination = context.ImportTexture(texture); List<PortablePassBuilder> writers = [];
        for (int mip = 0; mip < prepared.Levels.Length; mip++)
        {
            var level = prepared.Levels[mip];
            if(level.Bytes.Length==0) { continue; }
            var staging = scope.CreateBuffer(new((ulong)level.Bytes.Length,GpuBufferUsage.MapWrite | GpuBufferUsage.CopySource));
            using (var mapped = await services.Resources.MapBufferAsync(staging,GpuMapMode.Write,cancellationToken:cancellationToken)) { level.Bytes.CopyTo(mapped.Memory.Span); }
            var source = context.ImportBuffer(staging);
            var footprint = new GpuTextureCopyFootprint((uint)mip,GpuTextureAspect.All,default,new(level.Width,level.Height,1),level.Pitch,(ulong)level.Bytes.Length);
            writers.Add(context.AddPass(Name("image upload"),(source,destination,footprint,Length:(ulong)level.Bytes.Length),static (record,s) =>
                record.Commands.CopyBufferToTexture(record.GetBufferRange(s.source,0,s.Length),record.GetTexture(s.destination),s.footprint))
                .Read(source,PortablePassUsage.CopySource).ReadWrite(destination,PortablePassUsage.CopyDestination));
        }
        var previous=destination;uint previousMip=0;
        List<(PortablePassTexture Source,GpuTextureCopyFootprint SourceFootprint,GpuTextureCopyFootprint TargetFootprint)> copies=[];
        for(uint mip=1;mip<prepared.Levels.Length;mip++)
        {
            var level=prepared.Levels[mip];
            if(level.Bytes.Length!=0) { previous=destination;previousMip=mip;continue; }
            var sourceView=context.CreateView(Name("mip source"),previous,new(BaseMip:previousMip,MipCount:1));
            var target=context.CreateTexture(Name("mip"),new(GpuTextureDimension.Texture2D,level.Width,level.Height,1,1,1,1,GpuFormat.Rgba16Float,
                GpuTextureUsage.Sampled|GpuTextureUsage.ColorAttachment|GpuTextureUsage.CopySource));
            imageFilter.GenerateMip(context,Name("mip filter"),previous,sourceView,target);
            var footprint=new GpuTextureCopyFootprint(0,GpuTextureAspect.All,default,new(level.Width,level.Height,1));
            copies.Add((target,footprint,footprint with { Mip=mip }));previous=target;previousMip=0;
        }
        if(copies.Count!=0)
        {
            var copy=context.AddPass(Name("mip store"),(destination,Copies:copies.ToArray()),static (record,s) =>
            {
                foreach(var item in s.Copies) { record.Commands.CopyTexture(record.GetTexture(item.Source),item.SourceFootprint,record.GetTexture(s.destination),item.TargetFootprint); }
            }).ReadWrite(destination,PortablePassUsage.CopyDestination);
            foreach(var item in copies) { copy.Read(item.Source,PortablePassUsage.CopySource); }
            writers.Add(copy);
        }
        generation = context.RegisterContent(texture,services.Resources.Pin(texture),writers.ToArray()); images[key] = generation;
        _ = context.TryUseContent(generation,out texture); return destination;
    }
    private async ValueTask<PortablePassContentGeneration<GpuBufferRef>> Upload(PortablePassBuildContext context, Vector4[] values, CancellationToken cancellationToken)
    {
        byte[] bytes = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
        using var scope = services.Resources.CreateScope();
        var stagingRef = scope.CreateBuffer(new((ulong)bytes.Length,GpuBufferUsage.MapWrite | GpuBufferUsage.CopySource));
        using (var mapped = await services.Resources.MapBufferAsync(stagingRef,GpuMapMode.Write,cancellationToken:cancellationToken)) { bytes.CopyTo(mapped.Memory.Span); }
        var buffer = scope.CreateBuffer(new((ulong)bytes.Length,GpuBufferUsage.Storage | GpuBufferUsage.CopyDestination));
        var staging = context.ImportBuffer(stagingRef); var destination = context.ImportBuffer(buffer);
        var writer = context.AddPass(Name("upload"),(staging,destination,Length:(ulong)bytes.Length),static (record,s) =>
            record.Commands.CopyBuffer(record.GetBufferRange(s.staging,0,s.Length),record.GetBufferRange(s.destination,0,s.Length)))
            .Read(staging,PortablePassUsage.CopySource).Write(destination,PortablePassUsage.CopyDestination);
        return context.RegisterContent(buffer,services.Resources.Pin(buffer),[writer]);
    }
    private GpuRasterPipelineHandle Pipeline(bool blend, bool doubleSided, bool reflected)
    {
        var key = (blend,doubleSided,reflected); if (pipelines.TryGetValue(key,out var pipeline)) { return pipeline; }
        program ??= services.ShaderLoader.Load(shaders);
        GpuBlendDescription? blending = blend ? new(DestinationColorFactor:GpuBlendFactor.OneMinusSourceAlpha,DestinationAlphaFactor:GpuBlendFactor.OneMinusSourceAlpha) : null;
        pipeline = services.Backend.CreateRasterPipeline(new([new(GpuFormat.Rgba16Float,Blend:blending)],GpuFormat.D32Float)
        { DepthStencil = new(DepthTest:true,DepthWrite:!blend), CullMode = doubleSided ? GpuCullMode.None : GpuCullMode.Back,
            FrontFace = reflected ? GpuFrontFace.Clockwise : GpuFrontFace.CounterClockwise },program.Description);
        pipelines.Add(key,pipeline); return pipeline;
    }
    private string Name(string name) => $"model {name} {sequence++}";
    public ValueTask DisposeAsync()
    {
        foreach (var geometry in geometries.Values) { geometry.Generation?.Dispose(); }
        foreach (var image in images.Values) { image.Dispose(); }
        foreach (var pipeline in pipelines.Values) { services.Backend.DestroyRasterPipeline(pipeline); }
        geometries.Clear(); images.Clear(); pipelines.Clear(); program?.Dispose(); environmentImages.Dispose(); return imageFilter.DisposeAsync();
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Root(uint Offset,uint Reserved);
    private sealed class Geometry(PreparedGeometry data) { internal PreparedGeometry Data { get; } = data; internal PortablePassContentGeneration<GpuBufferRef>? Generation { get; set; } }
    private sealed class Inputs(PortablePassBuffer vertices,PortablePassBuffer parameters,PortablePassView[] textures) : IPortablePassBindingInputs
    { public void Write(PortablePassBindingWriter writer) { writer.Buffer(0,vertices); writer.Buffer(1,parameters); for (int i=0;i<textures.Length;i++) { writer.Texture((uint)i+2,textures[i]); } } }
}
