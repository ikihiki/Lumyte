namespace Lumyte.Composition;

/// <summary>Marks a writable field or property as an optional generated factory parameter.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
public sealed class ComposeParameterAttribute : Attribute;
