using Lumyte.Input;
using Lumyte.Platform;

namespace Lumyte.Interaction.Tests;

internal sealed class VirtualWindowInput(
    IReadOnlyList<IKeyboard>? keyboards = null,
    IReadOnlyList<IMouse>? mice = null,
    IReadOnlyList<ITouchscreen>? touchscreens = null,
    IWindow? window = null) : IWindowInput
{
    public IWindow Window { get; } = window ?? new VirtualWindow();

    public IReadOnlyList<IKeyboard> Keyboards { get; } = keyboards ?? [];

    public IReadOnlyList<IMouse> Mice { get; } = mice ?? [];

    public IReadOnlyList<ITouchscreen> Touchscreens { get; } = touchscreens ?? [];

    public ITextInputContext TextInput => null!;
}
