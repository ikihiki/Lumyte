using P = Lumyte.Graphics.Portable;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private sealed class ComputePipelineResource(WebGpuBackend owner, P.GpuShaderProgramDescription shaders,
        ShaderModuleResource module, BindingLayoutResource[] layouts,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuComputePipelineHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly P.GpuShaderProgramDescription Shaders = shaders;
        internal readonly uint ImmediateSize = shaders.ImmediateSize;
        internal readonly ShaderModuleResource Module = module;
        internal readonly BindingLayoutResource[] Layouts = layouts;
        internal F.ComputePipelineHandle Handle;
        internal F.PipelineLayoutHandle PipelineLayout;
        internal Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    internal readonly record struct ComputePipelineStatistics(int Creations);
    private int computePipelineCreations;

    internal ComputePipelineStatistics PipelineStatistics
    {
        get { lock (gate) { return new(computePipelineCreations); } }
    }

    public P.GpuComputePipelineHandle CreateComputePipeline(P.GpuShaderProgramDescription shaders)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        // A compute descriptor has one entry slot. Do not silently drop another entry or its declared stage.
        if (shaders.EntryPoints.Count != 1 || shaders.EntryPoints[0].Stage != P.GpuShaderStage.Compute)
        { throw new ArgumentException("A compute pipeline requires exactly one Compute entry point.", nameof(shaders)); }
        if (shaders.EntryPoints[0].Name is null)
        { throw new ArgumentException("The compute entry point name cannot be null.", nameof(shaders)); }
        lock (gate)
        {
            RequireAvailable();
            ShaderModuleResource module = RequireShaderModule(shaders.EntryPoints[0].Module);
            var layouts = new BindingLayoutResource[shaders.BindingLayouts.Count];
            var dependencies = new Task<IReadOnlyList<P.GpuDiagnostic>>[layouts.Length + 1];
            dependencies[0] = module.Diagnostics;
            for (int index = 0; index < layouts.Length; index++)
            {
                layouts[index] = RequireBindingLayout(shaders.BindingLayouts[index]);
                dependencies[index + 1] = layouts[index].Diagnostics;
            }
            return new ComputePipelineResource(this, shaders, module, layouts, WebGpuDiagnostics.CombineAsync(dependencies));
        }
    }

    public void DestroyComputePipeline(P.GpuComputePipelineHandle pipeline)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ComputePipelineResource resource = RequireComputePipeline(pipeline);
            resource.Destroyed = true;
            if ((nuint)resource.Handle != 0) { F.WebGPU_FFI.ComputePipelineRelease(resource.Handle); }
            if ((nuint)resource.PipelineLayout != 0) { F.WebGPU_FFI.PipelineLayoutRelease(resource.PipelineLayout); }
        }
    }

    private ComputePipelineResource RequireComputePipeline(P.GpuComputePipelineHandle pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not ComputePipelineResource resource || !ReferenceEquals(resource.Owner, this))
        { throw new ArgumentException("Compute pipeline belongs to another device.", nameof(pipeline)); }
        ObjectDisposedException.ThrowIf(resource.Destroyed, pipeline);
        return resource;
    }

    // Submission holds gate. No GPU pipeline is created during logical construction or command recording.
    private unsafe void EnsureComputePipeline(ComputePipelineResource resource)
    {
        ObjectDisposedException.ThrowIf(resource.Destroyed, resource);
        ObjectDisposedException.ThrowIf(resource.Module.Destroyed, resource.Module);
        foreach (BindingLayoutResource layout in resource.Layouts) { ObjectDisposedException.ThrowIf(layout.Destroyed, layout); }
        if ((nuint)resource.Handle != 0) { return; }

        var layouts = new F.BindGroupLayoutHandle[resource.Layouts.Length];
        for (int index = 0; index < layouts.Length; index++) { layouts[index] = resource.Layouts[index].Handle; }
        byte[] entryName = shaderUtf8.GetBytes(resource.Shaders.EntryPoints[0].Name);
        F.PipelineLayoutHandle pipelineLayout = default;
        F.ComputePipelineHandle pipeline = default;
        try
        {
            Task<IReadOnlyList<P.GpuDiagnostic>> layoutDiagnostics;
            fixed (F.BindGroupLayoutHandle* pointer = layouts)
            {
                var description = new F.PipelineLayoutDescriptorFFI
                {
                    BindGroupLayouts = pointer,
                    BindGroupLayoutCount = (nuint)layouts.Length,
                    ImmediateSize = resource.ImmediateSize,
                };
                PushScopes();
                try { pipelineLayout = F.WebGPU_FFI.DeviceCreatePipelineLayout(device, &description); }
                finally { layoutDiagnostics = PopScopes(); }
            }
            if ((nuint)pipelineLayout == 0)
            {
                status.Lose("WebGPU compute pipeline layout creation returned no object.");
                status.ThrowIfFailed();
            }
            Task<IReadOnlyList<P.GpuDiagnostic>> pipelineDiagnostics;
            fixed (byte* pointer = entryName)
            {
                var description = new F.ComputePipelineDescriptorFFI
                {
                    Layout = pipelineLayout,
                    Compute = new()
                    {
                        Module = resource.Module.Handle,
                        EntryPoint = new() { Data = pointer, Length = (nuint)entryName.Length },
                    },
                };
                PushScopes();
                try { pipeline = F.WebGPU_FFI.DeviceCreateComputePipeline(device, &description); }
                finally { pipelineDiagnostics = PopScopes(); }
            }
            if ((nuint)pipeline == 0)
            {
                status.Lose("WebGPU compute pipeline creation returned no object.");
                status.ThrowIfFailed();
            }
            Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics = WebGpuDiagnostics.CombineAsync(
                resource.Diagnostics, layoutDiagnostics, pipelineDiagnostics);
            resource.Handle = pipeline;
            resource.PipelineLayout = pipelineLayout;
            resource.Diagnostics = diagnostics;
            computePipelineCreations++;
        }
        catch
        {
            if ((nuint)pipeline != 0) { F.WebGPU_FFI.ComputePipelineRelease(pipeline); }
            if ((nuint)pipelineLayout != 0) { F.WebGPU_FFI.PipelineLayoutRelease(pipelineLayout); }
            throw;
        }
    }
}
