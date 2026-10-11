using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Supplies writable results for the indirect compute consumer.</summary>
/// <param name="Output">The writable execution result.</param>
public readonly partial record struct IndirectExecuteArguments(IGpuRef<uint> Output) : IShaderArguments;
