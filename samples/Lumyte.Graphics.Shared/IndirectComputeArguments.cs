using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Supplies the command and output storage for GPU-generated dispatch.</summary>
/// <param name="Command">The writable dispatch command.</param>
/// <param name="Output">The writable execution result.</param>
public readonly partial record struct IndirectComputeArguments(IGpuRef<DispatchIndirectArguments> Command, IGpuRef<uint> Output) : IShaderArguments;
