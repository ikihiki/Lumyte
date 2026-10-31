namespace Lumyte.Composition;

/// <summary>Marks a partial component or static construction function for factory generation.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ComposableAttribute : Attribute
{
    /// <summary>Gets or sets the outer factory class name; null uses the assembly default or Compose.</summary>
    public string? Factory { get; set; }

    /// <summary>Gets or sets the generated factory name; null uses the component or function name.</summary>
    public string? Name { get; set; }
}
