using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks direct argument serialization without a codec registry.</summary>
public sealed class ShaderArgumentsTests
{
    /// <summary>Checks that a handwritten type captures a stable snapshot without registration.</summary>
    [Fact]
    public void HandwrittenImplementationRequiresNoRegistration()
    {
        var value = new ManualArguments(17);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        value = new(29);

        Assert.Equal("arguments", snapshot.RootParameter);
        Assert.Equal("ManualArguments", snapshot.ShaderTypeName);
        ShaderValue member = Assert.Single(snapshot.Values);
        Assert.Equal("Value", member.Path);
        Assert.Equal(17u, BinaryPrimitives.ReadUInt32LittleEndian(member.Data.Span));
        Assert.Equal(29u, value.Value);
    }

    /// <summary>Checks static dispatch of a custom root name.</summary>
    [Fact]
    public void StaticRootParameterOverridesDefault()
    {
        var value = new CustomRootArguments(23);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);

        Assert.Equal("scene", snapshot.RootParameter);
        Assert.Equal("SceneArguments", snapshot.ShaderTypeName);
        Assert.Equal(23u, BinaryPrimitives.ReadUInt32LittleEndian(Assert.Single(snapshot.Values).Data.Span));
    }

    /// <summary>Checks generated implementations for root and buffer values.</summary>
    [Fact]
    public void GeneratedRootAndShaderDataImplementTheSameContract()
    {
        var camera = new BindingCameraArguments(Matrix4x4.Identity);
        var data = new BindingVector(new Vector3(11, 12, 13));

        ShaderValueSnapshot root = IShaderArguments.Capture(in camera);
        ShaderValueSnapshot element = IShaderArguments.Capture(in data);

        Assert.Equal("BindingCameraArguments", root.ShaderTypeName);
        Assert.Equal("ViewProjection", Assert.Single(root.Values).Path);
        Assert.Equal(64, root.Values[0].Data.Length);
        Assert.Equal("BindingVector", element.ShaderTypeName);
        Assert.Equal("Value", Assert.Single(element.Values).Path);
        Assert.Equal(12, element.Values[0].Data.Length);
    }

    /// <summary>Checks that writing to a value-type writer mutates the caller's instance.</summary>
    [Fact]
    public void ValueTypeWriterIsPassedByReference()
    {
        var value = new BindingCameraArguments(Matrix4x4.Identity);
        CountingWriter writer = default;

        Write(in value, ref writer);

        Assert.Equal(1, writer.Count);
    }

    private static void Write<T>(in T value, ref CountingWriter writer)
        where T : struct, IShaderArguments => value.Write(ref writer);

    private readonly record struct ManualArguments(uint Value) : IShaderArguments
    {
        public static string ShaderTypeName => "ManualArguments";

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => writer.WriteValue("Value", Value);
    }

    private readonly record struct CustomRootArguments(uint Value) : IShaderArguments
    {
        public static string RootParameter => "scene";

        public static string ShaderTypeName => "SceneArguments";

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => writer.WriteValue("Value", Value);
    }

    private struct CountingWriter : IShaderValueWriter
    {
        public int Count { get; private set; }

        public void WriteValue<T>(string path, in T value)
            where T : unmanaged => Count++;

        public void WriteReference<T>(string path, IGpuRef<T>? value) => Count++;
    }
}
