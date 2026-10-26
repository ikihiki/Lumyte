namespace Lumyte.Execution;

/// <summary>Indicates retained execution capacity is exhausted.</summary>
/// <remarks>Initializes a new instance of the <see cref="ExecutionQueueFullException"/> class.</remarks>
/// <param name="message">The reason.</param>
public sealed class ExecutionQueueFullException(string message) : InvalidOperationException(message)
{
}
