namespace Lumyte.Pipelines;

/// <summary>Validates a result return before entering irreversible cleanup.</summary>
internal interface IPipelineResultOwner
{
    /// <summary>Rejects unfinished use without consuming the caller's active result.</summary>
    void ValidateReturn();
}
