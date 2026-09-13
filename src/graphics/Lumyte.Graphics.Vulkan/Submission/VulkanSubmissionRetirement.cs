namespace Lumyte.Graphics.Vulkan;

// Controls the same recording and initialization storage from preparation through retirement.
// Implementations prepare all storage before submission; MarkSubmitted must not allocate or fail.
internal abstract class VulkanSubmissionRetirement(ulong value)
{
    private bool completionSignalKnown;
    private bool released;
    private bool rejected;

    internal ulong Value { get; } = value;
    internal bool Retained { get; private set; }

    internal void Retain(bool signalKnown)
    {
        Retained = true;
        completionSignalKnown = signalKnown;
        MarkSubmitted();
    }

    internal void RejectUnlessRetained()
    {
        if (Retained || rejected) { return; }
        rejected = true;
        Reject();
    }

    internal bool TryReleaseCompleted(ulong completed)
    {
        if (!Retained || !completionSignalKnown || Value > completed) { return false; }
        ReleaseAfterGpuUse();
        return true;
    }

    internal void ReleaseAfterGpuUse()
    {
        if (released) { return; }
        released = true;
        ReleaseNative();
    }

    protected abstract void MarkSubmitted();
    protected abstract void Reject();
    protected abstract void ReleaseNative();
}
