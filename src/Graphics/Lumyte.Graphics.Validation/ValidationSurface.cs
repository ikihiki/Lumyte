using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationSurface : ValidationResource<IGraphicsSurface>, IGraphicsSurface
{
    internal ValidationSurface(ValidationDevice device, IGraphicsSurface raw)
        : base(device, raw, device, false)
    {
    }

    public SurfaceCapabilities GetCapabilities()
    {
        ValidateAlive();
        return Raw.GetCapabilities();
    }

    public IGraphicsSwapchain CreateSwapchain(SwapchainDesc desc)
    {
        ValidateAlive();
        Device.Tracker.ValidateRelease(this);
        return Device.OwnCreated(Raw.CreateSwapchain(desc), raw => new ValidationSwapchain(Device, raw, this));
    }
}
