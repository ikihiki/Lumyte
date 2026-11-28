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

    public object? NativePreparationIdentity => _value.NativePreparationIdentity;

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        int size = TransferSize;
        int referenceCount = ReferenceCount;
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (referenceCount < 0 || referenceCount > size / 16)
        {
            throw new ArgumentOutOfRangeException(nameof(referenceCount));
        }

        context.Diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields(in _value), ShaderTypeName);
        var adapter = new ValidationWriter<TWriter>(writer, context, size, referenceCount);
        _value.Write(ref adapter);
        adapter.Complete();
        context.Prepare?.Invoke(new ShaderDependencyMetadata(adapter.References));
        writer = adapter.Inner;
    }
}
