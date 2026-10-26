namespace Lumyte.Resources;

// Retained across failed release attempts so a successful output release is never repeated.
internal enum ResourceGenerationCleanupPhase
{
    Output,
    Resources,
    Complete,
}
