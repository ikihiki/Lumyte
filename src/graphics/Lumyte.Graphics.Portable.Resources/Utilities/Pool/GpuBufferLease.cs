namespace Lumyte.Graphics.Portable.Resources;

/// <summary>Identifies one buffer loan. Copying this reference does not create an additional loan.</summary>
/// <remarks>Return it explicitly to its pool. It does not implement automatic disposal or own GPU completion.</remarks>
public sealed class GpuBufferLease
{
    internal GpuBufferLease(ResourcePool<GpuBufferDescription, GpuBufferHandle>.Lease loan) { Loan = loan; }

    internal ResourcePool<GpuBufferDescription, GpuBufferHandle>.Lease Loan { get; }

    /// <summary>The borrowed buffer, accessible until this loan is returned.</summary>
    public GpuBufferHandle Handle => Loan.GetHandle();

    /// <summary>The complete creation description, retained as immutable metadata after return.</summary>
    public GpuBufferDescription Description => Loan.Description;
}
