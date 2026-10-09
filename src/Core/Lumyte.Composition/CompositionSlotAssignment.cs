namespace Lumyte.Composition;

/// <summary>Represents a deferred named child operation for a particular target type.</summary>
/// <typeparam name="TTarget">The component receiving the slot.</typeparam>
public readonly struct CompositionSlotAssignment<TTarget>
    where TTarget : class
{
    private readonly Action<TTarget>? _apply;

    /// <summary>Initializes a new instance of the <see cref="CompositionSlotAssignment{TTarget}"/> struct.</summary>
    /// <param name="apply">The target operation.</param>
    public CompositionSlotAssignment(Action<TTarget> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        _apply = apply;
    }

    /// <summary>Applies the captured operation to a component.</summary>
    /// <param name="target">The non-null receiving component.</param>
    /// <exception cref="InvalidOperationException">The assignment is uninitialized.</exception>
    public void Apply(TTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_apply is null)
        {
            throw new InvalidOperationException("Uninitialized composition slot assignment.");
        }

        _apply(target);
    }
}
