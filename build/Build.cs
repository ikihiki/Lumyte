using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build : FalloutBuild
{
    [Parameter("Solution to restore, compile and test.")]
    public string Solution { get; set; } = "Lumyte.slnx";

    [Parameter("Build configuration shared by repository targets.")]
    public string Configuration { get; set; } = "Release";

    [Parameter("Optional project for restore, compile, test, pack or publish.")]
    public string? Project { get; set; }

    [Parameter("Optional target runtime identifier.")]
    public string? Runtime { get; set; }

    [Parameter("Skip compilation and use existing outputs.")]
    public bool NoBuild { get; set; }

    [Parameter("Skip NuGet restoration.")]
    public bool NoRestore { get; set; }

    [Parameter("Exclude Parley packaging until native assets from all supported RIDs have been collected.")]
    public bool SkipParleyPackage { get; set; }

    [Parameter("Explicit dotnet executable for child build commands.")]
    public string? DotnetPath { get; set; }

    [Parameter("Repository root when an isolated native worker uses a separate Fallout state directory.")]
    public string? RepositoryRoot { get; set; }

    private string RepositoryDirectory => Path.GetFullPath(RepositoryRoot ?? RootDirectory);

    private string ArtifactRoot => Path.Combine(RootDirectory, "artifacts");

    private string DotNetExecutable => DotnetPath
        ?? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
        ?? Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

    private string NodeExecutable => Node ?? FindExecutable("node");

    private Target Restore => definition => definition
        .OnlyWhenDynamic(() => !NoRestore && !NoBuild)
        .Executes(async () =>
        {
            var arguments = new List<string> { "restore", Project ?? Solution };
            if (Runtime is not null)
            {
                arguments.AddRange(["--runtime", Runtime]);
            }

            await DotNetAsync(arguments.ToArray()).ConfigureAwait(false);
        });

    private Target Compile => definition => definition
        .DependsOn(Restore)
        .OnlyWhenDynamic(() => !NoBuild)
        .Executes(async () =>
        {
            var arguments = new List<string> { "build", Project ?? Solution, "--configuration", Configuration, "--no-restore" };
            if (Runtime is not null)
            {
                arguments.AddRange(["--runtime", Runtime]);
            }

            await DotNetAsync(arguments.ToArray()).ConfigureAwait(false);
        });

    private Target Test => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", Project ?? Solution, "--configuration", Configuration, "--no-build", "--no-restore", "-m:1", "--logger", "trx", "--results-directory", Path.Combine(ArtifactRoot, "test-results")).ConfigureAwait(false);
        });

    private Target Clean => definition => definition
        .Executes(async () =>
        {
            await DotNetAsync("clean", Project ?? Solution, "--configuration", Configuration).ConfigureAwait(false);
        });

    private Target Pack => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            string output = Path.GetFullPath(Output ?? Path.Combine(ArtifactRoot, "nuget"));
            Directory.CreateDirectory(output);
            IEnumerable<string> projects = Project is string project
                ? [project]
                : Directory.EnumerateFiles(Path.Combine(RootDirectory, "src"), "*.csproj", SearchOption.AllDirectories)
                    .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));
            foreach (string path in projects.Order(StringComparer.Ordinal))
            {
                if (SkipParleyPackage && Path.GetFileName(path) == "Lumyte.Graphics.Text.Parley.csproj")
                {
                    continue;
                }

                if (Project is null)
                {
                    string packable = await DotNetAsync("msbuild", path, "-nologo", "-getProperty:IsPackable").ConfigureAwait(false);
                    if (!string.Equals(packable.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                var arguments = new List<string> { "pack", path, "--configuration", Configuration, "--no-build", "--no-restore", "--output", output };
                if (Runtime is not null)
                {
                    arguments.Add("-p:RuntimeIdentifier=" + Runtime);
                }

                await DotNetAsync(arguments.ToArray()).ConfigureAwait(false);
            }
        });

    private Target PreparePublish => definition => definition
        .OnlyWhenDynamic(() => !NoBuild && (Project ?? "samples/Lumyte.Graphics.Text.Parley.Browser.Sample").Contains("Browser", StringComparison.Ordinal))
        .Executes(InstallWasmToolsAsync);

    private Target Publish => definition => definition
        .DependsOn(PreparePublish)
        .Executes(async () =>
        {
            var arguments = new List<string> { "publish", Project ?? "samples/Lumyte.Graphics.Text.Parley.Browser.Sample", "--configuration", Configuration };
            if (NoBuild)
            {
                arguments.Add("--no-build");
            }

            if (NoRestore)
            {
                arguments.Add("--no-restore");
            }

            if (Runtime is not null)
            {
                arguments.AddRange(["--runtime", Runtime]);
            }

            if (Output is not null)
            {
                arguments.AddRange(["--output", Path.GetFullPath(Output)]);
            }

            await DotNetAsync(arguments.ToArray()).ConfigureAwait(false);
        });

    private Target CheckFormat => definition => definition
        .Executes(async () =>
        {
            await DotNetAsync("format", Project ?? Solution, "--no-restore", "--verify-no-changes").ConfigureAwait(false);
        });

    private Target Ci => definition => definition
        .DependsOn(Test, TestBrowserContracts, TestRust, TestText, TestWasm, Samples, Pack, TestAot, CheckFormat, CheckMarkdown);

    public static int Main() => Execute<Build>(build => build.Compile);

    private async Task InstallWasmToolsAsync()
    {
        using var global = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RootDirectory, "global.json")).ConfigureAwait(false));
        string version = global.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
        await DotNetAsync("workload", "install", "wasm-tools", "--version", version).ConfigureAwait(false);
    }

    private Task<string> DotNetAsync(params string[] arguments) => DotNetWithEnvironmentAsync(new Dictionary<string, string?>(), arguments);

    private Task<string> DotNetWithEnvironmentAsync(IReadOnlyDictionary<string, string?> environment, params string[] arguments)
    {
        var overrides = new Dictionary<string, string?>(environment)
        {
            ["LUMYTE_BUILD_ASSEMBLY"] = Assembly.GetExecutingAssembly().Location,
            ["LumyteAgilityChannel"] = SelectedAgilityChannel,
        };
        // Browser project references can evaluate the same managed dependency
        // with distinct interop properties. Serialize publication to keep their
        // shared output paths from being written concurrently.
        if (arguments.FirstOrDefault() == "publish" && !arguments.Any(argument => argument.StartsWith("-m:", StringComparison.Ordinal)))
        {
            arguments = [.. arguments, "-m:1"];
        }

        return ProcessRunner.RunAsync(DotNetExecutable, arguments, RootDirectory, overrides);
    }

    private Task<string> NodeAsync(params string[] arguments) => ToolAsync(NodeExecutable, arguments);

    private async Task<string> PythonAsync(params string[] arguments)
    {
        string python = await PythonRuntime.ResolveAsync(RootDirectory, null, Python).ConfigureAwait(false);
        return await ToolWithEnvironmentAsync(python, new Dictionary<string, string?>
        {
            ["PYTHONHOME"] = null,
            ["PYTHONPATH"] = null,
            ["PYTHONNOUSERSITE"] = "1",
        }, arguments).ConfigureAwait(false);
    }

    private Task<string> ToolAsync(string executable, params string[] arguments) => ToolWithEnvironmentAsync(executable, new Dictionary<string, string?>(), arguments);

    private Task<string> ToolWithEnvironmentAsync(string executable, IReadOnlyDictionary<string, string?> environment, params string[] arguments)
    {
        string path = Path.IsPathFullyQualified(executable) ? executable : FindExecutable(executable);
        return ProcessRunner.RunAsync(path, arguments, RootDirectory, environment);
    }
}
