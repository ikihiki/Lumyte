using System.Diagnostics;
using System.Security;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Tests the imported targets against clean, isolated compiler source trees.</summary>
public sealed class ShaderToolOutputPathTests
{
    /// <summary>Execution and incremental inputs follow the compiler's evaluated output directory.</summary>
    /// <param name="artifacts">Whether to use SDK artifacts output rather than BaseOutputPath.</param>
    /// <returns>The isolated build verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlternateOutputUsesSelectedCompilerAndDependenciesAsync(bool artifacts)
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "tools", "shaders", "Slang.targets")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        string directory = Path.Combine(Path.GetTempPath(), "lumyte-tool-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Only this source closure is copied: no default bin/obj can conceal a stale-tool fallback.
            string[] sources = ["tools/shaders/Lumyte.ShaderCompiler", "src/Graphics/Lumyte.Graphics.Shaders", "src/Graphics/Lumyte.Graphics.Abstractions"];
            foreach (string source in sources)
            {
                foreach (string file in Directory.EnumerateFiles(Path.Combine(root, source), "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(Path.Combine(root, source), file);
                    if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                    {
                        continue;
                    }

                    string destination = Path.Combine(directory, source, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                }
            }

            string shaders = Path.Combine(directory, "tools", "shaders");
            Directory.CreateDirectory(Path.Combine(shaders, "include"));
            File.Copy(Path.Combine(root, "tools", "shaders", "Slang.targets"), Path.Combine(shaders, "Slang.targets"));
            File.Copy(Path.Combine(root, "tools", "shaders", "include", "lumyte.slang"), Path.Combine(shaders, "include", "lumyte.slang"));
            File.Copy(Path.Combine(root, "global.json"), Path.Combine(directory, "global.json"));
            File.Copy(Path.Combine(root, "mise.toml"), Path.Combine(directory, "mise.toml"));
            string output = SecurityElement.Escape(Path.Combine(directory, "alternate-output"));
            string properties = $"""
                <Project><PropertyGroup>
                  <Nullable>enable</Nullable>
                  <UseArtifactsOutput>{artifacts.ToString().ToLowerInvariant()}</UseArtifactsOutput>
                  {(artifacts ? $"<ArtifactsPath>{output}</ArtifactsPath>" : $"<BaseOutputPath>{output}/$(MSBuildProjectName)/</BaseOutputPath>")}
                </PropertyGroup></Project>
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.props"), properties);
            await File.WriteAllTextAsync(Path.Combine(directory, "fixture.slang"), """[shader("compute")] [numthreads(1,1,1)] void main() {}""");
            string project = Path.Combine(directory, "fixture.proj");
            string fixture = """
                <Project>
                  <PropertyGroup><IntermediateOutputPath>shader-output/</IntermediateOutputPath></PropertyGroup>
                  <ItemGroup><SlangShader Include="fixture.slang" ResourceName="fixture" Stage="compute" Targets="spirv" /></ItemGroup>
                  <Import Project="tools/shaders/Slang.targets" />
                </Project>
                """;
            await File.WriteAllTextAsync(project, fixture);
            string compilerProject = Path.Combine(shaders, "Lumyte.ShaderCompiler", "Lumyte.ShaderCompiler.csproj");
            await RunAsync(directory, "restore", compilerProject, "-p:Configuration=Release", "--verbosity:quiet");
            string compiler = Environment.GetEnvironmentVariable("PATH")!
                .Split(Path.PathSeparator)
                .Select(path => Path.Combine(path, OperatingSystem.IsWindows() ? "slangc.exe" : "slangc"))
                .First(File.Exists);
            string[] build = ["msbuild", project, "-t:CompileSlangShaders", "-v:minimal", "-nologo", "-nr:false", "-m:1", "-p:SlangCompilerPath=" + compiler];
            await RunAsync(directory, build);
            string artifact = Path.Combine(directory, "shader-output", "Slang", "fixture.lshader");
            Assert.True(File.Exists(artifact));
            Assert.False(Directory.Exists(Path.Combine(shaders, "Lumyte.ShaderCompiler", "bin")));
            DateTime first = File.GetLastWriteTimeUtc(artifact);
            await RunAsync(directory, build);
            Assert.Equal(first, File.GetLastWriteTimeUtc(artifact));

            // A populated but invalid default output must never be selected instead.
            string staleDefault = Path.Combine(shaders, "Lumyte.ShaderCompiler", "bin", "Release", "net10.0");
            Directory.CreateDirectory(staleDefault);
            await File.WriteAllTextAsync(Path.Combine(staleDefault, "Lumyte.ShaderCompiler.dll"), "stale default compiler trap");
            File.Delete(artifact);
            await RunAsync(directory, build);
            Assert.True(File.Exists(artifact));

            // Touch runtime implementations, never reference assemblies: compiler
            // recompilation would conceal a missing dependency incremental input.
            string[] assemblies = ["Lumyte.ShaderCompiler.dll", "Lumyte.Graphics.Shaders.dll", "Lumyte.Graphics.Abstractions.dll"];
            foreach (string assembly in assemblies)
            {
                string runtimeConfig = Assert.Single(Directory.GetFiles(Path.Combine(directory, "alternate-output"), "Lumyte.ShaderCompiler.runtimeconfig.json", SearchOption.AllDirectories));
                string selected = Path.Combine(Path.GetDirectoryName(runtimeConfig)!, assembly);
                Assert.True(File.Exists(selected));
                await Task.Delay(1100);
                DateTime updated = DateTime.UtcNow;
                File.SetLastWriteTimeUtc(selected, updated);
                foreach (string built in Directory.GetFiles(directory, assembly, SearchOption.AllDirectories))
                {
                    if (!built.StartsWith(staleDefault + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                        !Path.GetRelativePath(directory, built).Split(Path.DirectorySeparatorChar).Any(part => part is "ref" or "refint"))
                    {
                        // Keep implementation copies consistent when Build prepares the tool.
                        File.SetLastWriteTimeUtc(built, updated);
                    }
                }

                string selectedCompiler = Path.Combine(Path.GetDirectoryName(runtimeConfig)!, "Lumyte.ShaderCompiler.dll");
                DateTime compilerTime = File.GetLastWriteTimeUtc(selectedCompiler);
                DateTime previous = File.GetLastWriteTimeUtc(artifact);
                await RunAsync(directory, build);
                Assert.Equal(compilerTime, File.GetLastWriteTimeUtc(selectedCompiler));
                Assert.True(File.GetLastWriteTimeUtc(artifact) > previous, assembly + " must invalidate shader output.");
            }

            // Build a real packable shader consumer, then forbid every source build,
            // restore and compiler invocation. NoBuild must consume its embedded assembly.
            string consumer = Path.Combine(directory, "package");
            Directory.CreateDirectory(consumer);
            string consumerProject = Path.Combine(consumer, "fixture.csproj");
            string consumerXml = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup><SlangShader Include="../fixture.slang" ResourceName="fixture" Targets="spirv" /></ItemGroup>
                  <Import Project="../tools/shaders/Slang.targets" />
                </Project>
                """;
            await File.WriteAllTextAsync(consumerProject, consumerXml);
            await RunAsync(directory, "build", consumerProject, "-c", "Release", "-p:SlangCompilerPath=" + compiler);
            string consumerAssembly = Assert.Single(
                Directory.GetFiles(Path.Combine(directory, "alternate-output"), "fixture.dll", SearchOption.AllDirectories),
                path => !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar).Any(part => part is "ref" or "refint" or "obj"));
            byte[] assemblyBytes = await File.ReadAllBytesAsync(consumerAssembly);
            using (var embedded = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(assemblyBytes)))
            {
                System.Reflection.Metadata.MetadataReader metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(embedded);
                Assert.Contains(metadata.ManifestResources, handle => metadata.GetString(metadata.GetManifestResource(handle).Name) == "fixture.lshader");
            }

            string traps = """
                <Project>
                  <Target Name="ForbidSourceWork" BeforeTargets="Build;CoreCompile;Restore">
                    <Error Text="NoBuild packaging attempted a build or restore." />
                  </Target>
                </Project>
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.targets"), traps);
            string[] pack = ["pack", consumerProject, "-c", "Release", "--no-build", "--no-restore", "-o", Path.Combine(directory, "packages"), "-p:DotNetHostPath=" + Path.Combine(directory, "compiler-execution-trap"), "-p:SlangCompilerPath=" + Path.Combine(directory, "native-compiler-trap")];
            string consumerArtifact = Assert.Single(Directory.GetFiles(consumer, "fixture.lshader", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(directory, "alternate-output"), "fixture.lshader", SearchOption.AllDirectories)));
            DateTime artifactTime = File.GetLastWriteTimeUtc(consumerArtifact);
            await File.AppendAllTextAsync(Path.Combine(directory, "fixture.slang"), "\n// newer source must not compile during pack");
            File.SetLastWriteTimeUtc(Path.Combine(directory, "fixture.slang"), DateTime.UtcNow.AddMinutes(1));
            await RunAsync(directory, pack);
            Assert.Equal(artifactTime, File.GetLastWriteTimeUtc(consumerArtifact));

            // Missing intermediates and missing tool outputs are also valid: the final
            // assembly already contains the shader. Missing final assembly is not valid.
            File.Delete(consumerArtifact);
            string toolRuntime = Assert.Single(
                Directory.GetFiles(Path.Combine(directory, "alternate-output"), "Lumyte.ShaderCompiler.runtimeconfig.json", SearchOption.AllDirectories),
                path => Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar).Contains("Lumyte.ShaderCompiler"));
            string selectedTool = Path.Combine(Path.GetDirectoryName(toolRuntime)!, "Lumyte.ShaderCompiler.dll");
            File.Delete(selectedTool);
            string resolvedPath = Path.Combine(directory, "resolved-tool.txt");
            string verifyToolXml = """
                  <Target Name="VerifyExistingTool" DependsOnTargets="PrepareLumyteShaderCompiler">
                    <WriteLinesToFile File="resolved-tool.txt" Lines="$(_LumyteShaderTool)" Overwrite="true" />
                  </Target>
                </Project>
                """;
            await File.WriteAllTextAsync(project, fixture.Replace("</Project>", verifyToolXml, StringComparison.Ordinal));
            await RunAsync(directory, "msbuild", project, "-t:VerifyExistingTool", "-p:NoBuild=true", "-nologo");
            Assert.Equal(selectedTool, (await File.ReadAllTextAsync(resolvedPath)).Trim());
            Assert.False(File.Exists(selectedTool));
            File.Delete(Path.Combine(directory, "fixture.slang"));
            File.Delete(Assert.Single(Directory.GetFiles(Path.Combine(directory, "packages"), "*.nupkg")));
            await RunAsync(directory, pack);
            Assert.False(File.Exists(consumerArtifact));
            Assert.False(File.Exists(selectedTool));
            using (System.IO.Compression.ZipArchive package = System.IO.Compression.ZipFile.OpenRead(Assert.Single(Directory.GetFiles(Path.Combine(directory, "packages"), "*.nupkg"))))
            {
                using Stream packagedAssembly = package.GetEntry("lib/net10.0/fixture.dll")!.Open();
                using var copy = new MemoryStream();
                await packagedAssembly.CopyToAsync(copy);
                Assert.Equal(assemblyBytes, copy.ToArray());
            }

            File.Delete(consumerAssembly);
            string failure = await RunAsync(directory, false, pack);
            Assert.Contains("NU5026", failure);
            Assert.DoesNotContain("attempted a build or restore", failure);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task RunAsync(string directory, params string[] arguments) => await RunAsync(directory, true, arguments);

    private static async Task<string> RunAsync(string directory, bool success, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        string output = await stdout + await stderr;
        Assert.True(success ? process.ExitCode == 0 : process.ExitCode != 0, string.Join(' ', arguments) + Environment.NewLine + output);
        return output;
    }
}
