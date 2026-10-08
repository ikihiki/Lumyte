namespace Lumyte.Composition;

/// <summary>Marks a static method accepting its component and a named child collection.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ComposeSlotAttribute : Attribute
{
}
