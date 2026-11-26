namespace Lumyte.Pipelines;

/// <summary>Describes finite requirements without acquiring resources.</summary>
public interface IStageFactory
{
    /// <summary>Describes requirements for immutable input metadata.</summary>
    /// <param name="inputs">The captured input descriptions.</param>
    /// <returns>The finite requirements.</returns>
    StageRequirements Describe(StageInputDescriptions inputs);
}
