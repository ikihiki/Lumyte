namespace Lumyte.Pipelines;

/// <summary>Validates actual finite extension requirements against the compiled reservation.</summary>
internal interface IStageRequirementValidation
{
    /// <summary>Rejects changed capabilities before stage invocation.</summary>
    /// <param name="actual">The requirements derived from actual immutable inputs.</param>
    void Validate(StageRequirements actual);
}
