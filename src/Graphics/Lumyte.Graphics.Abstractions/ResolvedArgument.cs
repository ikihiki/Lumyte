namespace Lumyte.Graphics.Abstractions;

internal sealed record ResolvedArgument(ArgumentKey Key, Func<IArgumentTable, uint, object> Write);
