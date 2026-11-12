using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

internal sealed record GraphShaderBinding(RenderGraphResource Resource, ulong Offset, ulong Count, bool ShaderData, ArgumentBufferAccess? Access, Action<GraphBindingLookup>? Register);
