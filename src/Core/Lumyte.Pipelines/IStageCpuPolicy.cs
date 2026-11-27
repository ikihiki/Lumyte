namespace Lumyte.Pipelines;

/// <summary>Restricts CPU access when an extension binds a non-CPU execution domain.</summary>
public interface IStageCpuPolicy
{
    /// <summary>Gets a value indicating whether CPU reads are admitted.</summary>
    bool CanReadCpu { get; }

    /// <summary>Gets a value indicating whether CPU publication is admitted.</summary>
    bool CanPublishCpu { get; }
}
