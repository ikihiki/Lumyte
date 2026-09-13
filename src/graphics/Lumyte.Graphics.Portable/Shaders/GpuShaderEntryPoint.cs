namespace Lumyte.Graphics.Portable;

/// <summary>A non-owning module, stage and entry-point name supplied to a pipeline.</summary>
public readonly record struct GpuShaderEntryPoint(GpuShaderModuleHandle Module, GpuShaderStage Stage, string Name);
