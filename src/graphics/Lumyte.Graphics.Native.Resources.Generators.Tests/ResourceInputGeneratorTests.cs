using System.Xml.Linq;
using Lumyte.Graphics.Resources.Generators.Tests;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Lumyte.Graphics.Native.Resources.Generators.Tests;

public sealed class ResourceInputGeneratorTests
{
    [Theory]
    [InlineData("<input namespace='Consumer' name='Inputs' size='8'><field name='Data' kind='GpuAddress' offset='4'/></input>", "fit")]
    [InlineData("<input namespace='Consumer' name='Inputs' size='16'><field name='Data' kind='GpuAddress' offset='0'/><field name='Image' kind='DescriptorIndex' offset='4'/></input>", "overlap")]
    [InlineData("<input namespace='Consumer' name='Inputs' size='8'><field name='Write' kind='GpuAddress' offset='0'/></input>", "member")]
    [InlineData("<input namespace='Consumer' name='Write' size='0'/>", "type name")]
    [InlineData("<input namespace='Consumer' name='Retain' size='0'/>", "type name")]
    [InlineData("<input namespace='Consumer' name='ByteSize' size='0'/>", "type name")]
    [InlineData("<input namespace='Consumer' name='AbiHash' size='0' abiHash='build'/>", "type name")]
    [InlineData("<input namespace='Consumer' name='Inputs' size='8' abiHash='build'><field name='AbiHash' kind='GpuAddress' offset='0'/></input>", "member")]
    [InlineData("<!DOCTYPE input SYSTEM 'https://example.invalid/schema'><input namespace='Consumer' name='Inputs' size='0'/>", "DTD")]
    public void InvalidMetadataProducesAnActionableDiagnostic(string schema, string reason)
    {
        var (_, result) = GeneratorCompilation.Generate(new ResourceInputGenerator(), "",
            ("inputs.native.resources.xml", schema));

        Diagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal("LNRG001", error.Id);
        Assert.Contains(reason, error.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateInputTypesAreDiagnosedAcrossFiles()
    {
        const string schema = "<input namespace='Consumer' name='Inputs' size='0'/>";

        var (_, result) = GeneratorCompilation.Generate(new ResourceInputGenerator(), "",
            ("one.native.resources.xml", schema), ("two.native.resources.xml", schema));

        Assert.Contains("Duplicate generated type", Assert.Single(result.Diagnostics).GetMessage());
    }

    [Fact]
    public void ScalarFieldsLeaveTheManagedInputEmpty()
    {
        const string source = "public static class ConsumerCheck { public static int Run() => Consumer.Inputs.ByteSize; }";
        var assembly = GeneratorCompilation.Compile(new ResourceInputGenerator(), source,
            ("inputs.native.resources.xml", "<input namespace='Consumer' name='Inputs' size='16'><field name='Tint' kind='Vector' offset='0' size='16'/></input>"));

        object? actual = assembly.GetType("ConsumerCheck")!.GetMethod("Run")!.Invoke(null, null);

        Assert.Equal(16, actual);
    }

    [Theory]
    [InlineData("compiler-abi-v1")]
    [InlineData("quote\" slash\\ ampersand& less< greater> single'\n\r\t\u2028\u2029")]
    public void AbiIdentityIsAnExactConstantInCompiledConsumer(string abiHash)
    {
        string schema = new XElement("input", new XAttribute("namespace", "Consumer"),
            new XAttribute("name", "Inputs"), new XAttribute("size", 0),
            new XAttribute("abiHash", abiHash)).ToString();
        const string source = "public static class ConsumerCheck { public static string Run() { const string hash = Consumer.Inputs.AbiHash; return hash; } }";

        var assembly = GeneratorCompilation.Compile(new ResourceInputGenerator(), source,
            ("inputs.native.resources.xml", schema));
        object? actual = assembly.GetType("ConsumerCheck")!.GetMethod("Run")!.Invoke(null, null);

        Assert.Equal(abiHash, actual);
    }

    [Fact]
    public void AbsentAbiIdentityKeepsTheTypeNameAvailable()
    {
        const string source = "public static class ConsumerCheck { public static int Run() => Consumer.AbiHash.ByteSize; }";

        var assembly = GeneratorCompilation.Compile(new ResourceInputGenerator(), source,
            ("inputs.native.resources.xml", "<input namespace='Consumer' name='AbiHash' size='0'/>"));
        object? actual = assembly.GetType("ConsumerCheck")!.GetMethod("Run")!.Invoke(null, null);

        Assert.Equal(0, actual);
    }
}
