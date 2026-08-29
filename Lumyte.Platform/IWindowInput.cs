using Lumyte.Input;

namespace Lumyte.Platform;

public interface IWindowInput
{
    IWindow Window { get; }

    IReadOnlyList<IKeyboard> Keyboards { get; }

    IReadOnlyList<IMouse> Mice { get; }

    IReadOnlyList<ITouchscreen> Touchscreens { get; }

    ITextInputContext TextInput { get; }
}
