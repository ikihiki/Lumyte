namespace Lumyte.Pipelines;

/// <summary>Declares a custom immutable pure CPU payload with no placement, lease or other provenance-bearing child at any depth.</summary>
/// <remarks>This is an author contract, not a runtime proof. Do not hide Host, opaque placements, ValueLease, SchemaData or resource/session owners inside this marker. Use explicit supported typed recursive bundles for Host or opaque placements; keep leases and owners in scope parameters or cleanup tables, not payload children. Hiding provenance-bearing children violates this author contract even when no opaque device value is present. Core does not reflectively inspect arbitrary object fields.</remarks>
public interface ICpuPipelineValue;
