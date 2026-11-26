namespace Lumyte.Pipelines;

/// <summary>Binds an explicit extension instance to one invocation scope.</summary>
internal interface IStageScopeBinding
{
    /// <summary>Binds the scope before the processor is invoked.</summary>
    /// <param name="context">The invocation scope.</param>
    void Bind(StagePreparationContext context);

    /// <summary>Completes the invocation scope after processor evaluation.</summary>
    /// <param name="succeeded">Whether a validated output was published.</param>
    void Complete(bool succeeded);
}
