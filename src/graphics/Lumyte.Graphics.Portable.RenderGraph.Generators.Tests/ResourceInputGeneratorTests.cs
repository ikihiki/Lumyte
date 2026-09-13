using System.Xml.Linq;
using Lumyte.Graphics.Resources.Generators.Tests;
using Microsoft.CodeAnalysis;

namespace Lumyte.Graphics.Portable.RenderGraph.Generators.Tests;

public sealed class ResourceInputGeneratorTests
{
    [Theory]
    [InlineData("<input namespace='Consumer' name='Inputs' group='0'><field name='A' kind='Buffer' binding='0'/><field name='B' kind='Texture' binding='0'/></input>", "Duplicate binding")]
    [InlineData("<input namespace='Consumer' name='Inputs' group='0'><field name='Data' kind='Buffer' binding='0'/><field name='DataOffset' kind='Texture' binding='1'/></input>", "member")]
    [InlineData("<input namespace='Consumer' name='Inputs' group='0'><field name='Address' kind='GpuAddress' binding='0'/></input>", "kind")]
    [InlineData("<input namespace='Consumer' name='Write' group='0'/>", "type name")]
    [InlineData("<input namespace='Consumer' name='Inputs' group='0' abiHash='a'><field name='AbiHash' kind='Buffer' binding='0'/></input>", "member")]
    [InlineData("<!DOCTYPE input SYSTEM 'https://example.invalid/schema'><input namespace='Consumer' name='Inputs' group='0'/>", "DTD")]
    public void InvalidMetadataProducesAnActionableDiagnostic(string schema, string reason)
    {
        var (_, result) = GeneratorCompilation.Generate(new ResourceInputGenerator(), "",
            ("inputs.portable.pass.resources.xml", schema));

        Diagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal("LPPG001", error.Id);
        Assert.Contains(reason, error.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateInputTypesAreDiagnosedAcrossFiles()
    {
        const string schema = "<input namespace='Consumer' name='Inputs' group='0'/>";

        var (_, result) = GeneratorCompilation.Generate(new ResourceInputGenerator(), "",
            ("one.portable.pass.resources.xml", schema), ("two.portable.pass.resources.xml", schema));

        Assert.Contains("Duplicate generated type", Assert.Single(result.Diagnostics).GetMessage());
    }

    [Fact]
    public void EmptyBindingInputImplementsThePassContract()
    {
        const string source = "public static class ConsumerCheck { public static object Run() => new Consumer.Inputs(); }";
        var assembly = GeneratorCompilation.Compile(new ResourceInputGenerator(), source,
            ("inputs.portable.pass.resources.xml", "<input namespace='Consumer' name='Inputs' group='3'/>"));

        object? actual = assembly.GetType("ConsumerCheck")!.GetMethod("Run")!.Invoke(null, null);

        Assert.IsAssignableFrom<IPortablePassBindingInputs>(actual);
    }

    [Fact]
    public void OrdinaryManagedSchemaIsLeftForTheResourceGenerator()
    {
        var (_, result) = GeneratorCompilation.Generate(new ResourceInputGenerator(), "",
            ("inputs.portable.resources.xml", "<input namespace='Consumer' name='Inputs' group='0'/>"));

        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    [Theory]
    [InlineData("compiler-abi-v1")]
    [InlineData("quote\" slash\\ ampersand& less< greater> single'\n\r\t\u2028\u2029")]
    public void AbiIdentityIsPreservedInCompiledConsumer(string abiHash)
    {
        string schema = new XElement("input", new XAttribute("namespace", "Consumer"),
            new XAttribute("name", "Inputs"), new XAttribute("group", 0),
            new XAttribute("abiHash", abiHash)).ToString();
        const string source = "public static class ConsumerCheck { public static string Run() => Consumer.Inputs.AbiHash; }";

        var assembly = GeneratorCompilation.Compile(new ResourceInputGenerator(), source,
            ("inputs.portable.pass.resources.xml", schema));

        Assert.Equal(abiHash, assembly.GetType("ConsumerCheck")!.GetMethod("Run")!.Invoke(null, null));
    }

    [Fact]
    public void ManagedAndPassGeneratorsSelectTheirOwnInputItems()
    {
        Assert.IsAssignableFrom<Resources.IGpuBindingInputs>(new Consumer.ManagedInputs());
        Assert.Equal(3u, Consumer.ManagedInputs.Group);
        Assert.Equal("pass-consumer-abi", Consumer.@namespace.@struct.AbiHash);
        Assert.Contains(typeof(IPortablePassBindingInputs), typeof(Consumer.@namespace.@struct).GetInterfaces());
    }
}
