namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication
{
    // Shares the driver's queue/resource serialization boundary with renderer preparation and submission.
    internal ValueTask<T> WithGraphicsAsync<T>(Func<Lumyte.Graphics.Abstractions.IGraphicDevice, ValueTask<T>> action) => Driver.WithGraphicsAsync(action);
}
