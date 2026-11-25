namespace Lumyte.Graphics.Pipelines;

/// <summary>Marks scheduler-owned transfer factories rather than caller-declared domains.</summary>
internal interface ITransferFactory
{
    /// <summary>Gets a value indicating whether this edge declares immutable source upload eligibility.</summary>
    bool RetainsImmutableSource => false;
}
