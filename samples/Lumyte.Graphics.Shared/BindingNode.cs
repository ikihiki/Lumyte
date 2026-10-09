using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Defines application-owned linked shader data for dependency traversal checks.</summary>
/// <param name="Value">The numeric payload.</param>
/// <param name="Next">The next logical element, which may form a cycle.</param>
public readonly record struct BindingNode(uint Value, IGpuRef<BindingNode> Next) : IShaderData;
