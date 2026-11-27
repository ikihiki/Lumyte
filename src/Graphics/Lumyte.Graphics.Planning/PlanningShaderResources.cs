using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class PlanningShaderResources(ShaderResources resources) : ShaderResources(resources)
{
    internal void ShareDomain(PlanningShaderResources other) => JoinTable(other);
}
