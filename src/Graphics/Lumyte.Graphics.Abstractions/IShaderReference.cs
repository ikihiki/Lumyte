namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides backend snapshot metadata.</summary>
public interface IShaderReference
{
    /// <summary>Gets the owning argument table identity.</summary>
    object Table { get; }

    /// <summary>Gets the backend resource represented by this registration.</summary>
    object Resource { get; }

    /// <summary>Gets the slot in its resource-kind namespace.</summary>
    uint Slot { get; }

    /// <summary>Gets the referenced byte offset.</summary>
    ulong OffsetInBytes { get; }

    /// <summary>Gets the referenced byte length.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Gets the original registered range offset, preserved by element references.</summary>
    ulong RegistrationOffsetInBytes { get; }

    /// <summary>Gets the original registered range length, preserved by element references.</summary>
    ulong RegistrationSizeInBytes { get; }

    /// <summary>Gets the logical element count.</summary>
    ulong Count { get; }

    /// <summary>Validates the registration identity and resource lifetime.</summary>
    void Validate();
}
