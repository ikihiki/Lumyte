namespace Lumyte.Composition;

/// <summary>Dispatches generated argument operations through constrained interface calls without boxing.</summary>
/// <typeparam name="T">The exact declared value type.</typeparam>
public static class ComposeArgument<T>
{
    /// <summary>Checks an argument through its interface, including explicit implementations.</summary>
    /// <typeparam name="TArgument">The concrete adapter struct.</typeparam>
    /// <param name="argument">The factory's adapter variable.</param>
    /// <returns>Whether the argument was supplied.</returns>
    public static bool IsSpecified<TArgument>(ref TArgument argument)
        where TArgument : struct, IComposeArgument<T> => argument.IsSpecified;

    /// <summary>Resolves an argument while preserving mutations to its factory-local struct.</summary>
    /// <typeparam name="TArgument">The concrete adapter struct.</typeparam>
    /// <param name="argument">The factory's adapter variable.</param>
    /// <returns>The resolved initial value.</returns>
    public static T GetInitialValue<TArgument>(ref TArgument argument)
        where TArgument : struct, IComposeArgument<T> => argument.GetInitialValue();

    /// <summary>Attaches passive metadata through the adapter interface without copying the adapter.</summary>
    /// <typeparam name="TArgument">The concrete adapter struct.</typeparam>
    /// <typeparam name="TTarget">The concrete target.</typeparam>
    /// <param name="argument">The factory's adapter variable.</param>
    /// <param name="target">The newly constructed target.</param>
    /// <param name="memberKey">The generated member identity.</param>
    /// <param name="initialValue">The previously resolved value.</param>
    /// <param name="setter">The typed construction setter.</param>
    public static void AttachTo<TArgument, TTarget>(ref TArgument argument, TTarget target, object memberKey, T initialValue, Action<TTarget, T> setter)
        where TArgument : struct, IComposeArgument<T>
        where TTarget : class => argument.AttachTo(target, memberKey, initialValue, setter);
}
