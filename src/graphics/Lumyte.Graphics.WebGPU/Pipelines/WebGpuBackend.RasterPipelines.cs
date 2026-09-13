using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private sealed class RasterPipelineResource(WebGpuBackend owner, P.GpuRasterPipelineDescription description,
        P.GpuShaderProgramDescription shaders, P.GpuShaderEntryPoint vertexEntry, P.GpuShaderEntryPoint? pixelEntry,
        ShaderModuleResource vertexModule, ShaderModuleResource? pixelModule, BindingLayoutResource[] layouts,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuRasterPipelineHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly P.GpuRasterPipelineDescription Description = description;
        internal readonly uint ImmediateSize = shaders.ImmediateSize;
        internal readonly P.GpuShaderEntryPoint VertexEntry = vertexEntry;
        internal readonly P.GpuShaderEntryPoint? PixelEntry = pixelEntry;
        internal readonly ShaderModuleResource VertexModule = vertexModule;
        internal readonly ShaderModuleResource? PixelModule = pixelModule;
        internal readonly BindingLayoutResource[] Layouts = layouts;
        internal F.RenderPipelineHandle Handle;
        internal F.PipelineLayoutHandle PipelineLayout;
        internal Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    internal readonly record struct RasterPipelineCreationStatistics(int Creations);
    private int rasterPipelineCreations;

    internal RasterPipelineCreationStatistics RasterPipelineStatistics
    {
        get { lock (gate) { return new(rasterPipelineCreations); } }
    }

    public P.GpuRasterPipelineHandle CreateRasterPipeline(P.GpuRasterPipelineDescription description,
        P.GpuShaderProgramDescription shaders)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(shaders);
        P.GpuShaderEntryPoint? vertexEntry = null;
        P.GpuShaderEntryPoint? pixelEntry = null;
        foreach (P.GpuShaderEntryPoint entry in shaders.EntryPoints)
        {
            if (entry.Name is null)
            { throw new ArgumentException("Raster entry point names cannot be null.", nameof(shaders)); }
            switch (entry.Stage)
            {
                case P.GpuShaderStage.Vertex when vertexEntry is null: vertexEntry = entry; break;
                case P.GpuShaderStage.Pixel when pixelEntry is null: pixelEntry = entry; break;
                default: throw new ArgumentException("A raster pipeline accepts one Vertex and at most one Pixel entry point.", nameof(shaders));
            }
        }
        if (vertexEntry is null)
        { throw new ArgumentException("A raster pipeline requires one Vertex entry point.", nameof(shaders)); }
        // There is no native target descriptor without a fragment stage; do not discard declared outputs.
        if (pixelEntry is null && description.ColorTargets.Count != 0)
        { throw new ArgumentException("Color target descriptions require a Pixel entry point.", nameof(shaders)); }

        lock (gate)
        {
            RequireAvailable();
            ShaderModuleResource vertexModule = RequireShaderModule(vertexEntry.Value.Module);
            ShaderModuleResource? pixelModule = pixelEntry.HasValue ? RequireShaderModule(pixelEntry.Value.Module) : null;
            var layouts = new BindingLayoutResource[shaders.BindingLayouts.Count];
            var dependencies = new List<Task<IReadOnlyList<P.GpuDiagnostic>>> { vertexModule.Diagnostics };
            if (pixelModule is not null && !ReferenceEquals(vertexModule, pixelModule))
            { dependencies.Add(pixelModule.Diagnostics); }
            for (int index = 0; index < layouts.Length; index++)
            {
                layouts[index] = RequireBindingLayout(shaders.BindingLayouts[index]);
                dependencies.Add(layouts[index].Diagnostics);
            }
            return new RasterPipelineResource(this, description, shaders, vertexEntry.Value, pixelEntry,
                vertexModule, pixelModule, layouts, WebGpuDiagnostics.CombineAsync(dependencies.ToArray()));
        }
    }

    public void DestroyRasterPipeline(P.GpuRasterPipelineHandle pipeline)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            RasterPipelineResource resource = RequireRasterPipeline(pipeline);
            resource.Destroyed = true;
            if ((nuint)resource.Handle != 0) { F.WebGPU_FFI.RenderPipelineRelease(resource.Handle); }
            if ((nuint)resource.PipelineLayout != 0) { F.WebGPU_FFI.PipelineLayoutRelease(resource.PipelineLayout); }
        }
    }

    private RasterPipelineResource RequireRasterPipeline(P.GpuRasterPipelineHandle pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not RasterPipelineResource resource || !ReferenceEquals(resource.Owner, this))
        { throw new ArgumentException("Raster pipeline belongs to another device.", nameof(pipeline)); }
        ObjectDisposedException.ThrowIf(resource.Destroyed, pipeline);
        return resource;
    }

    // Submission holds gate. Merely creating or selecting a logical pipeline does not compile it.
    private unsafe void EnsureRasterPipeline(RasterPipelineResource resource)
    {
        ObjectDisposedException.ThrowIf(resource.Destroyed, resource);
        ObjectDisposedException.ThrowIf(resource.VertexModule.Destroyed, resource.VertexModule);
        if (resource.PixelModule is not null) { ObjectDisposedException.ThrowIf(resource.PixelModule.Destroyed, resource.PixelModule); }
        foreach (BindingLayoutResource layout in resource.Layouts) { ObjectDisposedException.ThrowIf(layout.Destroyed, layout); }
        if ((nuint)resource.Handle != 0) { return; }

        var layouts = new F.BindGroupLayoutHandle[resource.Layouts.Length];
        for (int index = 0; index < layouts.Length; index++) { layouts[index] = resource.Layouts[index].Handle; }
        byte[] vertexName = shaderUtf8.GetBytes(resource.VertexEntry.Name);
        byte[] pixelName = resource.PixelEntry.HasValue ? shaderUtf8.GetBytes(resource.PixelEntry.Value.Name) : [];
        var targets = new F.ColorTargetStateFFI[resource.Description.ColorTargets.Count];
        var blends = new N.BlendState[targets.Length];
        F.PipelineLayoutHandle pipelineLayout = default;
        F.RenderPipelineHandle pipeline = default;
        try
        {
            Task<IReadOnlyList<P.GpuDiagnostic>> layoutDiagnostics;
            fixed (F.BindGroupLayoutHandle* pointer = layouts)
            {
                var native = new F.PipelineLayoutDescriptorFFI
                {
                    BindGroupLayouts = pointer,
                    BindGroupLayoutCount = (nuint)layouts.Length,
                    ImmediateSize = resource.ImmediateSize,
                };
                PushScopes();
                try { pipelineLayout = F.WebGPU_FFI.DeviceCreatePipelineLayout(device, &native); }
                finally { layoutDiagnostics = PopScopes(); }
            }
            if ((nuint)pipelineLayout == 0)
            {
                status.Lose("WebGPU raster pipeline layout creation returned no object.");
                status.ThrowIfFailed();
            }

            Task<IReadOnlyList<P.GpuDiagnostic>> pipelineDiagnostics;
            fixed (byte* vertexPointer = vertexName)
            fixed (byte* pixelPointer = pixelName)
            fixed (F.ColorTargetStateFFI* targetPointer = targets)
            fixed (N.BlendState* blendPointer = blends)
            {
                for (int index = 0; index < targets.Length; index++)
                {
                    P.GpuColorTargetDescription target = resource.Description.ColorTargets[index];
                    if (target.Blend.HasValue) { blends[index] = MapRasterBlend(target.Blend.Value); }
                    targets[index] = new()
                    {
                        Format = MapTextureFormat(target.Format),
                        // The ABI has matching flag positions and a wider field; unknown bits reach native validation.
                        WriteMask = (N.ColorWriteMask)target.WriteMask,
                        Blend = target.Blend.HasValue ? &blendPointer[index] : null,
                    };
                }
                var fragment = new F.FragmentStateFFI
                {
                    Module = resource.PixelModule?.Handle ?? default,
                    EntryPoint = new() { Data = pixelPointer, Length = (nuint)pixelName.Length },
                    Targets = targetPointer,
                    TargetCount = (nuint)targets.Length,
                };
                P.GpuRasterPipelineDescription description = resource.Description;
                N.DepthStencilState depthStencil = MapRasterDepthStencil(description);
                P.GpuDepthStencilState depth = description.DepthStencil;
                bool hasDepthState = description.DepthStencilFormat.HasValue || depth.DepthTest || depth.DepthWrite
                    || depth.StencilTest || depth.DepthBias != 0 || depth.DepthBiasSlopeScale != 0 || depth.DepthBiasClamp != 0;
                var native = new F.RenderPipelineDescriptorFFI
                {
                    Layout = pipelineLayout,
                    Vertex = new()
                    {
                        Module = resource.VertexModule.Handle,
                        EntryPoint = new() { Data = vertexPointer, Length = (nuint)vertexName.Length },
                    },
                    Fragment = resource.PixelModule is null ? null : &fragment,
                    Primitive = new()
                    {
                        Topology = MapRasterTopology(description.Topology),
                        StripIndexFormat = description.StripIndexFormat.HasValue
                            ? MapIndexFormat(description.StripIndexFormat.Value) : N.IndexFormat.Undefined,
                        CullMode = MapRasterCullMode(description.CullMode),
                        FrontFace = MapRasterFrontFace(description.FrontFace),
                    },
                    DepthStencil = hasDepthState ? &depthStencil : null,
                    Multisample = new()
                    {
                        Count = description.SampleCount,
                        Mask = description.SampleMask,
                        AlphaToCoverageEnabled = description.AlphaToCoverage,
                    },
                };
                PushScopes();
                try { pipeline = F.WebGPU_FFI.DeviceCreateRenderPipeline(device, &native); }
                finally { pipelineDiagnostics = PopScopes(); }
            }
            if ((nuint)pipeline == 0)
            {
                status.Lose("WebGPU raster pipeline creation returned no object.");
                status.ThrowIfFailed();
            }
            Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics = WebGpuDiagnostics.CombineAsync(
                resource.Diagnostics, layoutDiagnostics, pipelineDiagnostics);
            resource.Handle = pipeline;
            resource.PipelineLayout = pipelineLayout;
            resource.Diagnostics = diagnostics;
            rasterPipelineCreations++;
        }
        catch
        {
            if ((nuint)pipeline != 0) { F.WebGPU_FFI.RenderPipelineRelease(pipeline); }
            if ((nuint)pipelineLayout != 0) { F.WebGPU_FFI.PipelineLayoutRelease(pipelineLayout); }
            throw;
        }
    }

    private static N.PrimitiveTopology MapRasterTopology(P.GpuPrimitiveTopology topology) => topology switch
    {
        P.GpuPrimitiveTopology.TriangleList => N.PrimitiveTopology.TriangleList,
        P.GpuPrimitiveTopology.TriangleStrip => N.PrimitiveTopology.TriangleStrip,
        P.GpuPrimitiveTopology.LineList => N.PrimitiveTopology.LineList,
        P.GpuPrimitiveTopology.LineStrip => N.PrimitiveTopology.LineStrip,
        P.GpuPrimitiveTopology.PointList => N.PrimitiveTopology.PointList,
        _ => throw new ArgumentOutOfRangeException(nameof(topology)),
    };

    private static N.CullMode MapRasterCullMode(P.GpuCullMode mode) => mode switch
    {
        P.GpuCullMode.None => N.CullMode.None,
        P.GpuCullMode.Front => N.CullMode.Front,
        P.GpuCullMode.Back => N.CullMode.Back,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static N.FrontFace MapRasterFrontFace(P.GpuFrontFace face) => face switch
    {
        P.GpuFrontFace.CounterClockwise => N.FrontFace.CCW,
        P.GpuFrontFace.Clockwise => N.FrontFace.CW,
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    private static N.BlendState MapRasterBlend(P.GpuBlendDescription blend) => new()
    {
        Color = new()
        {
            Operation = MapRasterBlendOperation(blend.ColorOperation),
            SrcFactor = MapRasterBlendFactor(blend.SourceColorFactor),
            DstFactor = MapRasterBlendFactor(blend.DestinationColorFactor),
        },
        Alpha = new()
        {
            Operation = MapRasterBlendOperation(blend.AlphaOperation),
            SrcFactor = MapRasterBlendFactor(blend.SourceAlphaFactor),
            DstFactor = MapRasterBlendFactor(blend.DestinationAlphaFactor),
        },
    };

    private static N.BlendOperation MapRasterBlendOperation(P.GpuBlendOperation operation) => operation switch
    {
        P.GpuBlendOperation.Add => N.BlendOperation.Add,
        P.GpuBlendOperation.Subtract => N.BlendOperation.Subtract,
        P.GpuBlendOperation.ReverseSubtract => N.BlendOperation.ReverseSubtract,
        P.GpuBlendOperation.Minimum => N.BlendOperation.Min,
        P.GpuBlendOperation.Maximum => N.BlendOperation.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static N.BlendFactor MapRasterBlendFactor(P.GpuBlendFactor factor) => factor switch
    {
        P.GpuBlendFactor.Zero => N.BlendFactor.Zero,
        P.GpuBlendFactor.One => N.BlendFactor.One,
        P.GpuBlendFactor.SourceColor => N.BlendFactor.Src,
        P.GpuBlendFactor.OneMinusSourceColor => N.BlendFactor.OneMinusSrc,
        P.GpuBlendFactor.DestinationColor => N.BlendFactor.Dst,
        P.GpuBlendFactor.OneMinusDestinationColor => N.BlendFactor.OneMinusDst,
        P.GpuBlendFactor.SourceAlpha => N.BlendFactor.SrcAlpha,
        P.GpuBlendFactor.OneMinusSourceAlpha => N.BlendFactor.OneMinusSrcAlpha,
        P.GpuBlendFactor.DestinationAlpha => N.BlendFactor.DstAlpha,
        P.GpuBlendFactor.OneMinusDestinationAlpha => N.BlendFactor.OneMinusDstAlpha,
        P.GpuBlendFactor.SourceAlphaSaturated => N.BlendFactor.SrcAlphaSaturated,
        P.GpuBlendFactor.Constant => N.BlendFactor.Constant,
        P.GpuBlendFactor.OneMinusConstant => N.BlendFactor.OneMinusConstant,
        P.GpuBlendFactor.Source1Color => N.BlendFactor.Src1,
        P.GpuBlendFactor.OneMinusSource1Color => N.BlendFactor.OneMinusSrc1,
        P.GpuBlendFactor.Source1Alpha => N.BlendFactor.Src1Alpha,
        P.GpuBlendFactor.OneMinusSource1Alpha => N.BlendFactor.OneMinusSrc1Alpha,
        _ => throw new ArgumentOutOfRangeException(nameof(factor)),
    };

    private static N.DepthStencilState MapRasterDepthStencil(P.GpuRasterPipelineDescription description)
    {
        P.GpuDepthStencilState state = description.DepthStencil;
        return new()
        {
            Format = description.DepthStencilFormat.HasValue
                ? MapTextureFormat(description.DepthStencilFormat.Value) : N.TextureFormat.Undefined,
            DepthWriteEnabled = state.DepthWrite ? N.OptionalBool.True : N.OptionalBool.False,
            DepthCompare = state.DepthTest ? MapCompareFunction(state.DepthCompare) : N.CompareFunction.Always,
            StencilFront = state.StencilTest ? MapRasterStencilFace(state.Front) : new(),
            StencilBack = state.StencilTest ? MapRasterStencilFace(state.Back) : new(),
            StencilReadMask = state.StencilTest ? state.StencilReadMask : 0,
            StencilWriteMask = state.StencilTest ? state.StencilWriteMask : 0,
            DepthBias = state.DepthBias,
            DepthBiasSlopeScale = state.DepthBiasSlopeScale,
            DepthBiasClamp = state.DepthBiasClamp,
        };
    }

    private static N.StencilFaceState MapRasterStencilFace(P.GpuStencilFaceState face) => new()
    {
        Compare = MapCompareFunction(face.Compare),
        FailOp = MapRasterStencilOperation(face.FailOp),
        DepthFailOp = MapRasterStencilOperation(face.DepthFailOp),
        PassOp = MapRasterStencilOperation(face.PassOp),
    };

    private static N.StencilOperation MapRasterStencilOperation(P.GpuStencilOp operation) => operation switch
    {
        P.GpuStencilOp.Keep => N.StencilOperation.Keep,
        P.GpuStencilOp.Zero => N.StencilOperation.Zero,
        P.GpuStencilOp.Replace => N.StencilOperation.Replace,
        P.GpuStencilOp.IncrementClamp => N.StencilOperation.IncrementClamp,
        P.GpuStencilOp.DecrementClamp => N.StencilOperation.DecrementClamp,
        P.GpuStencilOp.Invert => N.StencilOperation.Invert,
        P.GpuStencilOp.IncrementWrap => N.StencilOperation.IncrementWrap,
        P.GpuStencilOp.DecrementWrap => N.StencilOperation.DecrementWrap,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
