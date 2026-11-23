using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Reference cardinality belongs to the validation adapter, not raw encoders.</summary>
[Collection("Static codec counters")]
public sealed class ReferenceCountLayerTests
{
    private static int _declared;
    private static int _getters;
    private static int _calls;

    /// <summary>Impossible declarations are rejected before the original codec executes.</summary>
    /// <param name="count">The impossible static declaration.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void InvalidDeclarationStopsBeforeCodec(int count)
    {
        _declared = count;
        _getters = 0;
        _calls = 0;
        using var device = new ValidationDevice(new TypedBackend());
        Type contextType = typeof(ValidationDevice).Assembly.GetType("Lumyte.Graphics.Validation.ValidationWriteContext", true)!;
        object context = Activator.CreateInstance(contextType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [device, null, null], null)!;
        Type adapterType = typeof(ValidationDevice).Assembly.GetType("Lumyte.Graphics.Validation.ValidationArguments`1", true)!.MakeGenericType(typeof(Value));
        var value = (IShaderArguments)Activator.CreateInstance(adapterType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [default(Value), context], null)!;
        var native = new TypedNativeWriter([]);
        Assert.Throws<ArgumentOutOfRangeException>(() => value.Write(ref native));
        Assert.Equal(1, _getters);
        Assert.Equal(0, _calls);
    }

    /// <summary>Underproduction completes no validation callback even with an exact byte total.</summary>
    [Fact]
    public void UnderproducedCountRejectsBeforePreparation()
    {
        _declared = 1;
        _getters = 0;
        _calls = 0;
        int prepared = 0;
        using var device = new ValidationDevice(new TypedBackend());
        Type contextType = typeof(ValidationDevice).Assembly.GetType("Lumyte.Graphics.Validation.ValidationWriteContext", true)!;
        Action<IShaderReferenceMetadata> prepare = _ => prepared++;
        object context = Activator.CreateInstance(contextType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [device, null, prepare], null)!;
        Type adapterType = typeof(ValidationDevice).Assembly.GetType("Lumyte.Graphics.Validation.ValidationArguments`1", true)!.MakeGenericType(typeof(Value));
        var value = (IShaderArguments)Activator.CreateInstance(adapterType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [default(Value), context], null)!;
        var native = new TypedNativeWriter([]);
        Assert.Throws<InvalidOperationException>(() => value.Write(ref native));
        Assert.Equal(1, _getters);
        Assert.Equal(1, _calls);
        Assert.Equal(0, prepared);
    }

    /// <summary>An undeclared second slot fails before its native writer call.</summary>
    [Fact]
    public void OverproducedCountRejectsBeforeExtraNativeReference()
    {
        var backend = new TypedBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsSampler sampler = device.CreateSampler(new());
        using IArgumentTable table = device.CreateArgumentTable(new() { SamplerCapacity = 1 });
        var original = new Pair(table.WriteSampler(0, sampler));
        Type contextType = typeof(ValidationDevice).Assembly.GetType("Lumyte.Graphics.Validation.ValidationWriteContext", true)!;
        object context = Activator.CreateInstance(contextType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [device, null, null], null)!;
        Type adapterType = typeof(ValidationDevice).Assembly.GetType("Lumyte.Graphics.Validation.ValidationArguments`1", true)!.MakeGenericType(typeof(Pair));
        var value = (IShaderArguments)Activator.CreateInstance(adapterType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [original, context], null)!;
        var native = new TypedNativeWriter(backend.NativeReferences);
        Assert.Throws<InvalidOperationException>(() => value.Write(ref native));
        Assert.Single(backend.NativeReferences);
    }

    private readonly struct Pair(IGpuRef<IGraphicsSampler> reference) : IShaderArguments
    {
        public static string ShaderTypeName => "Pair";

        public static int TransferSize => 32;

        public static int ReferenceCount => 1;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            writer.WriteReference(reference);
            writer.WriteReference(reference);
        }
    }

    private readonly struct Value : IShaderArguments
    {
        public static string ShaderTypeName => "CountLayerValue";

        public static int TransferSize => 32;

        public static int ReferenceCount
        {
            get
            {
                _getters++;
                return _declared;
            }
        }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            _calls++;
            writer.WriteValue(new byte[32]);
        }
    }
}
