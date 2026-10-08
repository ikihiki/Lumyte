namespace Lumyte.Composition;

/// <summary>Marks a writable member as a factory parameter.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ComposeParameterAttribute : Attribute
{
}
