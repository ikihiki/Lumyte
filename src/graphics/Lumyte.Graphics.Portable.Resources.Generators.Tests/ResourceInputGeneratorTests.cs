using System.Xml.Linq;
using Lumyte.Graphics.Resources.Generators.Tests;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Lumyte.Graphics.Portable.Resources.Generators.Tests;

public sealed class ResourceInputGeneratorTests
{
    [Theory]
    [InlineData("<input namespace='Consumer' name='Inputs' group='0'><field name='Data' kind='Buffer' binding='0'/><field name='Image' kind='Texture' binding='0'/></input>", "Duplicate binding")]
    [InlineData("<input namespace='Consumer' name='Inputs' group='0'><field name='Data' kind='Buffer' binding='0'/><field name='DataOffset' kind='Texture' binding='1'/></input>", "member")]
    [InlineData("<input namespace='Consumer' name='Inputs' group='0'><field name='Data' kind='Pointer' binding='0'/></input>", "kind")]
    [InlineData("<input namespace='Consumer' name='Write' group='0'/>", "type name")]
    [InlineData("<input namespace='Consumer' name='Group' group='0'/>", "type name")]
    [InlineData("<input namespace='Consumer' name='AbiHash' group='0' abiHash='build'/>", "type name")]
    [InlineData("<input namespace='Consumer' name='Inputs' group='0' abiHash='build'><field name='AbiHash' kind='Buffer' binding='0'/></input>", "member")]
    [InlineData("<!DOCTYPE input SYSTEM 'https://example.invalid/schema'><input namespace='Consumer' name='Inputs' group='0'/>", "DTD")]
    public void InvalidMetadataProducesAnActionableDiagnostic(string schema, string reason)
    {
        var (_, result) = GeneratorCompilation.Generate(new ResourceInputGenerator(), "",
            ("inputs.portable.resources.xml", schema));

        Diagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal("LPRG001", error.Id);
        Assert.Contains(reason, error.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateInputTypesAreDiagnosedAcrossFiles()
    {
        const string schema = "<input namespace='Consumer' name='Inputs' group='0'/>";

        var (_, result) = GeneratorCompilation.Generate(new ResourceInputGenerator(), "",
            ("one.portable.resources.xml", schema), ("two.portable.resources.xml", schema));

        Assert.Contains("Duplicate generated type", Assert.Single(result.Diagnostics).GetMessage());
    }

    [Fact]
    public void EmptyBindingInputImplementsTheManagedContract()
    {
        const string source = "public static class ConsumerCheck { public static object Run() => new Consumer.Inputs(); }";
        var assembly = GeneratorCompilation.Compile(new ResourceInputGenerator(), source,
            ("inputs.portable.resources.xml", "<input namespace='Consumer' name='Inputs' group='3'/>"));

        object? actual = assembly.GetType("ConsumerCheck")!.GetMethod("Run")!.Invoke(null, null);

        Assert.IsAssignableFrom<IGpuBindingInputs>(actual);
    }

    [Theory]
    [InlineData("compiler-abi-v1")]
    [InlineData("quote\" slash\\ ampersand& less< greater> single'\n\r\t\u2028\u2029")]
    public void AbiIdentityIsAnExactConstantInCompiledConsumer(string abiHash)
    {
        string schema = new XElement("input", new XAttribute("namespace", "Consumer"),
            new XAttribute("name", "Inputs"), new XAttribute("group", 0),
            new XAttribute("abiHash", abiHash)).ToString();
        const string source = "public static class ConsumerCheck { public static string Run() { const string hash = Consumer.Inputs.AbiHash; return hash; } }";

        var assembly = GeneratorCompilation.Compile(new ResourceInputGenerator(), source,
            ("inputs.portable.resources.xml", schema));
        object? actual = assembly.GetType("ConsumerCheck")!.GetMethod("Run")!.Invoke(null, null);

        Assert.Equal(abiHash, actual);
    }

    [Fact]
    public void AbsentAbiIdentityKeepsTheTypeNameAvailable()
    {
        const string source = "public static class ConsumerCheck { public static uint Run() => Consumer.AbiHash.Group; }";

        var assembly = GeneratorCompilation.Compile(new ResourceInputGenerator(), source,
            ("inputs.portable.resources.xml", "<input namespace='Consumer' name='AbiHash' group='3'/>"));
        object? actual = assembly.GetType("ConsumerCheck")!.GetMethod("Run")!.Invoke(null, null);

        Assert.Equal(3u, actual);
    }
}
