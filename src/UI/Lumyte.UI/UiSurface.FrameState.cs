namespace Lumyte.UI;

/// <summary>Exposes existing update state to the standard frame owner without scanning declarations.</summary>
public sealed partial class UiSurface
{
    internal bool HasPendingFrameUpdates => _pendingRebuild is not null || _rootElement.IsLayoutDirty || SignalRevision != CommittedSignalRevision;

    internal bool HasAnimationRegistrations => _animatedElements.Count != 0;
}
