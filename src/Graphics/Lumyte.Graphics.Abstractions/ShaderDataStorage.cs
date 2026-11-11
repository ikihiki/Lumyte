namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns persistent CPU transfer bytes and independent registration identities.</summary>
/// <remarks>Storage is device-specific, non-owning with respect to GPU resources, and not thread-safe.
/// Numeric and reference updates avoid full logical-value conversion. Caller-managed registration
/// lifetime and CPU/GPU synchronization remain unchanged. No disposal is required.</remarks>
/// <typeparam name="T">The generated logical shader data type.</typeparam>
public abstract class ShaderDataStorage<T>
    where T : struct, IShaderData
{
    /// <summary>Gets the number of independently mutable elements.</summary>
    public abstract int Count { get; }

    /// <summary>Converts a complete logical value, preserving the old element on validation failure.</summary>
    /// <param name="index">The element index.</param>
    /// <param name="value">The replacement value. All references must originate from this device.</param>
    public abstract void Set(int index, in T value);

    /// <summary>Directly updates a supported numeric leaf without capturing other members.</summary>
    /// <typeparam name="TValue">The supported unmanaged numeric type.</typeparam>
    /// <param name="index">The element index.</param>
    /// <param name="member">The generated typed member token.</param>
    /// <param name="value">The replacement numeric value.</param>
    public abstract void SetValue<TValue>(int index, ShaderDataValueMember<T, TValue> member, in TValue value)
        where TValue : unmanaged;

    /// <summary>Validates and encodes one reference before atomically replacing its bytes and identity.</summary>
    /// <typeparam name="TReference">The referenced logical element type.</typeparam>
    /// <param name="index">The element index.</param>
    /// <param name="member">The generated typed member token.</param>
    /// <param name="value">A non-null backend-created reference from the same device.</param>
    public abstract void SetReference<TReference>(int index, ShaderDataReferenceMember<T, TReference> member, IGpuRef<TReference> value);
}
