using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares prepared arguments.</summary>
/// <typeparam name = "TArguments">The typed shader or resource contract.</typeparam>
public readonly struct PreparedArguments<TArguments> : IShaderArguments
    where TArguments : struct, IShaderArguments
{
    private readonly IReadOnlyList<IArgumentPiece> _pieces;

    internal PreparedArguments(IReadOnlyList<IArgumentPiece> pieces)
    {
        _pieces = pieces;
        NativePreparationIdentity = new object();
    }

    /// <summary>Gets root parameter.</summary>
    public static string RootParameter => TArguments.RootParameter;

    /// <summary>Gets shader type name.</summary>
    public static string ShaderTypeName => TArguments.ShaderTypeName;

    /// <summary>Gets transfer size.</summary>
    public static int TransferSize => TArguments.TransferSize;

    /// <summary>Gets reference count.</summary>
    public static int ReferenceCount => TArguments.ReferenceCount;

    /// <summary>Gets transfer fields.</summary>
    public static IReadOnlyList<ShaderTransferField> TransferFields => TArguments.TransferFields;

    /// <inheritdoc/>
    public object? NativePreparationIdentity { get; }

    /// <summary>Declares write.</summary>
    /// <param name = "writer">The writer.</param>
    /// <typeparam name = "TWriter">The typed shader or resource contract.</typeparam>
    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        foreach (IArgumentPiece piece in _pieces)
        {
            piece.Write(ref writer);
        }
    }
}
