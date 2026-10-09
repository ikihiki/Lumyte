namespace Lumyte.Diagnostics;

/// <summary>Excludes a public result property from the wire schema.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DiagnosticIgnoreAttribute : Attribute
{
}
