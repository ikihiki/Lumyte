namespace Lumyte.Execution;

/// <summary>A synchronous update called directly by its owner.</summary>
public interface IUpdateHandler
{
    /// <summary>Updates the owned state.</summary>
    /// <param name="tick">The actual timing and skipped periods.</param>
    /// <param name="context">The access verifier.</param>
    void Update(in UpdateTick tick, in WorkContext context);
}
