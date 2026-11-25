using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

internal static class GraphicsComputeAdmission
{
    internal static void Validate(DeviceCaps caps, bool browser)
    {
        if (browser || caps.MaxComputeInvocationsPerWorkgroup == 0)
        {
            throw new NotSupportedException("The selected compute topology is unavailable on this device.");
        }
    }
}
