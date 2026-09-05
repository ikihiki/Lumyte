namespace Lumyte.Composition;

/// <summary>Marks the collection populated by the generated indexer.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class ComposeContentAttribute : Attribute;
