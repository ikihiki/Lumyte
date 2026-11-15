using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationSampler : ValidationResource<IGraphicsSampler>, IGraphicsSampler
{
    internal ValidationSampler(ValidationDevice device, IGraphicsSampler raw)
        : base(device, raw, device)
    {
    }

    public SamplerDesc Desc
    {
        get
        {
            ValidateAlive();
            return Raw.Desc;
        }
    }
}
