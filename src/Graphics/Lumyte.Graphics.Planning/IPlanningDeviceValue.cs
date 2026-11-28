namespace Lumyte.Graphics.Planning;

/// <summary>Exposes planning provenance without decoding or reading GPU contents.</summary>
internal interface IPlanningDeviceValue
{
    /// <summary>Gets the immutable common-plan resource declaration.</summary>
    PlanResource Resource { get; }
}
