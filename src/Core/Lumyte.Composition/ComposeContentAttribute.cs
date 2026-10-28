namespace Lumyte.Composition;

/// <summary>Marks the member receiving ordinary content from the generated indexer.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ComposeContentAttribute : Attribute
{
}
