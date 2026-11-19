namespace Lumyte.Composition;

/// <summary>Captures a template or fixed composition for a named operation with 1 evaluation argument.</summary>
/// <typeparam name="TTarget">The component receiving the slot.</typeparam>
/// <typeparam name="TArgs">The type of evaluation argument 1.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
public sealed class CompositionTemplateSlot<TTarget, TArgs, TComposition>
    where TTarget : class
{
    private readonly Action<TTarget, CompositionTemplate<TArgs, TComposition>> _apply;

    /// <summary>Initializes a new instance of the <see cref="CompositionTemplateSlot{TTarget, TArgs, TComposition}"/> class.</summary>
    /// <param name="apply">The operation receiving the target and unevaluated template.</param>
    public CompositionTemplateSlot(Action<TTarget, CompositionTemplate<TArgs, TComposition>> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        _apply = apply;
    }

    /// <summary>Gets a deferred assignment capturing the same unevaluated template.</summary>
    /// <param name="template">The non-null template to pass to the operation.</param>
    /// <returns>A reusable assignment that does not invoke the template.</returns>
    public CompositionSlotAssignment<TTarget> this[CompositionTemplate<TArgs, TComposition> template]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(template);
            return new(target => _apply(target, template));
        }
    }

    /// <summary>Gets a deferred assignment whose template always returns the same composition.</summary>
    /// <param name="composition">The non-null, already evaluated composition. It is not cloned.</param>
    /// <returns>An assignment ignoring all evaluation arguments.</returns>
    public CompositionSlotAssignment<TTarget> this[TComposition composition]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(composition);
            CompositionTemplate<TArgs, TComposition> template = (arg1) => composition;
            return this[template];
        }
    }
}
