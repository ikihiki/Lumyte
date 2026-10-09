using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Defines application root values for compute checks.</summary>
/// <param name="Camera">The row-major camera matrix.</param>
/// <param name="Factor">The integer payload.</param>
/// <param name="Node">The first linked data element.</param>
/// <param name="Output">The writable raw output range.</param>
[ShaderArguments]
public readonly record struct BindingComputeArguments(Matrix4x4 Camera, uint Factor, IGpuRef<BindingNode> Node, IGpuRef<uint> Output);
