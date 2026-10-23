namespace Lumyte.Execution;

/// <summary>Verifies physical execution access.</summary>
public readonly struct WorkContext
{
    private readonly ExecutionRuntime? _runtime;
    private readonly OwnerDefinition? _owner;

    internal WorkContext(ExecutionRuntime runtime, OwnerDefinition? owner)
    {
        _runtime = runtime;
        _owner = owner;
    }

    /// <summary>Rejects access outside the current handler or on another physical thread.</summary>
    public void VerifyAccess()
    {
        if (_runtime is null || ExecutionRuntime.Executing != _runtime || (_owner is not null && _owner.ThreadId != Environment.CurrentManagedThreadId))
        {
            throw new InvalidOperationException("Access is outside the declared execution target.");
        }
    }
}
