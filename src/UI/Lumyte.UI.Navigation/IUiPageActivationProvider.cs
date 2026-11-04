namespace Lumyte.UI.Navigation;

/// <summary>Defines the IUiPageActivationProvider lifecycle boundary; operations begin on the UI owner.</summary>
public interface IUiPageActivationProvider
{
    /// <summary>Validates registration and copies arguments with the registered copier.</summary>
    /// <typeparam name="TArgs">The exact registered argument type.</typeparam>
    /// <param name="key">The key value.</param>
    /// <param name="arguments">The arguments value.</param>
    /// <returns>The operation result or completion.</returns>
    UiPageDestination Capture<TArgs>(UiPageKey<TArgs> key, TArgs arguments);

    /// <summary>Returns a lightweight owned lease; resource acquisition starts in PrepareAsync.</summary>
    /// <param name="context">The context value.</param>
    /// <returns>The operation result or completion.</returns>
    IUiPageActivation Create(UiPageActivationContext context);
}
