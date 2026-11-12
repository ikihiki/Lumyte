namespace Lumyte.Graphics.Abstractions;

internal sealed record ArgumentRequest(Func<ResolvedArgument> Resolve, Action<object> Publish);
