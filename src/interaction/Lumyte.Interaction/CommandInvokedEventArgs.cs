namespace Lumyte.Interaction;

public sealed class CommandInvokedEventArgs(Command command) : EventArgs
{
    public Command Command { get; } = command;
}
