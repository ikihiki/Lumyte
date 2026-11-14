using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    [Parameter("Core benchmark execution mode: screen, compare, or diagnose. Required unless listing.")]
    public string BenchmarkMode { get; set; } = string.Empty;

    [Parameter("Core target project glob, for example Lumyte.Mathematics or Lumyte.*.Benchmark.")]
    public string BenchmarkProject { get; set; } = "*";

    [Parameter("BenchmarkDotNet method filter for selected Core projects.")]
    public string BenchmarkFilter { get; set; } = "*";

    [Parameter("Optional BenchmarkDotNet category filter across selected Core projects.")]
    public string? BenchmarkCategory { get; set; }

    [Parameter("Enumerate compiled Core benchmark cases without running timings.")]
    public bool BenchmarkList { get; set; }

    [Parameter("Shared Core benchmark process deadline in seconds, up to 900.")]
    public double BenchmarkTimeoutSeconds { get; set; } = 900;

    private Target BenchmarkCore => definition => definition.Executes(RunCoreBenchmarksAsync);

    private async Task RunCoreBenchmarksAsync()
    {
        string? mode = BenchmarkList && string.IsNullOrWhiteSpace(BenchmarkMode) ? null : CoreBenchmarkCatalog.NormalizeMode(BenchmarkMode);
        if (Configuration != "Release")
        {
            throw new ArgumentException("Core benchmarks require Release configuration.");
        }

        if (!double.IsFinite(BenchmarkTimeoutSeconds) || BenchmarkTimeoutSeconds <= 0 || BenchmarkTimeoutSeconds > 900)
        {
            throw new ArgumentOutOfRangeException(nameof(BenchmarkTimeoutSeconds), "The shared deadline must be positive and at most 900 seconds.");
        }

        string[] projects = CoreBenchmarkCatalog.SelectProjects(RepositoryDirectory, BenchmarkProject);
        string root = Path.GetFullPath(Output ?? Path.Combine(ArtifactRoot, "core-benchmarks", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff", System.Globalization.CultureInfo.InvariantCulture)));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(RepositoryDirectory, "artifacts", "nuget"));
        var process = new ComponentScreeningProcess(root, BenchmarkTimeoutSeconds);
        var childEnvironment = new Dictionary<string, string?>
        {
            ["PATH"] = Path.GetDirectoryName(Path.GetFullPath(DotNetExecutable)) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
        };
        var manifest = new JsonObject
        {
            ["schema"] = "core-benchmark-run-v1",
            ["mode"] = BenchmarkList ? "catalog" : mode,
            ["project_filter"] = BenchmarkProject,
            ["method_filter"] = BenchmarkFilter,
            ["category"] = BenchmarkCategory,
            ["status"] = "incomplete",
            ["repository"] = RepositoryDirectory,
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["architecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        };
        var records = new JsonArray();
        manifest["projects"] = records;
        try
        {
            try
            {
                manifest["source_sha"] = await process.CallAsync("git", ["rev-parse", "HEAD"], RepositoryDirectory, "source-sha").ConfigureAwait(false);
                string diff = await process.CallAsync("git", ["diff", "HEAD", "--binary"], RepositoryDirectory, "source-diff", trimOutput: false).ConfigureAwait(false);
                manifest["tracked_diff_sha256"] = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(diff)));
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or ScreeningFailure)
            {
                manifest["source_identity_error"] = error.Message;
            }

            var entries = new List<(string Project, string Binary, JsonObject Record)>();
            foreach (string project in projects)
            {
                string name = Path.GetFileNameWithoutExtension(project);
                string binary = Path.Combine(Path.GetDirectoryName(project)!, "bin", "Release", "net10.0", name + ".dll");
                var record = new JsonObject { ["project"] = Path.GetRelativePath(RepositoryDirectory, project), ["status"] = "preparing" };
                records.Add(record);
                string inputHash = CoreBenchmarkInputs(project);
                string provenance = binary + ".inputs.json";
                if (!NoBuild)
                {
                    var args = new List<string> { "build", project, "-c", "Release", "--nologo" };
                    if (NoRestore)
                    {
                        args.Add("--no-restore");
                    }

                    await process.CallAsync(DotNetExecutable, args, RepositoryDirectory, name + "-build", childEnvironment).ConfigureAwait(false);
                }

                if (!File.Exists(binary))
                {
                    throw new FileNotFoundException("Core benchmark binary is missing; build it before using --no-build.", binary);
                }

                string binaryHash = ComponentScreening.FileHash(binary);
                string outputHash = CoreBenchmarkCatalog.OutputHash(Path.GetDirectoryName(binary)!);
                if (CoreBenchmarkInputs(project) != inputHash)
                {
                    throw new InvalidOperationException("Benchmark inputs changed during build; rebuild from frozen sources.");
                }

                if (NoBuild)
                {
                    JsonNode? built = File.Exists(provenance) ? JsonNode.Parse(File.ReadAllText(provenance)) : null;
                    if (built?["inputs_sha256"]?.ToString() != inputHash || built?["binary_sha256"]?.ToString() != binaryHash || built?["outputs_sha256"]?.ToString() != outputHash)
                    {
                        throw new InvalidOperationException("No-build benchmark outputs do not match current inputs; run BenchmarkCore without --no-build first.");
                    }
                }
                else
                {
                    ComponentScreening.Dump(provenance, new JsonObject { ["inputs_sha256"] = inputHash, ["binary_sha256"] = binaryHash, ["outputs_sha256"] = outputHash });
                }

                record["outputs_sha256"] = outputHash;
                record["inputs_sha256"] = inputHash;
                record["binary_sha256"] = binaryHash;
                record["harness_sha256"] = ComponentScreening.TreeHash(Path.GetDirectoryName(project)!);
                record["shared_source_sha256"] = ComponentScreening.FileHash(Path.Combine(RepositoryDirectory, "benchmarks", "Core", "Shared", "BenchmarkEntry.cs"));
                record["status"] = "built";
                entries.Add((project, binary, record));
            }

            // Inspect compiled metadata only after every selected project has built.
            if (!string.IsNullOrWhiteSpace(BenchmarkCategory))
            {
                var selections = new List<JsonArray>();
                var selectedEntries = new List<(string Project, string Binary, JsonObject Record)>();
                foreach ((string Project, string Binary, JsonObject Record) entry in entries)
                {
                    string name = Path.GetFileNameWithoutExtension(entry.Project);
                    string result = await process.CallAsync(DotNetExecutable, [entry.Binary, "--catalog"],
                        Path.GetDirectoryName(entry.Project)!, name + "-catalog", BenchmarkEnvironment(childEnvironment, entry.Project)).ConfigureAwait(false);
                    JsonObject catalog = CoreBenchmarkCatalog.ParseCatalog(result);
                    JsonArray selected = CoreBenchmarkCatalog.SelectCases(catalog, mode, BenchmarkCategory);
                    selections.Add(selected);
                    entry.Record["selected_cases"] = selected.DeepClone();
                    catalog["Cases"] = selected.DeepClone();
                    entry.Record["catalog"] = catalog;
                    if (CoreBenchmarkCatalog.OutputHash(Path.GetDirectoryName(entry.Binary)!) != entry.Record["outputs_sha256"]!.ToString())
                    {
                        throw new InvalidOperationException("Benchmark execution dependencies changed during the run.");
                    }

                    if (selected.Count == 0)
                    {
                        entry.Record["status"] = "not-selected";
                    }
                    else
                    {
                        selectedEntries.Add(entry);
                    }
                }

                CoreBenchmarkCatalog.RequireSelectedCases(selections, BenchmarkCategory, mode);
                entries = selectedEntries;
            }

            // All builds finish before any timed job starts.
            foreach ((string project, string binary, JsonObject record) in entries)
            {
                string name = Path.GetFileNameWithoutExtension(project);
                if (!BenchmarkList || record["catalog"] is null)
                {
                    string result = await process.CallAsync(DotNetExecutable,
                        CoreBenchmarkCatalog.Arguments(binary, mode, BenchmarkFilter, BenchmarkCategory, Path.Combine(root, name), BenchmarkList),
                        Path.GetDirectoryName(project)!, name + (BenchmarkList ? "-catalog" : "-" + mode), BenchmarkEnvironment(childEnvironment, project)).ConfigureAwait(false);
                    if (BenchmarkList)
                    {
                        record["catalog"] = CoreBenchmarkCatalog.ParseCatalog(result);
                    }
                }

                if (CoreBenchmarkCatalog.OutputHash(Path.GetDirectoryName(binary)!) != record["outputs_sha256"]!.ToString())
                {
                    throw new InvalidOperationException("Benchmark execution dependencies changed during the run.");
                }

                record["status"] = BenchmarkList ? "catalogued" : "completed";
            }

            manifest["status"] = "completed";
        }
        catch (Exception error)
        {
            manifest["status"] = error is ScreeningFailure failure ? failure.Status : "failed";
            manifest["error"] = error.Message;
            throw;
        }
        finally
        {
            manifest["elapsed_seconds"] = process.ElapsedSeconds;
            ComponentScreening.Dump(Path.Combine(root, "manifest.json"), manifest);
        }
    }

    private static Dictionary<string, string?> BenchmarkEnvironment(Dictionary<string, string?> inherited, string project)
        => new(inherited) { ["LUMYTE_BENCHMARK_PROJECT"] = project };

    private string CoreBenchmarkInputs(string project)
    {
        var inputs = new List<string>
        {
            ComponentScreening.TreeHash(Path.Combine(RepositoryDirectory, "src", "Core")),
            ComponentScreening.TreeHash(Path.GetDirectoryName(project)!),
            ComponentScreening.TreeHash(Path.Combine(RepositoryDirectory, "benchmarks", "Core", "Shared")),
        };
        foreach (string name in new[] { "global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.config" })
        {
            string file = Path.Combine(RepositoryDirectory, name);
            if (File.Exists(file))
            {
                inputs.Add(name + ":" + ComponentScreening.FileHash(file));
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join(":", inputs))));
    }
}
