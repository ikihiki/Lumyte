using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Checks explicit synchronization descriptions without issuing any GPU operation.</summary>
public static class SemaphoreValidation
{
    /// <summary>Checks wait stages and distinct same-submission semaphore identities.</summary>
    /// <param name="waits">The snapshotted GPU waits.</param>
    /// <param name="signals">The snapshotted completion signals.</param>
    public static void Submission(IReadOnlyList<SemaphoreWaitDesc> waits, IReadOnlyList<IGraphicsSemaphore> signals)
    {
        var seen = new HashSet<IGraphicsSemaphore>(ReferenceEqualityComparer.Instance);
        foreach (SemaphoreWaitDesc wait in waits)
        {
            ArgumentNullException.ThrowIfNull(wait);
            ArgumentNullException.ThrowIfNull(wait.Semaphore);
            if (wait.Stages == PipelineStage.None || (wait.Stages & ~PipelineStage.AllCommands) != 0 || !seen.Add(wait.Semaphore))
            {
                throw new ArgumentException("Waits require nonempty GPU stages and distinct semaphore identities.");
            }
        }

        foreach (IGraphicsSemaphore signal in signals)
        {
            ArgumentNullException.ThrowIfNull(signal);
            if (!seen.Add(signal))
            {
                throw new ArgumentException("A submission cannot repeat a semaphore or both wait and signal it.");
            }
        }
    }

    /// <summary>Checks distinct presentation wait identities.</summary>
    /// <param name="waits">The snapshotted presentation waits.</param>
    public static void Presentation(IReadOnlyList<IGraphicsSemaphore> waits)
    {
        var seen = new HashSet<IGraphicsSemaphore>(ReferenceEqualityComparer.Instance);
        foreach (IGraphicsSemaphore wait in waits)
        {
            ArgumentNullException.ThrowIfNull(wait);
            if (!seen.Add(wait))
            {
                throw new ArgumentException("Presentation waits must have distinct semaphore identities.");
            }
        }
    }
}
