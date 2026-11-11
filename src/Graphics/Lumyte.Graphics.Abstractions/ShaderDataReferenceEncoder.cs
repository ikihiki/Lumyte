namespace Lumyte.Graphics.Abstractions;

/// <summary>Validates and writes one backend-private reference without allocating a wire array.</summary>
/// <param name="member">The certified immutable logical member metadata.</param>
/// <param name="reference">The original typed registration identity.</param>
/// <param name="kind">The certified backend helper kind.</param>
/// <param name="destination">The 16-byte destination, used only during this call.</param>
/// <returns>Exactly 16 on success. Other counts are rejected before persistent state changes.</returns>
public delegate int ShaderDataReferenceEncoder(ShaderDataMember member, object reference, string kind, Span<byte> destination);
