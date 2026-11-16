using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Tests the explicit, optional reflection boundary independently of transfer serialization.</summary>
public sealed class ShaderTransferDiagnosticBoundaryTests
{
    /// <summary>Raw capture needs no reflection and reports unavailable metadata until explicitly prepared.</summary>
    [Fact]
    public void RawDiagnosticsAreExplicitAndPreserveRecursiveOffsets()
    {
        var value = new Outer { Nested = new() { Position = new(1, 2, 3) } };
        ShaderValueSnapshot snapshot = ShaderTransfer.Capture(in value);
        Assert.Empty(snapshot.GetDiagnosticFields());
        IReadOnlyList<ShaderTransferField> fields = ShaderTransfer.ReflectFields(typeof(Outer));
        ShaderTransferField field = Assert.Single(fields);
        Assert.Equal("Nested.Position", field.Path);
        Assert.Equal(12, field.Offset);
        Assert.Equal(12, field.Size);
        Assert.Equal(typeof(Vector3), field.ValueType);
        Assert.Same(fields, ShaderTransfer.Fields<Outer>());
        Assert.Same(fields, snapshot.GetDiagnosticFields());
        Assert.Same(fields, ShaderTransfer.ReflectFields(typeof(Outer)));
        Assert.Equal(32, snapshot.Data.Length);
    }

    /// <summary>Marshalling bool as a four-byte native field must not be confused with managed wire layout.</summary>
    [Fact]
    public void NonBlittableReflectionFailsInsteadOfReturningIncorrectOffsets()
    {
        Assert.Throws<ArgumentException>(() => ShaderTransfer.ReflectFields(typeof(WithBoolean)));
        Assert.Throws<ArgumentException>(() => ShaderTransfer.ReflectFields(typeof(string)));
        Assert.Throws<ArgumentNullException>(() => ShaderTransfer.ReflectFields(null!));
        var value = new WithBoolean { Enabled = true, Value = 3 };
        Assert.Equal(8, ShaderTransfer.Capture(in value).Data.Length);
        Assert.Empty(ShaderTransfer.Fields<WithBoolean>());
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct Outer
    {
        [FieldOffset(8)]
        public Inner Nested;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct Inner
    {
        [FieldOffset(4)]
        public Vector3 Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WithBoolean
    {
        public bool Enabled;
        public int Value;
    }
}
