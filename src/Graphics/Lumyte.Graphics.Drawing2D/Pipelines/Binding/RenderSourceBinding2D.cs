using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class RenderSourceBinding2D
{
    private readonly Func<IDisposable>? _acquireTarget;

    internal RenderSourceBinding2D(IGraphicDevice device, IGraphicsTextureView target, IGraphicsOwnerContext context, Func<IDisposable>? acquireTarget = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is not IGraphicsNativeOrigin origin || !ReferenceEquals(origin.Device, device))
        {
            throw new ArgumentException("Target view belongs to another actual native device.", nameof(target));
        }

        origin.ValidateOrigin();
        Target = target;
        OwnerContext = context;
        _acquireTarget = acquireTarget;
    }

    internal IGraphicsTextureView Target { get; }

    internal IGraphicsOwnerContext OwnerContext { get; }

    internal IDisposable? AcquireTargetUse() => _acquireTarget?.Invoke();
}
