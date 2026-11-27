using Lumyte.Pipelines;

namespace Lumyte.Graphics.Planning;
/// <summary>Reserves finite declaration structure; native allocation is separately admitted by the common frame budget.</summary>
/// <param name = "MaximumPasses">The maximum passes declared by one invocation.</param>
/// <param name = "MaximumResources">The maximum logical resources declared by one invocation.</param>
public sealed record GraphicsStageRequirement(int MaximumPasses, int MaximumResources) : IStageExtensionRequirement;
