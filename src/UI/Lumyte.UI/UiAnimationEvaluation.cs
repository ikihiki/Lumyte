using Lumyte.Signals;

namespace Lumyte.UI;

internal static class UiAnimationEvaluation
{
    internal static T Read<T>(Func<T> read) => Signal.Computed(read).Peek();
}
