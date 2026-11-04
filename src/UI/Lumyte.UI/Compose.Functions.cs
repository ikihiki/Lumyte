using System.Runtime.CompilerServices;
using Lumyte.Signals;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Gets a local signal whose value survives function reconstruction at the same widget identity and call site.</summary>
    /// <typeparam name="T">The local value type.</typeparam>
    /// <param name="initialValue">The value used only on the first build.</param>
    /// <param name="key">An optional identity within the source call site.</param>
    /// <param name="file">The compiler-provided source file.</param>
    /// <param name="line">The compiler-provided source line.</param>
    /// <returns>The independent signal owned by this logical function instance.</returns>
    public static Signal<T> UseSignal<T>(T initialValue, string? key = null, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Use(() => Signal.Create(initialValue), key, file, line);

    /// <summary>Gets a local signal, evaluating its initializer only when this call site first appears.</summary>
    /// <typeparam name="T">The local value type.</typeparam>
    /// <param name="initialize">The synchronous initial value factory.</param>
    /// <param name="key">An optional identity within the source call site.</param>
    /// <param name="file">The compiler-provided source file.</param>
    /// <param name="line">The compiler-provided source line.</param>
    /// <returns>The independent signal owned by this logical function instance.</returns>
    public static Signal<T> UseSignal<T>(Func<T> initialize, string? key = null, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        return Use(() => Signal.Create(initialize()), key, file, line);
    }
}
