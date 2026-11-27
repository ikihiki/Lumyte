using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal readonly struct EmptyNativePreparationArguments(object identity) : IShaderArguments
{
    public static string ShaderTypeName => "NoArguments";

    public static int TransferSize => 0;

    public static int ReferenceCount => 0;

    public object? NativePreparationIdentity => identity;

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
    }
}
