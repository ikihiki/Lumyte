using System.Runtime.CompilerServices;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Retains a value at this call site for the current logical function instance.</summary>
    /// <typeparam name="T">The retained value type.</typeparam>
    /// <param name="initialize">Creates the value on its first build.</param>
    /// <param name="key">An optional identity within the call site.</param>
    /// <param name="file">The compiler-provided source file.</param>
    /// <param name="line">The compiler-provided source line.</param>
    /// <returns>The retained value. Resources are not automatically disposed.</returns>
    public static T Use<T>(Func<T> initialize, string? key = null, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        return UiFunctionBuild.Use(initialize, key, file, line);
    }

    /// <summary>Reads the nearest provided value during function evaluation.</summary>
    /// <typeparam name="T">The exact value type.</typeparam>
    /// <param name="context">The identity of the provided value.</param>
    /// <returns>The borrowed value.</returns>
    public static T UseContext<T>(UiContextKey<T> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return UiFunctionBuild.UseContext(context);
    }

    /// <summary>Provides a borrowed value to a fresh declaration and its descendants.</summary>
    /// <typeparam name="T">The exact value type.</typeparam>
    /// <param name="context">The identity of the provided value.</param>
    /// <param name="value">The borrowed value.</param>
    /// <param name="content">The fresh declaration subtree.</param>
    /// <returns>The same declaration, annotated for function evaluation.</returns>
    public static Widget Provide<T>(UiContextKey<T> context, T value, Widget content)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(content);
        content.ProvidedContexts = UiUseEnvironment.Provide(content.ProvidedContexts, context, value);
        return content;
    }
}
