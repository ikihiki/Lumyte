namespace Lumyte.Graphics.Portable.Resources;

/// <summary>Identifies one texture loan. Copying this reference does not create an additional loan.</summary>
/// <remarks>Return it explicitly to its pool. It does not implement automatic disposal or own GPU completion.</remarks>
public sealed class GpuTextureLease
{
    internal GpuTextureLease(ResourcePool<GpuTextureDescription, GpuTextureHandle>.Lease loan) { Loan = loan; }

    internal ResourcePool<GpuTextureDescription, GpuTextureHandle>.Lease Loan { get; }

    /// <summary>The borrowed texture, accessible until this loan is returned.</summary>
    public GpuTextureHandle Handle => Loan.GetHandle();

    /// <summary>The complete creation description, retained as immutable metadata after return.</summary>
    public GpuTextureDescription Description => Loan.Description;
}
