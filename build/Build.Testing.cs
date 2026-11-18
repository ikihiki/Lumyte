using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    [Parameter("Test lane: all, cpu, integration, or gpu (managed only; classified lanes use one SDK test invocation over a generated .slnf).")]
    public string Lane { get; set; } = "all";

    [Parameter("Test suite: managed, ui, aot, browser, wasm, browser-contracts, browser-audio, diagnostics-processes, diagnostics-ui, signals, text, windowing, rust.")]
    public string Suite { get; set; } = "managed";

    private static readonly string[] _browserCases = ["contracts", "graphics", "text", "ui-application", "ui-navigation", "ui-pages", "ui-transitions", "audio"];

    [Parameter("Independent browser case: contracts, graphics, text, ui-application, ui-navigation, ui-pages, ui-transitions, or audio. Omit for the complete suite.")]
    public string? BrowserCase { get; set; }

    [Parameter("UI feature: layout, controls, media, scroll, menus, tabs, collections, virtualization, numeric, application, navigation, or pages.")]
    public string? Feature { get; set; }

    [Parameter("Publish and execute the selected UI feature using strict NativeAOT.")]
    public bool Aot { get; set; }

    [Parameter("Maximum concurrent CPU test methods per assembly (1-64).")]
    public int TestConcurrency { get; set; } = 4;

    [Parameter("Capture the Vulkan pipeline validation output after the managed GPU lane.")]
    public bool PipelineValidation { get; set; }

    private Target Test => definition => definition.Executes(async () =>
    {
        ValidateTestSelection();
        if (TestNeedsCompilation)
        {
            await CompileAsync().ConfigureAwait(false);
        }

        await RunSelectedTestsAsync().ConfigureAwait(false);
        if (PipelineValidation)
        {
            await PipelineValidationAsync().ConfigureAwait(false);
        }
    });

    private void ValidateTestSelection()
    {
        if (BrowserCase is not null && (Suite != "browser" || !_browserCases.Contains(BrowserCase, StringComparer.Ordinal)))
        {
            throw new ArgumentException("--browser-case requires --suite browser and an exact supported case.");
        }

        if (PipelineValidation && (Suite != "managed" || Lane != "gpu"))
        {
            throw new ArgumentException("--pipeline-validation requires --suite managed --lane gpu.");
        }

        if (Lane is not ("all" or "cpu" or "integration" or "gpu"))
        {
            throw new ArgumentException("Invalid --lane. Expected all, cpu, integration, or gpu.");
        }

        if (Suite is not ("managed" or "ui" or "aot" or "browser" or "wasm" or "browser-contracts" or "browser-audio" or "diagnostics-processes" or "diagnostics-ui" or "signals" or "text" or "windowing" or "rust"))
        {
            throw new ArgumentException("Invalid --suite. See --help for supported suites.");
        }

        if (TestConcurrency is < 1 or > 64)
        {
            throw new ArgumentException("--test-concurrency must be between 1 and 64.");
        }

        if (Suite != "managed" && Lane != "all")
        {
            throw new ArgumentException("--lane is only supported with --suite managed; feature suites include samples and process checks.");
        }

        if (Suite == "ui")
        {
            ValidateUiFeature();
        }
        else if (Feature is not null || Aot)
        {
            throw new ArgumentException("--feature and --aot require --suite ui.");
        }

        if (Project is not null && Suite != "managed")
        {
            throw new ArgumentException("--project is only supported with --suite managed.");
        }
    }

    private bool TestNeedsCompilation => Suite is not ("aot" or "browser" or "wasm" or "browser-contracts" or "browser-audio" or "rust") && !Aot;

    private async Task RunSelectedTestsAsync()
    {
        switch (Suite)
        {
            case "managed":
                if (Lane != "all")
                {
                    await RunTestLaneAsync(Lane switch { "cpu" => "Cpu", "integration" => "Integration", _ => "Gpu" }).ConfigureAwait(false);
                }
                else
                {
                    await DotNetWithEnvironmentAsync(new Dictionary<string, string?> { ["LUMYTE_CPU_METHOD_PARALLELISM"] = null }, "test", Project ?? BuildSolution, "--configuration", Configuration, "--no-build", "--no-restore", "-m:1", "--logger", "trx", "--results-directory", Path.Combine(ArtifactRoot, "test-results")).ConfigureAwait(false);
                }

                break;
            case "ui": await RunUiFeatureAsync().ConfigureAwait(false); break;
            case "aot": await TestAotAsync().ConfigureAwait(false); break;
            case "browser":
                if (BrowserCase is null or "contracts")
                {
                    await TestBrowserContractsAsync().ConfigureAwait(false);
                }

                if (BrowserCase != "contracts")
                {
                    await InstallWasmToolsAsync().ConfigureAwait(false);
                    await TestWasmAsync().ConfigureAwait(false);
                }
                break;
            case "wasm":
                await InstallWasmToolsAsync().ConfigureAwait(false);
                await TestWasmAsync().ConfigureAwait(false);
                break;
            case "browser-audio":
                await InstallWasmToolsAsync().ConfigureAwait(false);
                await TestBrowserAudioAsync().ConfigureAwait(false);
                break;
            case "browser-contracts": await TestBrowserContractsAsync().ConfigureAwait(false); break;
            case "diagnostics-processes": await TestDiagnosticsProcessesAsync().ConfigureAwait(false); break;
            case "diagnostics-ui": await TestDiagnosticsUiAsync().ConfigureAwait(false); break;
            case "signals": await TestSignalsAsync().ConfigureAwait(false); break;
            case "text": await TestTextAsync().ConfigureAwait(false); break;
            case "windowing": await TestWindowingAsync().ConfigureAwait(false); break;
            case "rust": await TestRustAsync().ConfigureAwait(false); break;
        }
    }

    private async Task RunTestLaneAsync(string lane)
    {
        if (lane == "Cpu" && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            throw new PlatformNotSupportedException("The CPU suite runs only on Linux x64. Use Test --lane integration or gpu on other hosts.");
        }

        TestLane[] catalog = JsonSerializer.Deserialize<TestLane[]>(await File.ReadAllTextAsync(Path.Combine(RepositoryDirectory, "build", "TestLanes.json")).ConfigureAwait(false))!;
        string solution = Path.GetFullPath(Solution, RepositoryDirectory);
        string[] projects = XDocument.Load(solution).Descendants("Project")
            .Select(element => Path.GetFullPath(element.Attribute("Path")!.Value, Path.GetDirectoryName(solution)!))
            .Where(path => File.Exists(path) && XDocument.Load(path).Descendants("IsTestProject").Any(element => element.Value == "true"))
            .ToArray();
        foreach (string path in projects)
        {
            if (!catalog.Any(entry => Path.GetFullPath(entry.Project, RepositoryDirectory) == path))
            {
                throw new InvalidOperationException($"Classify the new test project in build/TestLanes.json: {path}");
            }
        }

        string? selected = Project is null ? null : Path.GetFullPath(Project, RepositoryDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (selected is not null && !catalog.Any(entry =>
            Path.GetFullPath(entry.Project, RepositoryDirectory) == selected ||
            Path.GetDirectoryName(Path.GetFullPath(entry.Project, RepositoryDirectory)) == selected))
        {
            throw new ArgumentException($"The selected project is not a classified test project: {Project}");
        }

        if (catalog.Select(entry => entry.Project).Distinct(StringComparer.Ordinal).Count() != catalog.Length || catalog.Any(entry => entry.Default is not ("Cpu" or "Integration") || entry.IntegrationClasses is null || entry.Lanes is null || entry.Lanes.Length == 0 || entry.Lanes.Any(value => value is not ("Cpu" or "Integration" or "Gpu")) || entry.Lanes.Distinct(StringComparer.Ordinal).Count() != entry.Lanes.Length))
        {
            throw new InvalidDataException("TestLanes.json contains duplicate projects or an invalid default lane.");
        }

        var environment = new Dictionary<string, string?> { ["LUMYTE_LANE_CATALOG"] = Path.Combine(RepositoryDirectory, "build", "TestLanes.json"), ["LUMYTE_TEST_SERIAL"] = lane == "Cpu" ? null : "1", ["LUMYTE_CPU_METHOD_PARALLELISM"] = lane == "Cpu" ? TestConcurrency.ToString(System.Globalization.CultureInfo.InvariantCulture) : null };
        if (lane == "Cpu")
        {
            foreach (string key in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(key => key.StartsWith("LUMYTE_", StringComparison.Ordinal) && (key.Contains("TEST", StringComparison.Ordinal) || key.Contains("BENCHMARK", StringComparison.Ordinal) || key.Contains("GPU", StringComparison.Ordinal))))
            {
                environment[key] = null;
            }
        }

        var selections = catalog.Select(entry => new
        {
            Path = Path.GetFullPath(entry.Project, RepositoryDirectory),
            Filter = entry.Filter(lane),
        }).Where(entry => projects.Contains(entry.Path) && IsHostProject(entry.Path)
            && (selected is null || selected == entry.Path || selected == Path.GetDirectoryName(entry.Path))
            && entry.Filter is not null).ToArray();
        if (selections.Length == 0)
        {
            throw new ArgumentException("No classified host test projects match the requested lane/project selection.");
        }

        string directory = Path.Combine(ArtifactRoot, "test-lanes");
        Directory.CreateDirectory(directory);
        string filterFile = Path.Combine(directory, lane.ToLowerInvariant() + ".slnf");
        string targetsFile = Path.Combine(directory, lane.ToLowerInvariant() + ".targets");
        string inventoryDirectory = Path.Combine(directory, "inventory-" + Guid.NewGuid().ToString("N"));
        environment["LUMYTE_LANE_INVENTORY"] = inventoryDirectory;
        // A solution filter references the canonical .slnx, never a copied project list
        // or an independently maintained solution. Project paths are solution-relative.
        var solutionFilter = new
        {
            solution = new
            {
                path = Path.GetRelativePath(directory, solution),
                projects = selections.Select(entry => Path.GetRelativePath(Path.GetDirectoryName(solution)!, entry.Path)).ToArray(),
            },
        };
        await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(solutionFilter, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        var targets = new XElement("Project",
            new XElement("PropertyGroup", new XElement("VSTestTestCaseFilter", "FullyQualifiedName=__UnselectedLumyteLaneProject__")));
        foreach (var entry in selections)
        {
            // Filters must be project-local MSBuild properties, not a global --filter:
            // mixed projects and default-integration projects have different predicates.
            string projectPath = entry.Path.Replace("%", "%25", StringComparison.Ordinal)
                .Replace("'", "%27", StringComparison.Ordinal).Replace("$", "%24", StringComparison.Ordinal)
                .Replace("@", "%40", StringComparison.Ordinal);
            targets.Add(new XElement("PropertyGroup", new XAttribute("Condition", $"'$(MSBuildProjectFullPath)' == '{projectPath}'"),
                new XElement("LumyteLaneSelected", "true"),
                new XElement("VSTestTestCaseFilter", entry.Filter),
                new XElement("VSTestLogger", "trx%3bLogFileName=$(MSBuildProjectName).trx")));
        }

        targets.Add(new XElement("Target", new XAttribute("Name", "ValidateLumyteLaneProject"), new XAttribute("BeforeTargets", "VSTest"),
            new XElement("Error", new XAttribute("Condition", "'$(IsTestProject)' == 'true' and '$(LumyteLaneSelected)' != 'true'"),
                new XAttribute("Text", "The solution filter scheduled an unselected test project: $(MSBuildProjectFullPath)"))));
        await File.WriteAllTextAsync(targetsFile, new XDocument(targets).ToString()).ConfigureAwait(false);
        // One SDK invocation owns every selected project. -m:1 retains cross-project
        // serial boundaries; the CPU xUnit adapter handles bounded method concurrency.
        await DotNetWithEnvironmentAsync(environment, "test", filterFile, "--configuration", Configuration, "--no-build", "--no-restore", "-m:1",
            "-p:LumyteTestLaneTargets=" + targetsFile, "--results-directory", Path.Combine(ArtifactRoot, "test-results", lane.ToLowerInvariant())).ConfigureAwait(false);
        foreach (var entry in selections)
        {
            string inventoryFile = Path.Combine(inventoryDirectory, Path.GetFileName(entry.Path) + ".json");
            if (!File.Exists(inventoryFile))
            {
                throw new InvalidDataException($"Missing real xUnit discovery inventory for {entry.Path}; rebuild the test project.");
            }

            using var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(inventoryFile).ConfigureAwait(false));
            if (inventory.RootElement.GetProperty("Project").GetString() != entry.Path || inventory.RootElement.GetProperty("Error").GetString() is { } error)
            {
                throw new InvalidDataException("Lane inventory drift: " + inventory.RootElement.GetProperty("Error").GetString());
            }
        }
    }

    private sealed record TestLane(string Project, string Default, string[] IntegrationClasses, string[] Lanes)
    {
        internal string? Filter(string lane)
        {
            if (!Lanes.Contains(lane, StringComparer.Ordinal))
            {
                return null;
            }

            if (lane == "Gpu")
            {
                return "Execution=Gpu";
            }

            if (Default == "Integration")
            {
                return lane == "Integration" ? "Execution!=Gpu" : null;
            }

            string[] names = IntegrationClasses.Select(name => $"FullyQualifiedName~.{name}.").ToArray();
            if (lane == "Integration")
            {
                return names.Length == 0 ? "Execution!=Gpu&Execution=Integration" : $"Execution!=Gpu&(Execution=Integration|{string.Join('|', names)})";
            }

            return "Execution!=Gpu&Execution!=Integration" + string.Concat(IntegrationClasses.Select(name => $"&FullyQualifiedName!~.{name}."));
        }
    }
}
