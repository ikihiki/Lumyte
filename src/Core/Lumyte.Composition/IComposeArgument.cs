namespace Lumyte.Composition;

/// <summary>Resolves a construction argument and attaches passive metadata to the constructed target.</summary>
/// <typeparam name="T">The exact member type.</typeparam>
public interface IComposeArgument<T>
{
    /// <summary>Gets a value indicating whether this argument was explicitly supplied.</summary>
    bool IsSpecified { get; }

    /// <summary>Resolves the initial value once, before constructing a target.</summary>
    /// <returns>The current argument value.</returns>
    T GetInitialValue();

    /// <summary>Attaches passive reconstruction metadata after constructor and additional actions succeed.</summary>
    /// <typeparam name="TTarget">The target type.</typeparam>
    /// <param name="target">The newly constructed target.</param>
    /// <param name="memberKey">A stable generated member identity.</param>
    /// <param name="initialValue">The value resolved before construction.</param>
    /// <param name="setter">A construction-only typed setter.</param>
    void AttachTo<TTarget>(TTarget target, object memberKey, T initialValue, Action<TTarget, T> setter)
        where TTarget : class;
}
