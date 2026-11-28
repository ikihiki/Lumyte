namespace Lumyte.Pipelines;

/// <summary>Binds a fresh explicit extension instance to one invocation transaction.</summary>
/// <remarks>Complete runs after context expiry. A failing success hook is followed by Complete(false); rollback must tolerate partial Bind and partial commit.</remarks>
public interface IStageScopeBinding
{
    /// <summary>Binds the scope before the processor is invoked.</summary>
    /// <param name="context">The invocation scope.</param>
    void Bind(StagePreparationContext context);

    /// <summary>Completes the invocation scope after processor evaluation.</summary>
    /// <param name="succeeded">Whether a validated output was published.</param>
    void Complete(bool succeeded);
}
