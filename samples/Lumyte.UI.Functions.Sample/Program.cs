using Lumyte.Signals;
using Lumyte.UI;
using static Lumyte.UI.Functions.Sample.CounterUi;

var label = Signal.Create("Count");
int notified = -1;
using var surface = new UiSurface(Counter(label, onChanged: value => notified = value, key: "main"));
surface.SetViewport(new(120, 40));
surface.UpdateLayout();
surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(1, 1)));
surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(1, 1)));
label.Value = "Updated";
surface.UpdateTree();
Check("Updated: 1");
if (notified != 1)
{
    throw new InvalidOperationException("The callback did not receive the local signal value.");
}

surface.Rebuild(Counter("Rebuilt", start: 99, key: "main"));
surface.UpdateTree();
Check("Rebuilt: 1");
surface.Rebuild(Counter("New identity", start: 99, key: "other"));
surface.UpdateTree();
Check("New identity: 99");
var context = new UiContextKey<string>();
int initializations = 0;
string Initialize()
{
    initializations++;
    return "!";
}

using var provided = new UiSurface(Lumyte.UI.Compose.Provide(context, "Provided", ContextCounter(context, Initialize)));
provided.Rebuild(Lumyte.UI.Compose.Provide(context, "Updated", ContextCounter(context, Initialize)));
provided.UpdateTree();
var outer = (UiFunctionElement)provided.RootElement;
var inner = (UiFunctionElement)outer.Content;
if (initializations != 1 || inner.Content.Widget.AutomationName != "Updated!: 0")
{
    throw new InvalidOperationException("Common Use state or nested context propagation failed.");
}

Console.WriteLine("Function widgets passed: common Use state, inherited context, nested functions, static factories, pointer callback, reactive parameters, persistent local signals and key reset.");

void Check(string expected)
{
    var function = (UiFunctionElement)surface.RootElement;
    if (function.Content.Widget.AutomationName != expected)
    {
        throw new InvalidOperationException($"Expected {expected}, received {function.Content.Widget.AutomationName}.");
    }
}
