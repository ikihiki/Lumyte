using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationSemaphore : ValidationResource<IGraphicsSemaphore>, IGraphicsSemaphore
{
    internal ValidationSemaphore(ValidationDevice device, IGraphicsSemaphore raw)
        : base(device, raw, device)
    {
    }
}
