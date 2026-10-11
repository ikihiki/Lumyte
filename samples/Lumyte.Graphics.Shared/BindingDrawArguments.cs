using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Defines application root values selecting a logical material range.</summary>
/// <param name="Materials">The logical material range.</param>
/// <param name="Count">The number of materials drawn by the shader.</param>
public readonly partial record struct BindingDrawArguments(IGpuRef<BindingMaterial> Materials, uint Count) : IShaderArguments;
