using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationShader : ValidationResource<IGraphicsShader>, IGraphicsShader
{
    internal ValidationShader(ValidationDevice device, IGraphicsShader raw)
        : base(device, raw, device)
    {
    }

    public ShaderArtifact Artifact
    {
        get
        {
            ValidateAlive();
            return Raw.Artifact;
        }
    }
}
