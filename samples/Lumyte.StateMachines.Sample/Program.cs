using Lumyte.StateMachines;
using Lumyte.StateMachines.Sample;
using static Lumyte.StateMachines.ComposeStateMachines;

var input = new ConnectionInput();
State<ConnectionInput> idle = new State<ConnectionInput>("Idle").OnEnter(context => context.Log.Add("Enter Idle")).OnExit(context => context.Log.Add("Exit Idle"));
State<ConnectionInput> connecting = new State<ConnectionInput>("Connecting").OnEnter(context => context.Log.Add("Enter Connecting"));
State<ConnectionInput> ready = new State<ConnectionInput>("Ready").OnEnter(context => context.Log.Add("Enter Ready"));
StateMachineInstance<ConnectionInput, ConnectionTrigger> machine = Machine<ConnectionInput, ConnectionTrigger>(idle)[new Transition<ConnectionInput, ConnectionTrigger>(idle, connecting, ConnectionTrigger.Connect).When(context => context.Configured).Effect(context => context.Log.Add("Connect")).WithPriority(10), new Transition<ConnectionInput, ConnectionTrigger>(connecting, ready, ConnectionTrigger.Ready).When(context => context.Ready)].Build(input);
machine.Transitioned += transition => Console.WriteLine($"{transition.From.Name} -> {transition.To.Name}");
if (!machine.Fire(ConnectionTrigger.Connect))
{
    throw new InvalidOperationException("The configured connection should start.");
}

input.Ready = true;
if (!machine.CanFire(ConnectionTrigger.Ready) || !machine.Fire(ConnectionTrigger.Ready) || !ReferenceEquals(machine.CurrentState, ready))
{
    throw new InvalidOperationException("The connection should enter its ready state.");
}

foreach (string action in input.Log)
{
    Console.WriteLine(action);
}
