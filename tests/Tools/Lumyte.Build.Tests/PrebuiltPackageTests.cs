using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Packages the real prebuilt Drawing shader payload without invoking compilers.</summary>
public sealed class PrebuiltPackageTests
{
    /// <summary>NoBuild packaging preserves embedded shaders and rejects missing final payloads.</summary>
    /// <returns>The completed actual SDK package assertions.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task ActualDrawingPackageConsumesOnlyPrebuiltShaderPayloadAsync()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Lumyte.slnx")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar)) ?? throw new DirectoryNotFoundException("Repository root not found.");
        }

        string configuration = typeof(PrebuiltPackageTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string project = Path.Combine(root, "src", "Graphics", "Lumyte.Graphics.Drawing", "Lumyte.Graphics.Drawing.csproj");
        string directory = Path.GetDirectoryName(project)!;
        string assembly = Path.Combine(directory, "bin", configuration, "net10.0", "Lumyte.Graphics.Drawing.dll");
        Assert.True(File.Exists(assembly), "Compile the host before running this prebuilt Integration contract: " + assembly);
        byte[] original = await File.ReadAllBytesAsync(assembly);
        string[] embedded;
        using (var pe = new PEReader(new MemoryStream(original)))
        {
            MetadataReader metadata = pe.GetMetadataReader();
            embedded = metadata.ManifestResources.Select(handle => metadata.GetManifestResource(handle))
                .Where(resource => resource.Implementation.IsNil)
                .Select(resource => metadata.GetString(resource.Name)).ToArray();
            Assert.Contains(embedded, name => name.EndsWith(".lshader", StringComparison.Ordinal));
        }

        string[] shaderFiles = Directory.GetFiles(Path.Combine(directory, "obj", configuration), "*.lshader", SearchOption.AllDirectories);
        Assert.NotEmpty(shaderFiles);
        Assert.All(shaderFiles, path => Assert.Contains(Path.GetFileName(path), embedded));
        string tool = Path.Combine(root, "tools", "shaders", "Lumyte.ShaderCompiler", "bin", "Release", "net10.0", "Lumyte.ShaderCompiler.dll");
        string[] protectedFiles = [assembly, tool, .. shaderFiles];
        Dictionary<string, string> hashes = protectedFiles.ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        using var fixture = new BuildFixture();
        string[] arguments = ["pack", project, "-c", configuration, "--no-build", "--no-restore", "-nr:false", "-m:1", "-o", fixture.Output, "-p:DotNetHostPath=" + fixture.DotnetTool, "-p:SlangCompilerPath=" + fixture.Node];
        (int code, string output) = await PackAsync(root, fixture, arguments);
        Assert.True(code == 0, output);
        string package = Assert.Single(Directory.GetFiles(fixture.Output, "Lumyte.Graphics.Drawing.*.nupkg"));
        using (ZipArchive zip = ZipFile.OpenRead(package))
        {
            using Stream packaged = zip.GetEntry("lib/net10.0/Lumyte.Graphics.Drawing.dll")!.Open();
            using var bytes = new MemoryStream();
            await packaged.CopyToAsync(bytes);
            Assert.Equal(original, bytes.ToArray());
        }

        Assert.False(Directory.Exists(fixture.Log), "No compiler/tool trap may execute while packaging.");
        foreach ((string path, string hash) in hashes)
        {
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
        }

        // Ask the real project to package an absent prebuilt target in the
        // owned fixture directory. Never rename or modify canonical outputs.
        string missing = Path.Combine(fixture.Output, "missing", "Lumyte.Graphics.Drawing.dll");
        File.Delete(package);
        (code, output) = await PackAsync(root, fixture, [.. arguments, "-p:TargetPath=" + missing]);
        Assert.NotEqual(0, code);
        Assert.Contains("NU5026", output, StringComparison.Ordinal);
        Assert.False(File.Exists(missing));
        Assert.False(Directory.Exists(fixture.Log));
        foreach ((string path, string hash) in hashes)
        {
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
        }
    }

    private static async Task<(int Code, string Output)> PackAsync(string root, BuildFixture fixture, string[] arguments)
    {
        string dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var start = new ProcessStartInfo(dotnet) { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        start.Environment["LUMYTE_PREBUILT_CONSUMER"] = "1";
        start.Environment["LUMYTE_PREBUILT_ROOT"] = root;
        start.Environment["PARLEY_TEST_LOG"] = fixture.Log;
        start.Environment["LUMYTE_TEST_DOTNET_EXIT"] = "92";
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(TimeSpan.FromMinutes(3));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        return (process.ExitCode, await stdout + await stderr);
    }
}
