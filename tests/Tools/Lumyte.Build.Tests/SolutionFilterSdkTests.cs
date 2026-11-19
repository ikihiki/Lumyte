using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Lumyte.Build.Tests;

/// <summary>Proves the pinned SDK executes canonical .slnx solution filters with project-local lane predicates.</summary>
/// <param name="output">Retains SDK and real process evidence.</param>
public sealed class SolutionFilterSdkTests(ITestOutputHelper output)
{
    private static string Dotnet => Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

    /// <summary>Mixed classes, default-integration projects and same-class CPU methods retain their exact partitions.</summary>
    /// <returns>The completed real SDK assertion.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task CanonicalSolutionFilterExecutesExactlyOneSdkTestPerLaneAsync()
    {
        using var fixture = new BuildFixture();
        string repository = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repository, "Lumyte.slnx")))
        {
            repository = Path.GetDirectoryName(repository.TrimEnd(Path.DirectorySeparatorChar)) ?? throw new DirectoryNotFoundException("Repository root not found.");
        }

        File.Copy(Path.Combine(repository, "global.json"), Path.Combine(fixture.Root, "global.json"));
        File.WriteAllText(Path.Combine(fixture.Root, "Directory.Build.targets"), new XDocument(new XElement("Project", new XElement("Import", new XAttribute("Project", Path.Combine(repository, "tests", "Directory.Build.targets"))))).ToString());
        string[] projects = ["Mixed/Mixed.csproj", "Native/Native.csproj", "CpuOnly/CpuOnly.csproj", "GpuOnly/GpuOnly.csproj"];
        for (int index = 0; index < projects.Length; index++)
        {
            string path = Path.Combine(fixture.Root, projects[index]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string projectContent = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IsTestProject>true</IsTestProject>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <AssemblyName>{(index == 0 ? "Fixture.Fake.IntegrationTests" : "Fixture.Native" + index + ".Tests")}</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                    <PackageReference Include="xunit" Version="2.9.3" />
                    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.1" />
                  </ItemGroup>
                </Project>
                """;
            File.WriteAllText(path, projectContent);
        }

        string mixedSource = """
            using Xunit;
            namespace Fixture;
            public sealed class MixedCases
            {
                [Fact] public void CpuOne() => Meet();
                [Fact] public void CpuTwo() => Meet();
                [Fact, Trait("Execution", "Integration")] public void Process() => Assert.Equal("integration", Environment.GetEnvironmentVariable("LUMYTE_FIXTURE_LANE"));
                [Fact, Trait("Execution", "Gpu")] public void Gpu() => Assert.Equal("gpu", Environment.GetEnvironmentVariable("LUMYTE_FIXTURE_LANE"));
                private static void Meet()
                {
                    Assert.Equal("cpu", Environment.GetEnvironmentVariable("LUMYTE_FIXTURE_LANE"));
                    Assert.Equal("4", Environment.GetEnvironmentVariable("LUMYTE_CPU_METHOD_PARALLELISM"));
                }
            }
            public sealed class LegacyExternalTests
            {
                [Fact] public void External() => Assert.Equal("integration", Environment.GetEnvironmentVariable("LUMYTE_FIXTURE_LANE"));
            }
            """;
        File.WriteAllText(Path.Combine(fixture.Root, "Mixed", "Cases.cs"), mixedSource);
        string nativeSource = """
            using Xunit;
            namespace Fixture;
            public sealed class NativeCases
            {
                [Fact] public void UntaggedNative() => Assert.Equal("integration", Environment.GetEnvironmentVariable("LUMYTE_FIXTURE_LANE"));
                [Fact, Trait("Execution", "Gpu")] public void Gpu() => Assert.Equal("gpu", Environment.GetEnvironmentVariable("LUMYTE_FIXTURE_LANE"));
            }
            """;
        File.WriteAllText(Path.Combine(fixture.Root, "Native", "Cases.cs"), nativeSource);
        File.WriteAllText(Path.Combine(fixture.Root, "CpuOnly", "Cases.cs"), """using Xunit; public sealed class CpuCases { [Fact] public void Cpu() => Assert.Equal("cpu", System.Environment.GetEnvironmentVariable("LUMYTE_FIXTURE_LANE")); }""");
        File.WriteAllText(Path.Combine(fixture.Root, "GpuOnly", "Cases.cs"), """using Xunit; public sealed class GpuCases { [Fact, Trait("Execution", "Gpu")] public void Gpu() => Assert.Equal("gpu", System.Environment.GetEnvironmentVariable("LUMYTE_FIXTURE_LANE")); }""");
        File.WriteAllText(fixture.Solution, new XDocument(new XElement("Solution", projects.Select(path => new XElement("Project", new XAttribute("Path", path))))).ToString());
        Directory.CreateDirectory(Path.Combine(fixture.Root, "build"));
        File.WriteAllText(Path.Combine(fixture.Root, "build", "TestLanes.json"), JsonSerializer.Serialize(new[]
        {
            new { Project = projects[0], Default = "Cpu", IntegrationClasses = new[] { "LegacyExternalTests" }, Lanes = new[] { "Cpu", "Integration", "Gpu" } },
            new { Project = projects[1], Default = "Integration", IntegrationClasses = Array.Empty<string>(), Lanes = new[] { "Integration", "Gpu" } },
            new { Project = projects[2], Default = "Cpu", IntegrationClasses = Array.Empty<string>(), Lanes = new[] { "Cpu" } },
            new { Project = projects[3], Default = "Integration", IntegrationClasses = Array.Empty<string>(), Lanes = new[] { "Gpu" } },
        }));
        output.WriteLine("Pinned SDK: " + await RunDotnetAsync(fixture.Root, "--version"));
        output.WriteLine(await RunDotnetAsync(fixture.Root, "build", fixture.Solution, "-c", "Release", "--nologo", "-m:1", "-nr:false", "-p:UseSharedCompilation=false"));
        foreach ((string lane, int count, int membership) in new[] { ("cpu", 3, 2), ("integration", 3, 2), ("gpu", 3, 3) })
        {
            if (lane == "cpu" && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64))
            {
                continue;
            }

            fixture.Environment["LUMYTE_FIXTURE_LANE"] = lane;
            BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--lane", lane, "--dotnet-path", Dotnet, "--no-build", "--no-restore");
            output.WriteLine(result.Output + result.Error);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            Assert.Single(Regex.Matches(result.Output, @"(?m)^Running: .*dotnet(?:\.exe)? test "));
            string filter = Path.Combine(fixture.Root, "artifacts", "test-lanes", lane + ".slnf");
            using var document = JsonDocument.Parse(File.ReadAllText(filter));
            JsonElement solution = document.RootElement.GetProperty("solution");
            Assert.Equal(fixture.Solution, Path.GetFullPath(solution.GetProperty("path").GetString()!, Path.GetDirectoryName(filter)!));
            Assert.Equal(membership, solution.GetProperty("projects").GetArrayLength());
            string[] results = Directory.GetFiles(Path.Combine(fixture.Root, "artifacts", "test-results", lane), "*.trx");
            Assert.Equal(membership, results.Length);
            XElement[] counters = results.Select(path => XDocument.Load(path).Descendants().Single(element => element.Name.LocalName == "Counters")).ToArray();
            Assert.Equal(count, counters.Sum(element => int.Parse(element.Attribute("passed")!.Value, System.Globalization.CultureInfo.InvariantCulture)));
            Assert.All(counters, element => Assert.Equal("0", element.Attribute("failed")!.Value));
            Assert.All(counters, element => Assert.True(int.Parse(element.Attribute("total")!.Value, System.Globalization.CultureInfo.InvariantCulture) > 0));
            Assert.DoesNotContain("No test matches", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
        }

        string catalog = Path.Combine(fixture.Root, "build", "TestLanes.json");
        string correct = File.ReadAllText(catalog);
        File.WriteAllText(catalog, correct.Replace(""""Cpu","Integration","Gpu"""", """"Cpu","Integration"""", StringComparison.Ordinal));
        fixture.Environment["LUMYTE_FIXTURE_LANE"] = "integration";
        BuildFixture.ProcessResult drift = await fixture.RunAsync("Test", "--lane", "integration", "--dotnet-path", Dotnet, "--no-build", "--no-restore");
        output.WriteLine(drift.Output + drift.Error);
        Assert.NotEqual(0, drift.ExitCode);
        Assert.Contains("Lane inventory drift", drift.Output + drift.Error, StringComparison.Ordinal);
    }

    private static async Task<string> RunDotnetAsync(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo(Dotnet) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        File.AppendAllText(Path.Combine(directory, "sdk-proof.log"), "Starting " + string.Join(' ', arguments) + Environment.NewLine);
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(TimeSpan.FromSeconds(90));
            File.AppendAllText(Path.Combine(directory, "sdk-proof.log"), "Completed " + process.ExitCode + Environment.NewLine);
            string result = await stdout + await stderr;
            Assert.True(process.ExitCode == 0, result);
            return result;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
