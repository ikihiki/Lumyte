namespace Lumyte.Composition;

/// <summary>Marks a partial component for delegate factory generation.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ComposableAttribute : Attribute
{
    /// <summary>Gets or sets the outer factory class name; null uses the assembly default or Compose.</summary>
    public string? Factory { get; set; }

    /// <summary>Gets or sets the factory property name; null uses the component name.</summary>
    public string? Name { get; set; }
}
