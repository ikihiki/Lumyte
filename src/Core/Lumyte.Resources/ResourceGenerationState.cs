namespace Lumyte.Resources;

// Publication is tracked by the manager map, independently of a generation's lifetime.
internal enum ResourceGenerationState
{
    Building,
    Ready,
    Releasing,
    CleanupFailed,
    RetryingCleanup,
    Released,
}
