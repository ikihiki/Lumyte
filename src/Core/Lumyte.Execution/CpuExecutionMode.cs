namespace Lumyte.Execution;

/// <summary>CPU scheduling backend.</summary>
public enum CpuExecutionMode
{
    /// <summary>Cooperative.</summary>
    Cooperative,

    /// <summary>ThreadPool.</summary>
    ThreadPool,
}
