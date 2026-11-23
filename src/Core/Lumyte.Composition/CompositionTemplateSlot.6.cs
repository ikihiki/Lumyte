namespace Lumyte.Composition;

/// <summary>Captures a template or fixed composition for a named operation with 6 evaluation arguments.</summary>
/// <typeparam name="TTarget">The component receiving the slot.</typeparam>
/// <typeparam name="T1">The type of evaluation argument 1.</typeparam>
/// <typeparam name="T2">The type of evaluation argument 2.</typeparam>
/// <typeparam name="T3">The type of evaluation argument 3.</typeparam>
/// <typeparam name="T4">The type of evaluation argument 4.</typeparam>
/// <typeparam name="T5">The type of evaluation argument 5.</typeparam>
/// <typeparam name="T6">The type of evaluation argument 6.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
public sealed class CompositionTemplateSlot<TTarget, T1, T2, T3, T4, T5, T6, TComposition>
    where TTarget : class
{
    private readonly Action<TTarget, CompositionTemplate<T1, T2, T3, T4, T5, T6, TComposition>> _apply;

    /// <summary>Initializes a new instance of the <see cref="CompositionTemplateSlot{TTarget, T1, T2, T3, T4, T5, T6, TComposition}"/> class.</summary>
    /// <param name="apply">The operation receiving the target and unevaluated template.</param>
    public CompositionTemplateSlot(Action<TTarget, CompositionTemplate<T1, T2, T3, T4, T5, T6, TComposition>> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        _apply = apply;
    }

    /// <summary>Gets a deferred assignment capturing the same unevaluated template.</summary>
    /// <param name="template">The non-null template to pass to the operation.</param>
    /// <returns>A reusable assignment that does not invoke the template.</returns>
    public CompositionSlotAssignment<TTarget> this[CompositionTemplate<T1, T2, T3, T4, T5, T6, TComposition> template]
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
            CompositionTemplate<T1, T2, T3, T4, T5, T6, TComposition> template = (arg1, arg2, arg3, arg4, arg5, arg6) => composition;
            return this[template];
        }
    }
}
