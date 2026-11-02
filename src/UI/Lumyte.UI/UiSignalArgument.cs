namespace Lumyte.UI;

internal sealed record UiSignalArgument(object Source, Func<bool> Read);
