namespace Lumyte.Graphics.WebGPU.Browser;

internal static class BrowserDeviceDisposal
{
    internal static void Destroy(Action destroy, Action release)
    {
        // Interop may fail before device.destroy() reaches the runtime. In that case,
        // releasing command storage would discard a still-uncertain submission's retention.
        destroy();
        release();
    }
}
