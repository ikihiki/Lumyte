namespace Lumyte.UI.Transitions.Browser.Sample;

// The software GPU may take longer than the normal two-second window-opening deadline.
// Freeze only the injected manager's preparation clock; motion keeps real application deadlines.
internal sealed class PresentationTestClock : TimeProvider
{
    public override long GetTimestamp() => 0;
}
