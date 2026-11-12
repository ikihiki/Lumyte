namespace Lumyte.Graphics.Abstractions;

internal sealed class ArgumentRequest(Func<ResolvedArgument> resolve, Action<object> resolved)
{
    private object? _reference;

    internal ResolvedArgument Resolve() => resolve();

    internal void Install(object reference) => _reference = reference;

    internal void NotifyResolved() => resolved(_reference ?? throw new InvalidOperationException("The native reference is not installed."));
}
