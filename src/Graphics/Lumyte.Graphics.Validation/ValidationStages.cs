using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Validation;

internal static class ValidationStages
{
    internal static PipelineStage Normalize(ValidationDevice device, PipelineStage stages) => MeshShaderValidation.NormalizeStages(stages, device.Caps.Features);

    internal static BarrierScope Normalize(ValidationDevice device, BarrierScope scope) => new(Normalize(device, scope.Stages), scope.Access);
}
