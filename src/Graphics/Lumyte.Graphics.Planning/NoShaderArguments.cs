using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares no root bytes; generic SetArguments still selects the command-local prepared entry.</summary>
public readonly struct NoShaderArguments : IShaderArguments
{
    /// <inheritdoc/>
    public static string ShaderTypeName => "NoShaderArguments";

    /// <inheritdoc/>
    public static int TransferSize => 0;

    /// <inheritdoc/>
    public static int ReferenceCount => 0;

    /// <inheritdoc/>
    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
    }
}
