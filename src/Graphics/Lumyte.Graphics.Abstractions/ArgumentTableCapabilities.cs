namespace Lumyte.Graphics.Abstractions;

internal static class ArgumentTableCapabilities
{
    internal static ArgumentTableBindingLimits ArgumentLimits(this IGraphicDevice device) => device.Caps.ArgumentTableBindingLimits;
}
