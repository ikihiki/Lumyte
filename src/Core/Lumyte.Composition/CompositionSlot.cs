namespace Lumyte.Composition;

/// <summary>Captures children for a named operation without owning node storage.</summary>
/// <typeparam name="TTarget">The component receiving the slot.</typeparam>
/// <typeparam name="TChild">The accepted child type.</typeparam>
public sealed class CompositionSlot<TTarget, TChild>
    where TTarget : class
{
    private readonly Action<TTarget, TChild[]> _apply;

    /// <summary>Initializes a new instance of the <see cref="CompositionSlot{TTarget, TChild}"/> class.</summary>
    /// <param name="apply">The operation called when the assignment is applied.</param>
    public CompositionSlot(Action<TTarget, TChild[]> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        _apply = apply;
    }

    /// <summary>Gets an assignment capturing a snapshot of the supplied children.</summary>
    /// <param name="children">The ordered children; an empty array clears the slot.</param>
    /// <returns>A deferred assignment reusable on compatible targets.</returns>
    public CompositionSlotAssignment<TTarget> this[params TChild[] children]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(children);
            var snapshot = (TChild[])children.Clone();
            return new(target => _apply(target, (TChild[])snapshot.Clone()));
        }
    }
}
