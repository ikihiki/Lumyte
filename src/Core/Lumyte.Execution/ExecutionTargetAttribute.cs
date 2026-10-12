namespace Lumyte.Execution;

/// <summary>Opts into generated execution declarations.</summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ExecutionTargetAttribute : Attribute
{
}
