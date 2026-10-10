using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Selects elements from independently registered buffer ranges.</summary>
/// <param name="First">The first selected element.</param>
/// <param name="Second">The second selected element.</param>
/// <param name="Output">The writable result.</param>
public readonly partial record struct BindingRangeArguments(IGpuRef<uint> First, IGpuRef<uint> Second, IGpuRef<uint> Output) : IShaderArguments;
