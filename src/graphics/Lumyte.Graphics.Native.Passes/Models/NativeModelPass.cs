using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.ModelPreparation;
using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.Native.Shaders;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Native.Passes.Models.Generated;
using DxRoot = Lumyte.Graphics.Native.Passes.Models.Generated.DirectX12.Root;
using VkRoot = Lumyte.Graphics.Native.Passes.Models.Generated.Vulkan.Root;
using Preparation = Lumyte.Graphics.ModelPreparation.ModelPreparation;

namespace Lumyte.Graphics.Native.Passes;

public static class NativeModelRendering
{
    public static NativeRenderPassRegistry AddModelRendering(this NativeRenderPassRegistry registry)
    { registry.Register(ModelPassContract.Instance, static services => new NativeModelPass(services)); return registry; }
}
internal sealed class NativeModelPass(NativePassServices services) : INativeRenderPass<ModelPassRequest, ModelPassResult>
{
    private readonly Dictionary<GeometryKey, Geometry> geometries = [];
    private readonly Dictionary<ModelImageKey, CachedImage> images = [];
    private readonly NativeFilterPass imageFilter = new(services);
    private int imageSequence;
    private readonly Dictionary<(bool Blend, bool DoubleSided, bool Reflected), NativeGpuRasterPipelineHandle> pipelines = [];
    public async ValueTask BuildAsync(NativePassBuildContext context, ModelPassRequest request, ModelPassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = context.GetInput(request.Data);
        var color = context.ImportTexture(request.Color); var depth = context.ImportTexture(request.Depth);
        var cv = context.CreateView("model color", color, new(NativeGpuTextureViewDimension.TwoD, GpuFormat.Rgba16Float,
            NativeGpuTextureAspect.Color, 0, 1, 0, 1, GpuTextureViewPurpose.Attachment));
        var dv = context.CreateView("model depth", depth, new(NativeGpuTextureViewDimension.TwoD, GpuFormat.D32Float,
            NativeGpuTextureAspect.Depth, 0, 1, 0, 1, GpuTextureViewPurpose.Attachment));
        context.AddPass("model clear", (cv, dv, request.ClearColor, request.ClearDepth), static (record, s) =>
        {
            Vector4 c = s.ClearColor;
            record.Commands.BeginRendering([new(record.GetRenderView(s.cv), NativeGpuLoadOp.Clear, ClearColor: new(c.X*c.W,c.Y*c.W,c.Z*c.W,c.W))],
                new(record.GetRenderView(s.dv), NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store, ClearDepth: s.ClearDepth));
            record.Commands.EndRendering();
        }).Write(color, new(GpuStage.ColorOutput, GpuAccess.ColorWrite)).Write(depth, new(GpuStage.DepthStencil, GpuAccess.DepthStencilWrite));
        List<(ModelDrawItem Draw, Geometry Geometry, float Depth)> draws = [];
        foreach (var draw in snapshot.Draws.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!draw.Visible) { continue; }
            var key = new GeometryKey(draw.Geometry, draw.Deformation, draw.Range, ModelUvLayout.From(draw.Material));
            if (!geometries.TryGetValue(key, out var geometry))
            {
                var prepared = Preparation.Prepare(key);
                geometry = new(prepared, prepared.VertexCount == 0 ? null : Upload(prepared.Vertices)); geometries.Add(key, geometry);
            }
            if (geometry.Data.VertexCount != 0) { draws.Add((draw, geometry, draw.Material.AlphaMode == ModelAlphaMode.Blend ? Preparation.Depth(geometry.Data, draw.LocalToWorld, snapshot.Camera.View) : 0)); }
        }
        int sequence = 0;
        foreach (var entry in draws.OrderBy(x => x.Draw.Material.AlphaMode == ModelAlphaMode.Blend).ThenBy(x => x.Depth))
        {
            var draw = entry.Draw; var geometry = entry.Geometry; var vertices = geometry.Buffer!;
            var data = Preparation.Parameters(draw, snapshot, (float)color.Description.Width / color.Description.Height);
            List<NativePassTexture> textures = []; int slot = 0;
            foreach (var texture in draw.Material.Textures)
            {
                var key = new ModelImageKey(texture?.Texture.Image ?? ModelImages.White, slot is 0 or 4);
                var image = await ImageAsync(context, key, cancellationToken);
                textures.Add(context.ImportTexture(image.Texture, image.Description)); context.Retain(services.Resources.AcquireUse(image.View));
                data[17 + slot * 4].X = BitConverter.UInt32BitsToSingle(services.Resources.GetShaderIndex(image.View)); slot++;
            }
            using var parameters = Upload(data);
            var vb = context.ImportBuffer(vertices.Buffer); var pb = context.ImportBuffer(parameters.Buffer);
            context.Retain(services.Resources.AcquireUse(vertices.View)); context.Retain(services.Resources.AcquireUse(parameters.View));
            bool blend = draw.Material.AlphaMode == ModelAlphaMode.Blend;
            var state = (Pipeline: Pipeline(blend, draw.Material.DoubleSided, draw.LocalToWorld.GetDeterminant() < 0), cv, dv,
                Geometry: services.Resources.GetShaderIndex(vertices.View), Parameters: services.Resources.GetShaderIndex(parameters.View), geometry.Data.VertexCount, blend);
            var pass = context.AddPass($"model draw {sequence++}", state, (record, s) =>
            {
                record.Commands.Barrier(GpuStage.Host, GpuAccess.HostWrite, GpuStage.All, GpuAccess.ShaderRead);
                record.Commands.BeginRendering([new(record.GetRenderView(s.cv), NativeGpuLoadOp.Load)],
                    new(record.GetRenderView(s.dv), NativeGpuLoadOp.Load, NativeGpuStoreOp.Store));
                record.Commands.SetPipeline(s.Pipeline);
                record.Commands.SetDepthStencilState(new(DepthTest: true, DepthWrite: !s.blend));
                if (services.Backend.ShaderCodeFormat == GpuShaderCodeFormat.Dxil)
                { DxRoot root = new() { geometry = s.Geometry, parameters = s.Parameters }; record.Commands.Draw(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref root, 1)), s.VertexCount); }
                else
                { VkRoot root = new() { geometry = s.Geometry, parameters = s.Parameters }; record.Commands.Draw(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref root, 1)), s.VertexCount); }
                record.Commands.EndRendering();
            }).Read(vb, new(GpuStage.All, GpuAccess.ShaderRead)).Read(pb, new(GpuStage.All, GpuAccess.ShaderRead))
                .ReadWrite(color, new(GpuStage.ColorOutput, GpuAccess.ColorRead | GpuAccess.ColorWrite)).ReadWrite(depth, new(GpuStage.DepthStencil, GpuAccess.DepthStencilRead | GpuAccess.DepthStencilWrite));
            foreach (var texture in textures.Distinct()) { pass.Read(texture, new(GpuStage.PixelShader, GpuAccess.ShaderRead)); }
        }
        // Imported uses hold buffers through completion even when an entry is evicted now.
        while (geometries.Count > 1024) { var key = geometries.Keys.First(); geometries[key].Buffer?.Dispose(); geometries.Remove(key); }
        while (images.Count > 256) { var key = images.Keys.First(); images[key].Dispose(); images.Remove(key); }
    }
    private async ValueTask<CachedImage> ImageAsync(NativePassBuildContext context, ModelImageKey key, CancellationToken cancellationToken)
    {
        if (images.TryGetValue(key, out var existing))
        {
            if (existing.Generation is null || context.TryUseContent(existing.Generation,out GpuTextureRef _)) { return existing; }
            existing.Dispose(); images.Remove(key);
        }
        var prepared = ModelImages.Prepare(key); var first = prepared.Levels[0]; var scope = services.Resources.CreateScope();
        try
        {
            var description = new NativeGpuTextureDescription(NativeGpuTextureDimension.TwoD, first.Width, first.Height, 1, (uint)prepared.Levels.Length, 1, 1,
                GpuFormat.Rgba16Float, NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.CopyDestination);
            var texture = scope.CreateTexture(description);
            var view = scope.GetView(texture, new GpuTextureViewDescription(NativeGpuTextureViewDimension.TwoD, GpuFormat.Rgba16Float,
                NativeGpuTextureAspect.Color, 0, (uint)prepared.Levels.Length, 0, 1));
            using(var initialize=services.Resources.BeginBatch())
            {
                using var staging=services.Resources.CreateScope();
                initialize.Use(texture);
                List<(NativeGpuRange Range,NativeGpuTextureCopyFootprint Footprint)> uploads=[];
                for (int mip = 0; mip < prepared.Levels.Length; mip++)
                {
                    var level = prepared.Levels[mip];
                    if (level.Bytes.Length == 0) { continue; }
                    var buffer=staging.CreateBuffer(new((ulong)level.Bytes.Length,NativeGpuMemoryKind.CpuVisible,512));
                    initialize.Use(buffer);var range=services.Resources.GetBufferRange(buffer);
                    Marshal.Copy(level.Bytes,0,range.Region.CpuAddress+checked((nint)range.Offset),level.Bytes.Length);
                    uploads.Add((range,new((uint)mip,NativeGpuTextureAspect.Color,0,1,default,new(level.Width,level.Height,1),level.Pitch,(ulong)level.Bytes.Length)));
                }
                var commands=initialize.StartCommandRecording();
                bool transitions=services.Backend.Capabilities.ExplicitTextureTransitions;
                commands.DiscardTexture(services.Resources.GetTextureView(view),transitions ? GpuTextureLayout.CopyDestination : GpuTextureLayout.General);
                commands.Barrier(GpuStage.Host,GpuAccess.HostWrite,GpuStage.Copy,GpuAccess.CopyRead);
                foreach(var upload in uploads) { commands.CopyMemoryToTexture(upload.Range,services.Resources.GetTextureHandle(texture),upload.Footprint); }
                if(transitions) { commands.TextureTransition(services.Resources.GetTextureView(view),GpuTextureLayout.CopyDestination,GpuTextureLayout.General); }
                commands.Barrier(GpuStage.Copy,GpuAccess.CopyWrite,GpuStage.All,GpuAccess.ShaderRead);
                await initialize.Submit().WaitAsync(cancellationToken);
            }
            var destination=context.ImportTexture(texture,description);
            var previous=destination; uint previousMip=0;
            List<(NativePassBuffer Buffer,NativeGpuTextureCopyFootprint Footprint)> copies=[];
            for(uint mip=1;mip<prepared.Levels.Length;mip++)
            {
                var level=prepared.Levels[mip];
                if(level.Bytes.Length!=0) { previous=destination;previousMip=mip;continue; }
                string name=$"model mip {imageSequence++}";
                var sourceView=context.CreateView(name+" source",previous,new(NativeGpuTextureViewDimension.TwoD,GpuFormat.Rgba16Float,NativeGpuTextureAspect.Color,previousMip,1,0,1));
                var target=context.CreateTexture(name,new(NativeGpuTextureDimension.TwoD,level.Width,level.Height,1,1,1,1,GpuFormat.Rgba16Float,
                    NativeGpuTextureUsage.Sampled|NativeGpuTextureUsage.ColorAttachment|NativeGpuTextureUsage.CopySource));
                imageFilter.GenerateMip(context,name+" filter",previous,sourceView,target);
                ulong pitch=(level.Width*8ul+255)&~255ul;
                var scratch=context.CreateBuffer(name+" copy",new(pitch*level.Height,Alignment:512));
                var footprint=new NativeGpuTextureCopyFootprint(0,NativeGpuTextureAspect.Color,0,1,default,new(level.Width,level.Height,1),pitch,pitch*level.Height);
                context.AddPass(name+" read",(target,scratch,footprint),static (record,s) =>
                    record.Commands.CopyTextureToMemory(record.GetTexture(s.target),record.GetBufferRange(s.scratch),s.footprint))
                    .Read(target,new(GpuStage.Copy,GpuAccess.CopyRead)).Write(scratch,new(GpuStage.Copy,GpuAccess.CopyWrite));
                copies.Add((scratch,footprint with { Mip=mip }));previous=target;previousMip=0;
            }
            NativePassContentGeneration<GpuTextureRef>? generation=null;
            if(copies.Count!=0)
            {
                var copy=context.AddPass($"model mip store {imageSequence++}",(destination,Copies:copies.ToArray()),static (record,s) =>
                {
                    foreach(var item in s.Copies) { record.Commands.CopyMemoryToTexture(record.GetBufferRange(item.Buffer),record.GetTexture(s.destination),item.Footprint); }
                }).ReadWrite(destination,new(GpuStage.Copy,GpuAccess.CopyWrite));
                foreach(var item in copies) { copy.Read(item.Buffer,new(GpuStage.Copy,GpuAccess.CopyRead)); }
                generation=context.RegisterContent(texture,services.Resources.AcquireUse(texture),copy);
                _=context.TryUseContent(generation,out GpuTextureRef _);
            }
            var result = new CachedImage(scope, texture, view, description,generation); images.Add(key, result); return result;
        }
        catch { scope.Dispose(); throw; }
    }
    private CachedBuffer Upload(Vector4[] values)
    {
        byte[] bytes = MemoryMarshal.AsBytes(values.AsSpan()).ToArray(); var scope = services.Resources.CreateScope();
        try
        {
            var buffer = scope.CreateBuffer(new((ulong)bytes.Length, NativeGpuMemoryKind.CpuVisible, 16));
            var range = services.Resources.GetBufferRange(buffer);
            Marshal.Copy(bytes, 0, range.Region.CpuAddress + checked((nint)range.Offset), bytes.Length);
            var view = scope.GetView(buffer, new GpuBufferViewDescription(0, (ulong)bytes.Length));
            return new(scope, buffer, view);
        }
        catch { scope.Dispose(); throw; }
    }
    private NativeGpuRasterPipelineHandle Pipeline(bool blend, bool doubleSided, bool reflected)
    {
        var key = (blend, doubleSided, reflected);
        if (pipelines.TryGetValue(key, out var pipeline)) { return pipeline; }
        using var program = services.ShaderLoader.Load(ShaderPackage.Create(), services.Backend.ShaderCodeFormat == GpuShaderCodeFormat.Dxil ? DxRoot.AbiHash : VkRoot.AbiHash);
        pipeline = services.Backend.CreateRasterPipeline(new()
        {
            ColorTargets = [new(GpuFormat.Rgba16Float, Blend: new(Enabled: blend, DestinationColorFactor: NativeGpuBlendFactor.OneMinusSourceAlpha, DestinationAlphaFactor: NativeGpuBlendFactor.OneMinusSourceAlpha))],
            DepthStencilFormat = GpuFormat.D32Float, CullMode = doubleSided ? NativeGpuCullMode.None : NativeGpuCullMode.Back,
            FrontFace = reflected ? NativeGpuFrontFace.Clockwise : NativeGpuFrontFace.CounterClockwise,
        }, program.Code);
        pipelines.Add(key, pipeline); return pipeline;
    }
    public ValueTask DisposeAsync()
    {
        foreach (var geometry in geometries.Values) { geometry.Buffer?.Dispose(); }
        foreach (var image in images.Values) { image.Dispose(); }
        foreach (var pipeline in pipelines.Values) { services.Backend.DestroyRasterPipeline(pipeline); }
        geometries.Clear(); images.Clear(); pipelines.Clear(); return imageFilter.DisposeAsync();
    }
    private sealed record Geometry(PreparedGeometry Data, CachedBuffer? Buffer);
    private sealed record CachedImage(GpuResourceScope Scope, GpuTextureRef Texture, GpuViewRef View, NativeGpuTextureDescription Description,
        NativePassContentGeneration<GpuTextureRef>? Generation) : IDisposable
    { public void Dispose() { Generation?.Dispose();Scope.Dispose(); } }
    private sealed record CachedBuffer(GpuResourceScope Scope, GpuBufferRef Buffer, GpuViewRef View) : IDisposable
    { public void Dispose() => Scope.Dispose(); }
}
