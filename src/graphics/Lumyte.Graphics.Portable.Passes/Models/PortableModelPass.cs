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
                new(1,GpuShaderStage.Vertex | GpuShaderStage.Pixel,new GpuBufferBindingLayout(GpuBufferBindingType.ReadOnlyStorage))])],
            new("Root",8,4,[new("offset","u32",0,4,4),new("reserved","u32",4,4,4)]),[],new([]),"Lumyte.Portable.Model.v1");
    }
}
internal sealed class PortableModelPass(PortablePassServices services, PortableShaderPackage shaders) : IPortableRenderPass<ModelPassRequest, ModelPassResult>
{
    private readonly Dictionary<GeometryKey, Geometry> geometries = [];
    private readonly Dictionary<(bool Blend, bool DoubleSided, bool Reflected), GpuRasterPipelineHandle> pipelines = [];
    private PortableShaderProgram? program;
    private int sequence;
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
            var key = new GeometryKey(draw.Geometry, draw.Deformation, draw.Range);
            if (!geometries.TryGetValue(key, out var geometry)) { geometry = new(Preparation.Prepare(key)); geometries.Add(key, geometry); }
            if (geometry.Data.VertexCount != 0) { draws.Add((draw, geometry, draw.Material.AlphaMode == ModelAlphaMode.Blend ? Preparation.Depth(geometry.Data, draw.LocalToWorld, snapshot.Camera.View) : 0)); }
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
            var bindings = context.CreateBindings(Name("bindings"), program!, 0, new Inputs(vertices,parameterData));
            var state = (cv,dv,pipeline,bindings,geometry.Data.VertexCount);
            context.AddPass(Name("draw"), state, static (record,s) =>
            {
                record.Commands.BeginRendering([new(record.GetTextureView(s.cv), GpuAttachmentLoadOperation.Load)],
                    new(record.GetTextureView(s.dv), DepthLoadOperation: GpuAttachmentLoadOperation.Load, DepthStoreOperation: GpuAttachmentStoreOperation.Store));
                record.Commands.SetPipeline(s.pipeline); record.Commands.SetBindings(0,record.GetBindings(s.bindings));
                Root root = default; record.Commands.SetRootData(in root); record.Commands.Draw(s.VertexCount); record.Commands.EndRendering();
            }).Read(vertices,PortablePassUsage.StorageRead).Read(parameterData,PortablePassUsage.StorageRead)
                .ReadWrite(color,PortablePassUsage.ColorAttachment).ReadWrite(depth,PortablePassUsage.DepthStencilAttachment);
        }
        while (geometries.Count > 1024) { var key = geometries.Keys.First(); geometries[key].Generation?.Dispose(); geometries.Remove(key); }
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
        foreach (var pipeline in pipelines.Values) { services.Backend.DestroyRasterPipeline(pipeline); }
        geometries.Clear(); pipelines.Clear(); program?.Dispose(); return ValueTask.CompletedTask;
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Root(uint Offset,uint Reserved);
    private sealed class Geometry(PreparedGeometry data) { internal PreparedGeometry Data { get; } = data; internal PortablePassContentGeneration<GpuBufferRef>? Generation { get; set; } }
    private sealed class Inputs(PortablePassBuffer vertices,PortablePassBuffer parameters) : IPortablePassBindingInputs
    { public void Write(PortablePassBindingWriter writer) { writer.Buffer(0,vertices); writer.Buffer(1,parameters); } }
}
