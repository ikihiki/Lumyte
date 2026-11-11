namespace Lumyte.Graphics.Abstractions;

/// <summary>Marks application-defined structures serialized as read-only shader data.</summary>
public interface IShaderData : IShaderArguments
{
    /// <summary>Gets generated value-independent leaf metadata for persistent storage.</summary>
    /// <remarks>Handwritten codecs remain supported by logical span uploads. They must not claim
    /// generated metadata unless their member sequence and types obey that immutable schema.</remarks>
    static virtual IReadOnlyList<ShaderDataMember> StorageSchema => throw new NotSupportedException("Persistent storage requires generated shader data metadata.");
}
