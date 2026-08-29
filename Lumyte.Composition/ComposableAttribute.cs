namespace Lumyte.Composition;

/// <summary>Marks a partial class for declarative factory and content-indexer generation.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComposableAttribute : Attribute
{
    /// <summary>The generated static factory class. The assembly default is used when omitted.</summary>
    public string? Factory { get; set; }

    /// <summary>The generated factory method name. The component type name is used when omitted.</summary>
    public string? Name { get; set; }
}
