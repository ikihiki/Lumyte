using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal readonly struct ValidationArguments<T>(in T value, ValidationWriteContext context) : IShaderArguments
    where T : struct, IShaderArguments
{
    private readonly T _value = value;

    public static string RootParameter => T.RootParameter;

    public static string ShaderTypeName => T.ShaderTypeName;

    public static int TransferSize => T.TransferSize;

    public static int ReferenceCount => T.ReferenceCount;

    public static IReadOnlyList<ShaderTransferField> TransferFields => T.TransferFields;

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        if (TransferSize <= 0 || ReferenceCount < 0 || ReferenceCount > TransferSize / 16)
        {
            throw new ArgumentException("Invalid static shader transfer shape.");
        }

        context.Diagnostic?.ValidateTransferLayout(TransferSize, ShaderTransfer.Fields(in _value), ShaderTypeName);
        var adapter = new ValidationWriter<TWriter>(writer, context, TransferSize, ReferenceCount);
        _value.Write(ref adapter);
        adapter.Complete();
        context.Prepare?.Invoke(new ShaderDependencyMetadata(adapter.References));
        writer = adapter.Inner;
    }
}
