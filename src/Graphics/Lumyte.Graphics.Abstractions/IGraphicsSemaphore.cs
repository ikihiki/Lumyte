namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns a binary GPU ordering signal; the caller explicitly selects every signal and consuming wait.</summary>
public interface IGraphicsSemaphore : IDisposable
{
}
