using System.Runtime.InteropServices.JavaScript;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private sealed class PipelineState(WebGpuBackend owner, P.GpuShaderProgramDescription shaders,
        ShaderModuleResource[] modules, BindingLayoutResource[] layouts,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics)
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly P.GpuShaderProgramDescription Shaders = shaders;
        internal readonly ShaderModuleResource[] Modules = modules;
        internal readonly BindingLayoutResource[] Layouts = layouts;
        internal readonly uint ImmediateSize = shaders.ImmediateSize;
        internal Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal JSObject? Handle;
        internal JSObject? Layout;
        internal bool Destroyed;
    }

    private sealed class ComputePipelineResource(PipelineState state) : P.GpuComputePipelineHandle
    { internal readonly PipelineState State = state; }
    private sealed class RasterPipelineResource(PipelineState state, P.GpuRasterPipelineDescription description) : P.GpuRasterPipelineHandle
    {
        internal readonly PipelineState State = state;
        internal readonly P.GpuRasterPipelineDescription Description = description;
    }

    public P.GpuComputePipelineHandle CreateComputePipeline(P.GpuShaderProgramDescription shaders)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        RequireAvailable();
        if (shaders.EntryPoints.Count != 1 || shaders.EntryPoints[0].Stage != P.GpuShaderStage.Compute)
        { throw new ArgumentException("A compute pipeline requires exactly one Compute entry point.", nameof(shaders)); }
        return new ComputePipelineResource(CreatePipelineState(shaders));
    }

    public P.GpuRasterPipelineHandle CreateRasterPipeline(P.GpuRasterPipelineDescription description, P.GpuShaderProgramDescription shaders)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(shaders);
        RequireAvailable();
        int vertices = shaders.EntryPoints.Count(static entry => entry.Stage == P.GpuShaderStage.Vertex);
        int pixels = shaders.EntryPoints.Count(static entry => entry.Stage == P.GpuShaderStage.Pixel);
        if (vertices != 1 || pixels > 1 || shaders.EntryPoints.Count != vertices + pixels)
        { throw new ArgumentException("A raster pipeline accepts one Vertex and at most one Pixel entry point.", nameof(shaders)); }
        if (pixels == 0 && description.ColorTargets.Count != 0)
        { throw new ArgumentException("Color target descriptions require a Pixel entry point.", nameof(shaders)); }
        return new RasterPipelineResource(CreatePipelineState(shaders), description);
    }

    private PipelineState CreatePipelineState(P.GpuShaderProgramDescription shaders)
    {
        var modules = new ShaderModuleResource[shaders.EntryPoints.Count];
        var layouts = new BindingLayoutResource[shaders.BindingLayouts.Count];
        var diagnostics = new List<Task<IReadOnlyList<P.GpuDiagnostic>>>();
        for (int index = 0; index < modules.Length; index++)
        {
            P.GpuShaderEntryPoint entry = shaders.EntryPoints[index];
            if (entry.Name is null) { throw new ArgumentException("Entry point names cannot be null.", nameof(shaders)); }
            modules[index] = RequireShaderModule(entry.Module);
            diagnostics.Add(modules[index].Diagnostics);
        }
        for (int index = 0; index < layouts.Length; index++)
        {
            layouts[index] = RequireBindingLayout(shaders.BindingLayouts[index]);
            diagnostics.Add(layouts[index].Diagnostics);
        }
        return new(this, shaders, modules, layouts, BrowserDiagnostics.CombineAsync(diagnostics.ToArray()));
    }

    public void DestroyComputePipeline(P.GpuComputePipelineHandle pipeline) => DestroyPipeline(RequireComputePipeline(pipeline).State);
    public void DestroyRasterPipeline(P.GpuRasterPipelineHandle pipeline) => DestroyPipeline(RequireRasterPipeline(pipeline).State);

    private void DestroyPipeline(PipelineState state)
    {
        runtime.RequireThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        state.Destroyed = true;
        state.Handle?.Dispose();
        state.Layout?.Dispose();
    }

    private ComputePipelineResource RequireComputePipeline(P.GpuComputePipelineHandle pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not ComputePipelineResource resource || !ReferenceEquals(resource.State.Owner, this))
        { throw new ArgumentException("Compute pipeline belongs to another device.", nameof(pipeline)); }
        ObjectDisposedException.ThrowIf(resource.State.Destroyed, pipeline);
        return resource;
    }

    private RasterPipelineResource RequireRasterPipeline(P.GpuRasterPipelineHandle pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not RasterPipelineResource resource || !ReferenceEquals(resource.State.Owner, this))
        { throw new ArgumentException("Raster pipeline belongs to another device.", nameof(pipeline)); }
        ObjectDisposedException.ThrowIf(resource.State.Destroyed, pipeline);
        return resource;
    }

    private void EnsurePipeline(PipelineState state, P.GpuRasterPipelineDescription? raster)
    {
        ObjectDisposedException.ThrowIf(state.Destroyed, state);
        foreach (ShaderModuleResource module in state.Modules) { ObjectDisposedException.ThrowIf(module.Destroyed, module); }
        foreach (BindingLayoutResource layout in state.Layouts) { ObjectDisposedException.ThrowIf(layout.Destroyed, layout); }
        if (state.Handle is not null) { return; }
        foreach (P.GpuShaderEntryPoint entry in state.Shaders.EntryPoints) { shaderUtf8.GetByteCount(entry.Name); }
        var layoutResult = CreateObject(device, "pipelineLayout", new
        {
            bindGroupLayouts = Enumerable.Range(0, state.Layouts.Length).Select(Ref).ToArray(),
            immediateSize = state.ImmediateSize,
        }, state.Layouts.Select(static layout => layout.Handle).ToArray());
        JSObject? pipeline = null;
        try
        {
            var references = state.Modules.Select(static module => module.Handle).Prepend(layoutResult.Handle).ToArray();
            object Entry(int index) => new { module = Ref(index + 1), entryPoint = state.Shaders.EntryPoints[index].Name };
            object description;
            if (raster is null) { description = new { layout = Ref(0), compute = Entry(0) }; }
            else
            {
                int vertexIndex = state.Shaders.EntryPoints[0].Stage == P.GpuShaderStage.Vertex ? 0 : 1;
                int pixelIndex = state.Shaders.EntryPoints.Count == 2 ? 1 - vertexIndex : -1;
                description = new
                {
                    layout = Ref(0), vertex = Entry(vertexIndex),
                    fragment = pixelIndex < 0 ? null : new
                    {
                        module = Ref(pixelIndex + 1), entryPoint = state.Shaders.EntryPoints[pixelIndex].Name,
                        targets = raster.ColorTargets.Select(target => new
                        {
                            format = MapTextureFormat(target.Format), writeMask = (uint)target.WriteMask,
                            blend = target.Blend is { } blend ? MapBlend(blend) : null,
                        }).ToArray(),
                    },
                    primitive = new
                    {
                        topology = MapTopology(raster.Topology),
                        stripIndexFormat = raster.StripIndexFormat is { } indexFormat ? MapIndexFormat(indexFormat) : null,
                        cullMode = MapCullMode(raster.CullMode), frontFace = MapFrontFace(raster.FrontFace),
                    },
                    depthStencil = MapDepthStencil(raster),
                    multisample = new { count = raster.SampleCount, mask = raster.SampleMask, alphaToCoverageEnabled = raster.AlphaToCoverage },
                };
            }
            var result = CreateObject(device, raster is null ? "computePipeline" : "rasterPipeline", description, references);
            pipeline = result.Handle;
            var diagnostics = BrowserDiagnostics.CombineAsync(state.Diagnostics, layoutResult.Diagnostics, result.Diagnostics);
            state.Layout = layoutResult.Handle;
            state.Handle = pipeline;
            state.Diagnostics = diagnostics;
        }
        catch { pipeline?.Dispose(); layoutResult.Handle.Dispose(); throw; }
    }

    private static object? MapDepthStencil(P.GpuRasterPipelineDescription description)
    {
        P.GpuDepthStencilState state = description.DepthStencil;
        if (!description.DepthStencilFormat.HasValue && !state.DepthTest && !state.DepthWrite && !state.StencilTest
            && state.DepthBias == 0 && state.DepthBiasSlopeScale == 0 && state.DepthBiasClamp == 0) { return null; }
        return new
        {
            format = description.DepthStencilFormat is { } format ? MapTextureFormat(format) : null,
            depthWriteEnabled = state.DepthWrite, depthCompare = state.DepthTest ? MapCompareFunction(state.DepthCompare) : "always",
            stencilFront = state.StencilTest ? MapStencilFace(state.Front) : null,
            stencilBack = state.StencilTest ? MapStencilFace(state.Back) : null,
            stencilReadMask = state.StencilTest ? state.StencilReadMask : 0,
            stencilWriteMask = state.StencilTest ? state.StencilWriteMask : 0,
            depthBias = state.DepthBias, depthBiasSlopeScale = state.DepthBiasSlopeScale, depthBiasClamp = state.DepthBiasClamp,
        };
    }

    private static object MapStencilFace(P.GpuStencilFaceState state) => new
    { compare = MapCompareFunction(state.Compare), failOp = MapStencilOp(state.FailOp), depthFailOp = MapStencilOp(state.DepthFailOp), passOp = MapStencilOp(state.PassOp) };
    private static object MapBlend(P.GpuBlendDescription state) => new
    {
        color = new { operation = MapBlendOperation(state.ColorOperation), srcFactor = MapBlendFactor(state.SourceColorFactor), dstFactor = MapBlendFactor(state.DestinationColorFactor) },
        alpha = new { operation = MapBlendOperation(state.AlphaOperation), srcFactor = MapBlendFactor(state.SourceAlphaFactor), dstFactor = MapBlendFactor(state.DestinationAlphaFactor) },
    };
    private static string MapTopology(P.GpuPrimitiveTopology topology) => topology switch
    {
        P.GpuPrimitiveTopology.TriangleList => "triangle-list", P.GpuPrimitiveTopology.TriangleStrip => "triangle-strip",
        P.GpuPrimitiveTopology.LineList => "line-list", P.GpuPrimitiveTopology.LineStrip => "line-strip", P.GpuPrimitiveTopology.PointList => "point-list",
        _ => throw new ArgumentOutOfRangeException(nameof(topology)),
    };
    private static string MapIndexFormat(P.GpuIndexFormat format) => format switch
    { P.GpuIndexFormat.Uint16 => "uint16", P.GpuIndexFormat.Uint32 => "uint32", _ => throw new ArgumentOutOfRangeException(nameof(format)) };
    private static string MapCullMode(P.GpuCullMode mode) => mode switch
    { P.GpuCullMode.None => "none", P.GpuCullMode.Front => "front", P.GpuCullMode.Back => "back", _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
    private static string MapFrontFace(P.GpuFrontFace face) => face switch
    { P.GpuFrontFace.CounterClockwise => "ccw", P.GpuFrontFace.Clockwise => "cw", _ => throw new ArgumentOutOfRangeException(nameof(face)) };
    private static string MapStencilOp(P.GpuStencilOp op) => op switch
    {
        P.GpuStencilOp.Keep => "keep", P.GpuStencilOp.Zero => "zero", P.GpuStencilOp.Replace => "replace", P.GpuStencilOp.Invert => "invert",
        P.GpuStencilOp.IncrementClamp => "increment-clamp", P.GpuStencilOp.DecrementClamp => "decrement-clamp",
        P.GpuStencilOp.IncrementWrap => "increment-wrap", P.GpuStencilOp.DecrementWrap => "decrement-wrap",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };
    private static string MapBlendOperation(P.GpuBlendOperation op) => op switch
    {
        P.GpuBlendOperation.Add => "add", P.GpuBlendOperation.Subtract => "subtract", P.GpuBlendOperation.ReverseSubtract => "reverse-subtract",
        P.GpuBlendOperation.Minimum => "min", P.GpuBlendOperation.Maximum => "max", _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };
    private static string MapBlendFactor(P.GpuBlendFactor factor) => factor switch
    {
        P.GpuBlendFactor.Zero => "zero", P.GpuBlendFactor.One => "one", P.GpuBlendFactor.SourceColor => "src",
        P.GpuBlendFactor.OneMinusSourceColor => "one-minus-src", P.GpuBlendFactor.DestinationColor => "dst",
        P.GpuBlendFactor.OneMinusDestinationColor => "one-minus-dst", P.GpuBlendFactor.SourceAlpha => "src-alpha",
        P.GpuBlendFactor.OneMinusSourceAlpha => "one-minus-src-alpha", P.GpuBlendFactor.DestinationAlpha => "dst-alpha",
        P.GpuBlendFactor.OneMinusDestinationAlpha => "one-minus-dst-alpha", P.GpuBlendFactor.SourceAlphaSaturated => "src-alpha-saturated",
        P.GpuBlendFactor.Constant => "constant", P.GpuBlendFactor.OneMinusConstant => "one-minus-constant",
        P.GpuBlendFactor.Source1Color => "src1", P.GpuBlendFactor.OneMinusSource1Color => "one-minus-src1",
        P.GpuBlendFactor.Source1Alpha => "src1-alpha", P.GpuBlendFactor.OneMinusSource1Alpha => "one-minus-src1-alpha",
        _ => throw new ArgumentOutOfRangeException(nameof(factor)),
    };
}
