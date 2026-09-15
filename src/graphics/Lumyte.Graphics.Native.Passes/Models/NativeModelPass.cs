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
    private readonly Dictionary<(bool Blend, bool DoubleSided, bool Reflected), NativeGpuRasterPipelineHandle> pipelines = [];
    public ValueTask BuildAsync(NativePassBuildContext context, ModelPassRequest request, ModelPassResult result, CancellationToken cancellationToken)
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
            var key = new GeometryKey(draw.Geometry, draw.Deformation, draw.Range);
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
            using var parameters = Upload(Preparation.Parameters(draw, snapshot, (float)color.Description.Width / color.Description.Height));
            var vb = context.ImportBuffer(vertices.Buffer); var pb = context.ImportBuffer(parameters.Buffer);
            context.Retain(services.Resources.AcquireUse(vertices.View)); context.Retain(services.Resources.AcquireUse(parameters.View));
            bool blend = draw.Material.AlphaMode == ModelAlphaMode.Blend;
            var state = (Pipeline: Pipeline(blend, draw.Material.DoubleSided, draw.LocalToWorld.GetDeterminant() < 0), cv, dv,
                Geometry: services.Resources.GetShaderIndex(vertices.View), Parameters: services.Resources.GetShaderIndex(parameters.View), geometry.Data.VertexCount, blend);
            context.AddPass($"model draw {sequence++}", state, (record, s) =>
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
        }
        // Imported uses hold buffers through completion even when an entry is evicted now.
        while (geometries.Count > 1024) { var key = geometries.Keys.First(); geometries[key].Buffer?.Dispose(); geometries.Remove(key); }
        return ValueTask.CompletedTask;
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
        foreach (var pipeline in pipelines.Values) { services.Backend.DestroyRasterPipeline(pipeline); }
        geometries.Clear(); pipelines.Clear(); return ValueTask.CompletedTask;
    }
    private sealed record Geometry(PreparedGeometry Data, CachedBuffer? Buffer);
    private sealed record CachedBuffer(GpuResourceScope Scope, GpuBufferRef Buffer, GpuViewRef View) : IDisposable
    { public void Dispose() => Scope.Dispose(); }
}
