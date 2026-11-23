using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed partial class ValidationCommandBuffer : IGraphicsNativeOrigin
{
    /// <inheritdoc/>
    void IGraphicsNativeOrigin.ValidateOrigin()
    {
        OutsidePass();
        if (Raw is not IGraphicsNativeOrigin origin)
        {
            throw new NotSupportedException("The wrapped command has no actual recording origin.");
        }

        origin.ValidateOrigin();
    }
}
