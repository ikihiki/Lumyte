namespace Lumyte.Execution;

/// <summary>Opts into generated execution declarations.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ExecutionUpdateAttribute : Attribute
{
    /// <summary>Gets or sets the generated work name.</summary>
    public string? Name { get; set; }
}
