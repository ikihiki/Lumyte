using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Tests;

internal partial struct GetterOnlyArguments(uint number) : IShaderArguments
{
    private Wire _wire = new() { Number = number };

    public partial uint Number { get; }

    private struct Wire
    {
        public uint Number;
    }
}

internal partial struct ReadonlyStorageArguments(uint number) : IShaderArguments
{
    private readonly Wire _wire = new(number);

    public readonly partial uint Number { get; }

    private readonly struct Wire(uint number)
    {
        public readonly uint Number = number;
    }
}

internal readonly partial struct ReadonlyOuterArguments(uint number) : IShaderArguments
{
    private readonly Wire _wire = new(number);

    public partial uint Number { get; }

    private readonly struct Wire(uint number)
    {
        public readonly uint Number = number;
    }
}

internal partial struct RestrictedSetterArguments : IShaderArguments
{
    private Wire _wire;

    public partial uint Number { get; private set; }

    public partial uint ReadonlyNumber { readonly get; private set; }

    internal partial uint NumberFromInternal { get; set; }

    internal uint HiddenNumber => PrivateNumber;

    private partial uint PrivateNumber { get; set; }

    internal void Change(uint number)
    {
        Number = number;
        ReadonlyNumber = number + 1;
        PrivateNumber = number + 2;
    }

    private struct Wire
    {
        public uint Number;

        public uint ReadonlyNumber;

        public uint NumberFromInternal;

        public uint PrivateNumber;
    }
}

internal partial struct ManagedOuterArguments : IShaderArguments
{
    private Wire _wire;

    public partial uint Number { get; set; }

    public IGpuRef<uint>? Reference { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Wire
    {
        public uint Number;

        public ShaderReferenceSlot Reference;
    }
}

/// <summary>Compiles defining partial properties and executes their private-wire transfers.</summary>
public sealed class PartialPropertyCompatibilityTests
{
    /// <summary>Get-only properties preserve both field and outer-struct readonly shapes.</summary>
    [Fact]
    public void ReadonlyPrivateWireValuesAreWritten()
    {
        var mutable = new GetterOnlyArguments(17);
        var storage = new ReadonlyStorageArguments(23);
        var outer = new ReadonlyOuterArguments(31);
        Assert.Equal(17u, mutable.Number);
        Assert.Equal(23u, storage.Number);
        Assert.Equal(31u, outer.Number);
        Assert.Equal(17u, ReadNumber(in mutable));
        Assert.Equal(23u, ReadNumber(in storage));
        Assert.Equal(31u, ReadNumber(in outer));
    }

    /// <summary>Access restrictions and explicitly readonly getters match their definitions.</summary>
    [Fact]
    public void RestrictedAndMutablePropertiesWriteUpdatedWire()
    {
        RestrictedSetterArguments value = default;
        value.Change(43);
        Assert.Equal(43u, value.Number);
        Assert.Equal(44u, value.ReadonlyNumber);
        Assert.Equal(45u, value.HiddenNumber);
        Assert.Equal(43u, ReadNumber(in value));
        value.NumberFromInternal = 59;
        Assert.Equal(59u, value.NumberFromInternal);
    }

    /// <summary>A managed outer value writes numeric wire bytes and opaque reference slots.</summary>
    [Fact]
    public void ManagedOuterMutableSetterWritesPrivateWire()
    {
        var value = new ManagedOuterArguments { Number = 61 };
        ShaderValueSnapshot first = IShaderArguments.Capture(in value);
        value.Number = 67;
        ShaderValueSnapshot second = IShaderArguments.Capture(in value);
        Assert.Equal(61u, MemoryMarshal.Read<uint>(first.Data.Span));
        Assert.Equal(67u, MemoryMarshal.Read<uint>(second.Data.Span));
        Assert.Equal(20, second.Data.Length);
        Assert.Single(second.Values);
        Assert.Null(second.Values[0].Reference);
    }

    private static uint ReadNumber<T>(in T value)
        where T : struct, IShaderArguments
        => MemoryMarshal.Read<uint>(IShaderArguments.Capture(in value).Data.Span);
}
