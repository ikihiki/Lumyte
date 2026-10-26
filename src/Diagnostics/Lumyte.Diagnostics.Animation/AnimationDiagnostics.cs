namespace Lumyte.Diagnostics.Animation;

/// <summary>Generated diagnostic operations over the scoped animation registry.</summary>
public sealed partial class AnimationDiagnostics(AnimationDiagnosticRegistry registry)
{
    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "list", DisplayName = "List animation targets")]
    private DiagnosticResult<AnimationDiagnosticTargetList> List()
        => DiagnosticResult<AnimationDiagnosticTargetList>.Success(new(registry.List()));

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "read", DisplayName = "Read animation target")]
    private DiagnosticResult<AnimationDiagnosticSnapshot> Read(string id)
        => Find(id, out AnimationDiagnosticRegistry.Target? target)
            ?? DiagnosticResult<AnimationDiagnosticSnapshot>.Success(target!.Snapshot());

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "play", DisplayName = "Play or resume animation")]
    private DiagnosticResult<AnimationDiagnosticSnapshot> Play(string id) => Mutate(id, static target => target.Play());

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "pause", DisplayName = "Pause animation")]
    private DiagnosticResult<AnimationDiagnosticSnapshot> Pause(string id) => Mutate(id, static target => target.Pause());

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "stop", DisplayName = "Stop animation")]
    private DiagnosticResult<AnimationDiagnosticSnapshot> Stop(string id) => Mutate(id, static target => target.Stop());

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "seek", DisplayName = "Seek playback")]
    private DiagnosticResult<AnimationDiagnosticSnapshot> Seek(string id, [DiagnosticArgument(Minimum = 0)] long positionTicks)
        => Mutate(id, target => target.Seek(positionTicks));

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "set-speed", DisplayName = "Set animation speed")]
    private DiagnosticResult<AnimationDiagnosticSnapshot> SetSpeed(string id, [DiagnosticArgument(Minimum = 0)] double speed)
        => Mutate(id, target => target.SetSpeed(speed));

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "set-trigger", DisplayName = "Set animation trigger")]
    private DiagnosticResult<AnimationDiagnosticSnapshot> SetTrigger(string id, string trigger)
        => Mutate(id, target => target.SetTrigger(trigger));

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "clear-triggers", DisplayName = "Clear animation triggers")]
    private DiagnosticResult<AnimationDiagnosticSnapshot> ClearTriggers(string id)
        => Mutate(id, static target => target.ClearTriggers());

    private DiagnosticResult<AnimationDiagnosticSnapshot> Mutate(string id, Func<AnimationDiagnosticRegistry.Target, bool> action)
    {
        DiagnosticResult<AnimationDiagnosticSnapshot>? missing = Find(id, out AnimationDiagnosticRegistry.Target? target);
        if (missing != null)
        {
            return missing;
        }

        try
        {
            return action(target!)
                ? DiagnosticResult<AnimationDiagnosticSnapshot>.Success(target!.Snapshot())
                : DiagnosticResult<AnimationDiagnosticSnapshot>.Reject("unsupported", "The target does not support this operation in its current state.");
        }
        catch (ArgumentException)
        {
            return DiagnosticResult<AnimationDiagnosticSnapshot>.Reject("invalid-value", "The value is outside the animation contract.");
        }
        catch (InvalidOperationException)
        {
            return DiagnosticResult<AnimationDiagnosticSnapshot>.Reject("invalid-state", "The animation target rejected this operation in its current state.");
        }
    }

    private DiagnosticResult<AnimationDiagnosticSnapshot>? Find(string id, out AnimationDiagnosticRegistry.Target? target)
    {
        if (!registry.TryGet(id, out target))
        {
            return DiagnosticResult<AnimationDiagnosticSnapshot>.Reject("not-found", "The animation target is not registered.");
        }

        return null;
    }
}
