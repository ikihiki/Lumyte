namespace Lumyte.Resources;

public sealed record ResourceHotReloadOptions
{
    public TimeSpan DebounceDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
