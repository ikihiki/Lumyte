namespace Lumyte.Composition;

/// <summary>Marks the child collection replaced by the generated indexer.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ComposeContentAttribute : Attribute
{
}
