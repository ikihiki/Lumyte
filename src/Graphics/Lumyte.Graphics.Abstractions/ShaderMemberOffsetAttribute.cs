namespace Lumyte.Graphics.Abstractions;

/// <summary>Declares a C#-authoritative byte offset for a logical shader wire member.</summary>
/// <param name="offset">The member offset relative to its containing wire structure.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ShaderMemberOffsetAttribute(int offset) : Attribute
{
    /// <summary>Gets the explicit C# wire byte offset.</summary>
    public int Offset { get; } = offset;
}
