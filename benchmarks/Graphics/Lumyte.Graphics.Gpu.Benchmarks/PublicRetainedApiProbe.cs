namespace Lumyte.Graphics.Incremental.Benchmarks;

// Compatibility owner for the linked, completion-safe frame helper. Unknown submissions
// deliberately retain device/resources until process exit, including failure paths.
internal static class PublicRetainedApiProbe
{
    internal static readonly List<object> QuarantinedObjects = [];
}
