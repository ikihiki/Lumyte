using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal static class ShaderTransferDiagnostics
{
    internal static void Verify()
    {
        var arguments = new Arguments(73);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in arguments);
        ShaderValueSnapshot copy = snapshot.Copy();
        Require(Arguments.Writes == 1 && Arguments.Probes == 0);
        Require(MemoryMarshal.Read<uint>(copy.Data.Span) == 73);
        Require(snapshot.DiagnosticFields is { Target: null });
        IReadOnlyList<ShaderTransferField> fields = copy.GetDiagnosticFields();
        Require(Arguments.Probes == 1 && fields.Count == 1);
        Require(fields[0].Path == "WireValue" && fields[0].Offset == 0 && fields[0].Size == 4);
        Require(ReferenceEquals(fields, ShaderTransfer.Fields(in arguments)));
        Require(ReferenceEquals(fields, ShaderTransfer.Fields(typeof(Arguments))));
        Require(ReferenceEquals(fields, snapshot.GetDiagnosticFields()));
        Require(Arguments.Writes == 1 && Arguments.Probes == 1);
        var raw = new Raw(19);
        ShaderValueSnapshot rawSnapshot = ShaderTransfer.Capture(in raw);
        Require(rawSnapshot.GetDiagnosticFields().Count == 0);
        Require(MemoryMarshal.Read<uint>(rawSnapshot.Data.Span) == 19);
        Console.WriteLine("Native AOT static shader diagnostics, deferred metadata and single-write capture passed.");
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Native AOT shader diagnostic boundary failed.");
        }
    }

    private readonly record struct Raw(uint Value);

    // A managed application wrapper with a distinct private wire representation.
    private readonly struct Arguments(uint value) : IShaderArguments
    {
        private readonly string _application = "not shader bytes";
        private readonly uint _value = value;

        public static string ShaderTypeName => "PrivateWire";

        public static int TransferSize => sizeof(uint);

        public static int ReferenceCount => 0;

        public static IReadOnlyList<ShaderTransferField> TransferFields
        {
            get
            {
                Probes++;
                return [new("WireValue", 0, sizeof(uint), typeof(uint))];
            }
        }

        internal static int Writes { get; private set; }

        internal static int Probes { get; private set; }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            Require(_application.Length != 0);
            Writes++;
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in _value, 1)));
        }
    }
}
