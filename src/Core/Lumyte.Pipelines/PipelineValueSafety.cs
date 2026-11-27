namespace Lumyte.Pipelines;

/// <summary>Checks explicit payload shapes without reflection-based object traversal.</summary>
public static class PipelineValueSafety
{
    /// <summary>Checks known scalar values, canonical host values, explicit CPU payload declarations and recursive bundles.</summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="value">The immutable value.</param>
    /// <returns>Whether CPU access is explicitly declared safe.</returns>
    public static bool IsCpuReadable<T>(T value) => FixedValueMetadata.IsFixed(typeof(T)) || ValueShape.Capture(value, actual: true).CpuReadable;
}
