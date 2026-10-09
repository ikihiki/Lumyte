using Lumyte.Animation;
using Lumyte.Animation.StateMachine.Sample;
using Lumyte.Core.Time;
using Lumyte.StateMachines;
using static Lumyte.Animation.ComposeAnimation;

var amount = AnimationChannel<float>.Create();
var action = StateMachineTrigger.Create();
var idle = new Tween<float>(0, 0, Duration.FromSeconds(1), AnimationInterpolators.Float, AnimationEasing.Linear);
var pulse = new Tween<float>(0, 1, Duration.FromSeconds(0.2), AnimationInterpolators.Float, AnimationEasing.Linear);
ComposeAnimation.Definitions.Timeline forward = Timeline()[Track<float>(amount, pulse)];
ComposeAnimation.Definitions.Timeline actionTimeline = Timeline()[
    Sequence()[
        Repeat(2)[Sequence()[forward, Reverse()[forward]]],
        Marker("Finished")]];
ComposeAnimation.Definitions.StateMachine<Motion, MotionInput> definition =
    StateMachine<Motion, MotionInput>(Motion.Idle)[
        State<Motion, MotionInput>(Motion.Idle, Timeline()[Track<float>(amount, idle)], wrap: AnimationWrapMode.Loop)[Transition<Motion, MotionInput>(Motion.Action, trigger: action)],
        State<Motion, MotionInput>(Motion.Action, actionTimeline)[Transition<Motion, MotionInput>(Motion.Idle, onCompleted: true)]];
var clock = new ManualClock();
AnimationStateMachine<Motion, MotionInput> machine = definition.Build(clock, new MotionInput(Enabled: true));
var output = new AnimationOutput();
var events = new List<AnimationStateEvent<Motion>>();
var transitions = new List<AnimationStateTransition<Motion>>();
machine.SetTrigger(action);
machine.Update(new MotionInput(Enabled: true), output, events, transitions);
Console.WriteLine($"State: {machine.CurrentState}");
output.Clear();
events.Clear();
transitions.Clear();
clock.Advance(Duration.FromSeconds(1));
bool changed = machine.Update(new MotionInput(Enabled: true), output, events, transitions);
if (!changed || machine.CurrentState != Motion.Idle || events.Count != 1 || transitions.Count != 1 || !output.TryGet(amount, out float value))
{
    throw new InvalidOperationException("The finite action must complete and return to idle.");
}

Console.WriteLine($"{transitions[0].From} -> {transitions[0].To}, value={value:F1}, marker={events[0].Occurrence.Event.Name}");
Duration completedBeforeTransition = transitions[0].ObservedAt - events[0].OccurredAt;
Console.WriteLine($"Marker preceded transition by {completedBeforeTransition.TotalSeconds:F1} seconds.");
