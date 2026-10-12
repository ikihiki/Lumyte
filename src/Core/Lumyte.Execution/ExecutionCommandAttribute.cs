namespace Lumyte.Execution;

/// <summary>Opts into generated execution declarations.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ExecutionCommandAttribute : Attribute
{
    /// <summary>Gets or sets the generated work name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the input packing mode.</summary>
    public ExecutionInputKind Input { get; set; }

    /// <summary>Gets or sets the generated one-reference sender group.</summary>
    public string? Sender { get; set; }

    /// <summary>Gets or sets a value indicating whether gets or sets whether a synchronous startup handler may return a pending operation.</summary>
    public bool AllowAwaitableResult { get; set; }
}
