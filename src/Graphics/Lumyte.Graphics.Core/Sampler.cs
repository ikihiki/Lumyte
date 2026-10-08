namespace Lumyte.Graphics;

/// <summary>
/// Owns an immutable sampler independently of texture views.
/// </summary>
public sealed class Sampler : GpuResource
{
    internal Sampler(IGraphicsDriver driver, object handle)
        : base(driver, handle)
    {
    }
}
