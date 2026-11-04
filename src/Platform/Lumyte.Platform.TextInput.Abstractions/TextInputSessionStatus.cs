namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines text input session status.</summary>
/// <param name = "SessionId">The session id.</param>
/// <param name = "Generation">The generation.</param>
/// <param name = "IsActive">The is active.</param>
/// <param name = "IsComposing">The is composing.</param>
/// <param name = "Code">The code.</param>
public sealed record TextInputSessionStatus(Guid SessionId, ulong Generation, bool IsActive, bool IsComposing, string Code);
