namespace Lumyte.Graphics.Abstractions;

/// <summary>Identifies the actual backend owner and allocation of a native resource or program.</summary>
/// <remarks>
/// This optional contract must be implemented by the backend allocation owner, not inferred from
/// artifact metadata. Compare allocation identities using object equality within the same device
/// lifetime, after validating both origins. Views share their texture's allocation identity; their
/// subresource range remains in IGraphicsTextureView.Info. Identity does not transfer ownership,
/// prove GPU completion, or grant permission to use a resource concurrently with disposal.
/// Devices have irreversible lifetimes: a replacement device requires new native resources.
/// </remarks>
public interface IGraphicsNativeOrigin
{
    /// <summary>Gets the actual creating device, or its corresponding validation-domain device.</summary>
    IGraphicDevice Device { get; }

    /// <summary>Gets an opaque identity for the underlying allocation or owned executable program.</summary>
    object AllocationIdentity { get; }

    /// <summary>Rejects a disposed resource, expired surface lease, or lost/disposed creating device.</summary>
    void ValidateOrigin();
}
