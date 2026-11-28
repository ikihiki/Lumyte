using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;
/// <summary>One physical descriptor exposure retained with its entire operation-local native table generation.</summary>
/// <param name = "Operation">The draw or dispatch reservation index.</param>
/// <param name = "Allocation">The native resource identity, including descriptors not selected dynamically by the shader.</param>
/// <param name = "Access">The physical table binding access, not a semantic content-version write.</param>
public sealed record PhysicalBindingUse(int Operation, object Allocation, ArgumentBufferAccess Access);
