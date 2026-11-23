namespace Lumyte.UI.Application;

/// <summary>Owns optional integration wakeups.</summary>
public sealed partial class UiApplication
{
    internal void WakeOwner() => _dispatcher.Post(static _ => { }, null);
}
