namespace Lumyte.Execution;

/// <summary>Opts into generated execution declarations.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ExecutionRangeAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="ExecutionRangeAttribute"/> class.</summary>
    /// <param name="lengthMethod">The synchronous length method.</param>
    public ExecutionRangeAttribute(string lengthMethod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lengthMethod);
        LengthMethod = lengthMethod;
    }

    /// <summary>Gets the method evaluating the length.</summary>
    public string LengthMethod { get; }

    /// <summary>Gets or sets the generated work name.</summary>
    public string? Name { get; set; }
}
