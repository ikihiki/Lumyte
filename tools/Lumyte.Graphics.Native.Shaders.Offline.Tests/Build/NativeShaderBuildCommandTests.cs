using System.Text.Json;
using Lumyte.Graphics.Native.Shaders;
using Lumyte.Graphics.Native.Shaders.Offline;
using Lumyte.Graphics.Shaders.Offline;

namespace Lumyte.Graphics.Native.Shaders.Offline.Tests.Build;

public sealed class NativeShaderBuildCommandTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "LumyteNativeBuildTests", Guid.NewGuid().ToString("N"));

    public NativeShaderBuildCommandTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void RequestResolvesSourceAndImportsRelativeToItsFile()
    {
        string path = Path.Combine(directory, "compute.json");
        File.WriteAllText(path, """
            {
              "source": "shaders/compute.slang",
              "hostNamespace": "Example.Compute",
              "entryPoints": [{"name": "main", "stage": "Compute"}],
              "targets": [{"target": "Vulkan", "descriptorHeapAbi": "VulkanUnified"}],
              "includeDirectories": ["shared"],
              "defines": {"QUALITY": "2"}
            }
            """);

        NativeShaderBuildRequest request = NativeShaderBuildCommand.ReadRequest(path);

        Assert.Equal((Path.Combine(directory, "shaders", "compute.slang"),
            Path.Combine(directory, "shared"), "2", NativeShaderDescriptorHeapAbi.VulkanUnified),
            (request.SourcePath, Assert.Single(request.IncludeDirectories), request.Defines["QUALITY"],
                Assert.Single(request.Targets).DescriptorHeapAbi));
    }

    [Fact]
    public void RequestRejectsMisspelledBuildSettings()
    {
        string path = Path.Combine(directory, "compute.json");
        File.WriteAllText(path, """
            {"source":"compute.slang","hostNamespace":"Example",
             "entryPoints":[{"name":"main","stage":"Compute"}],
             "targets":[{"target":"Vulkan"}],"rootParameterNmae":"root"}
            """);

        JsonException error = Assert.Throws<JsonException>(() => NativeShaderBuildCommand.ReadRequest(path));

        Assert.Contains("rootParameterNmae", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidRequestLeavesPreviousGeneratedInputsIntact()
    {
        string request = Path.Combine(directory, "request.json");
        File.WriteAllText(request, "invalid JSON");
        string requests = Path.Combine(directory, "requests.txt");
        File.WriteAllLines(requests, [request]);
        string output = Path.Combine(directory, "output");
        await ShaderBuildOutputs.PublishAsync(output,
            new Dictionary<string, byte[]> { ["old/Input.native.resources.xml"] = [1, 2, 3] }, default);
        string oldInventory = File.ReadAllText(Path.Combine(output, "generated-resources.txt"));

        await Assert.ThrowsAsync<JsonException>(() => NativeShaderBuildCommand.RunAsync(
            ["--requests", requests, "--output", output, "--compiler", "unused-compiler"], default));

        Assert.Equal(oldInventory, File.ReadAllText(Path.Combine(output, "generated-resources.txt")));
    }

    [Fact]
    public async Task RemovingEveryRequestRemovesGeneratedInputsWithoutInvokingCompiler()
    {
        string requests = Path.Combine(directory, "requests.txt");
        File.WriteAllText(requests, string.Empty);
        string output = Path.Combine(directory, "output");
        await ShaderBuildOutputs.PublishAsync(output,
            new Dictionary<string, byte[]> { ["old/Input.native.resources.xml"] = [1, 2, 3] }, default);

        await NativeShaderBuildCommand.RunAsync(
            ["--requests", requests, "--output", output, "--compiler", "unused-compiler"], default);

        Assert.Empty(File.ReadAllLines(Path.Combine(output, "generated-resources.txt")));
        Assert.False(File.Exists(Path.Combine(output, "old", "Input.native.resources.xml")));
    }

    public void Dispose()
    {
        string root = Path.GetFullPath(directory);
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LumyteNativeBuildTests")) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(parent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unexpected test output directory.");
        }
        Directory.Delete(root, recursive: true);
    }
}
