using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Abstractions;

/// <summary>Defines ITextInputService.</summary>
public interface ITextInputService : IDisposable
{
    /// <summary>Performs CreateSession.</summary>
    /// <param name="window">The window value.</param>
    /// <param name="client">The client value.</param>
    /// <returns>The operation result.</returns>
    ITextInputSession CreateSession(IWindow window, ITextInputClient client);
}
