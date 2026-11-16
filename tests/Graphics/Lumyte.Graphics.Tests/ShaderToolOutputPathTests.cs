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
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task RunAsync(string directory, params string[] arguments)
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

        Assert.True(process.ExitCode == 0, string.Join(' ', arguments) + Environment.NewLine + await stdout + await stderr);
    }
}
