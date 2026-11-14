using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    [Parameter("Benchmark execution mode: screen, compare, or diagnose. Required unless listing.")]
    public string BenchmarkMode { get; set; } = string.Empty;

    [Parameter("Target project glob, for example Lumyte.Mathematics or Lumyte.*.Benchmark.")]
    public string BenchmarkProject { get; set; } = "*";

    [Parameter("BenchmarkDotNet method filter for selected projects.")]
    public string BenchmarkFilter { get; set; } = "*";

    [Parameter("Optional BenchmarkDotNet category filter across selected projects.")]
    public string? BenchmarkCategory { get; set; }

    [Parameter("Enumerate compiled Benchmark cases without running timings.")]
    public bool BenchmarkList { get; set; }

    [Parameter("Deadline seconds, up to 900: shared for BenchmarkCore; per build/catalog/Type.Method partition for BenchmarkCoreEvaluate.")]
    public double BenchmarkTimeoutSeconds { get; set; } = 900;

    private Target BenchmarkCore => definition => definition.Executes(() => RunBenchmarksAsync("Core"));

    private Target BenchmarkAnimation => definition => definition.Executes(() => RunBenchmarksAsync("Animation"));

    private Target BenchmarkInput => definition => definition.Executes(() => RunBenchmarksAsync("Input"));

    [Parameter("Include median share of 8.333 ms for explicitly SteadyState cases only (not frame latency).")]
    public bool BenchmarkBudgetShare { get; set; }

    private Target BenchmarkCoreEvaluate => definition => definition.Executes(EvaluateCoreAsync);

    private async Task EvaluateCoreAsync()
    {
        CoreBenchmarkEvaluation.Validate(Configuration, BenchmarkFilter, BenchmarkTimeoutSeconds);
        string? mode = BenchmarkList && string.IsNullOrWhiteSpace(BenchmarkMode) ? null : BenchmarkCatalog.NormalizeMode(BenchmarkMode);
        if (NoBuild)
        {
            throw new ArgumentException("BenchmarkCoreEvaluate owns fresh isolated builds; --no-build is not supported.");
        }

        string[] projects = BenchmarkCatalog.SelectProjects(RepositoryDirectory, BenchmarkProject);
        string root = Path.GetFullPath(Output ?? Path.Combine(ArtifactRoot, "core-evaluation", Guid.NewGuid().ToString("N")));
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new ArgumentException("Core evaluation output must be fresh and empty; resume is not supported: " + root);
        }

        Directory.CreateDirectory(root);
        // Keep ownership for the entire run, including untimed planning and final reporting.
        using var ownership = new FileStream(Path.Combine(root, "evaluation.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var environment = new Dictionary<string, string?>
        {
            ["PATH"] = Path.GetDirectoryName(Path.GetFullPath(DotNetExecutable)) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["LANG"] = "C.UTF-8",
            ["LC_ALL"] = "C.UTF-8",
        };
        var records = new JsonArray();
        var partitions = new JsonArray();
        var results = new JsonArray();
        var manifest = new JsonObject
        {
            ["schema"] = "core-benchmark-evaluation-v1",
            ["status"] = "incomplete",
            ["plan_complete"] = false,
            ["required_scope"] = "Standalone",
            ["mode"] = mode,
            ["list_only"] = BenchmarkList,
            ["project_filter"] = BenchmarkProject,
            ["category_filter"] = BenchmarkCategory,
            ["method_filter"] = BenchmarkFilter,
            ["per_command_deadline_seconds"] = BenchmarkTimeoutSeconds,
            ["deadline_policy"] = "Each build/catalog command and each serial Type.Method partition has its own bounded deadline; no retries. All builds/catalogs finish before timing.",
            ["repository"] = RepositoryDirectory,
            ["output"] = root,
            ["max_preparation_process_seconds"] = (2 + (projects.Length * 2)) * BenchmarkTimeoutSeconds,
            ["projects"] = records,
            ["partitions"] = partitions,
            ["results"] = results,
        };
        var expected = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        bool filtered = BenchmarkProject != "*" || !string.IsNullOrWhiteSpace(BenchmarkCategory);
        // A finite plan, no retry loops: at most one build + catalog per project and one call per partition.
        async Task<string> CallAsync(IEnumerable<string> arguments, string label, string directory, Dictionary<string, string?> child)
        {
            var bounded = new ComponentScreeningProcess(root, BenchmarkTimeoutSeconds);
            return await bounded.CallAsync(DotNetExecutable, arguments, directory, label, child).ConfigureAwait(false);
        }

        void Save()
        {
            try
            {
                manifest["coverage"] = CoreBenchmarkEvaluation.Coverage(expected.Keys, results.Select(row => row!["key"]!.GetValue<string>()), filtered);
            }
            catch (ArgumentException error)
            {
                manifest["status"] = "incomplete";
                manifest["coverage"] = new JsonObject
                {
                    ["error"] = error.Message,
                    ["expected_expanded_cases"] = expected.Count,
                    ["observed_expanded_cases"] = results.Count,
                    ["selected_coverage_complete"] = false,
                    ["full_category_complete"] = false,
                };
            }

            bool allStandaloneEligible = manifest["plan_complete"]!.GetValue<bool>() && !records.SelectMany(record => record!["catalog"]?["Cases"]?.AsArray() ?? []).Any(item =>
                item!["evaluation_status"]?.GetValue<string>() == "excluded-mode");
            manifest["coverage"]!["all_standalone_cases_mode_eligible"] = allStandaloneEligible;
            bool executionComplete = manifest["status"]!.GetValue<string>() is "completed" or "completed-subset";
            manifest["coverage"]!["full_mode_eligible_scope_complete"] = executionComplete && manifest["coverage"]!["full_category_complete"]!.GetValue<bool>();
            if (!allStandaloneEligible || !executionComplete)
            {
                manifest["coverage"]!["full_category_complete"] = false;
            }

            if (BenchmarkList)
            {
                manifest["coverage"]!["full_mode_eligible_scope_complete"] = false;
                manifest["coverage"]!["full_category_complete"] = false;
                manifest["coverage"]!["selected_coverage_complete"] = false;
            }

            ComponentScreening.Dump(Path.Combine(root, "summary.json"), manifest);
            File.WriteAllText(Path.Combine(root, "summary.md"), CoreBenchmarkEvaluation.Markdown(manifest));
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(RepositoryDirectory, "artifacts", "nuget"));
            manifest["source_sha"] = await new ComponentScreeningProcess(root, BenchmarkTimeoutSeconds).CallAsync(
                "git", ["rev-parse", "HEAD"], RepositoryDirectory, "source-sha").ConfigureAwait(false);
            string diff = await new ComponentScreeningProcess(root, BenchmarkTimeoutSeconds).CallAsync(
                "git", ["diff", "HEAD", "--binary"], RepositoryDirectory, "source-diff", trimOutput: false).ConfigureAwait(false);
            manifest["tracked_diff_sha256"] = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(diff)));
            // TreeHash includes untracked source files and excludes build products.
            manifest["evaluator_harness_sha256"] = ComponentScreening.TreeHash(Path.Combine(RepositoryDirectory, "build"));
            manifest["evaluator_binary_sha256"] = ComponentScreening.FileHash(typeof(Build).Assembly.Location);
            var timingEnvironment = new JsonObject();
            foreach (string name in new[] { "DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_ReadyToRun", "COMPlus_TieredCompilation", "COMPlus_TieredPGO", "COMPlus_ReadyToRun", "DOTNET_gcServer", "COMPlus_gcServer" })
            {
                timingEnvironment[name] = Environment.GetEnvironmentVariable(name);
            }

            manifest["timing_environment"] = timingEnvironment;
            manifest["started_utc"] = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            var entries = new List<(string Project, string Binary, string Inputs, string Outputs, JsonObject Record)>();
            string artifacts = Path.Combine(root, "build");
            foreach (string project in projects)
            {
                string name = Path.GetFileNameWithoutExtension(project);
                var record = new JsonObject { ["project"] = Path.GetRelativePath(RepositoryDirectory, project), ["status"] = "preparing" };
                records.Add(record);
                string inputs = BenchmarkInputs(project, "Core");
                var arguments = new List<string> { "build", project, "-c", "Release", "--nologo", "--artifacts-path", artifacts };
                if (NoRestore)
                {
                    arguments.Add("--no-restore");
                }

                await CallAsync(arguments, name + "-build", RepositoryDirectory, environment).ConfigureAwait(false);
                string binary = Path.Combine(artifacts, "bin", name, "release", name + ".dll");
                if (!File.Exists(binary) || BenchmarkInputs(project, "Core") != inputs)
                {
                    throw new InvalidOperationException("Missing binary or inputs changed while building " + name);
                }

                string outputs = BenchmarkCatalog.OutputHash(Path.GetDirectoryName(binary)!);
                record["inputs_sha256"] = inputs;
                record["outputs_sha256"] = outputs;
                record["binary_sha256"] = ComponentScreening.FileHash(binary);
                record["status"] = "built";
                entries.Add((project, binary, inputs, outputs, record));
            }

            foreach ((string Project, string Binary, string Inputs, string Outputs, JsonObject Record) entry in entries)
            {
                string name = Path.GetFileNameWithoutExtension(entry.Project);
                string json = await CallAsync([entry.Binary, "--catalog"], name + "-catalog", Path.GetDirectoryName(entry.Project)!, BenchmarkEnvironment(environment, entry.Project)).ConfigureAwait(false);
                JsonObject catalog = BenchmarkCatalog.ParseCatalog(json);
                entry.Record["catalog"] = catalog;
                var catalogKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonNode? item in catalog["Cases"]!.AsArray())
                {
                    if (!catalogKeys.Add(CoreBenchmarkEvaluation.CaseKey(item!)))
                    {
                        throw new ArgumentException("Duplicate compiled catalog identity in " + name);
                    }

                    string? exclusion = CoreBenchmarkEvaluation.Exclusion(item!, mode, BenchmarkCategory);
                    item!["evaluation_status"] = exclusion ?? "selected";
                    if (exclusion is not null)
                    {
                        continue;
                    }

                    string key = name + ":" + CoreBenchmarkEvaluation.CaseKey(item);
                    if (!expected.TryAdd(key, item))
                    {
                        throw new ArgumentException("Duplicate compiled expanded case: " + key);
                    }
                }

                foreach (IGrouping<string, JsonNode?> group in catalog["Cases"]!.AsArray().Where(item => item!["evaluation_status"]!.GetValue<string>() == "selected").GroupBy(item => CoreBenchmarkEvaluation.Partition(item!), StringComparer.Ordinal))
                {
                    string filter = group.Key;
                    // BDN's plain full method glob matches that method and all its Params/Arguments.
                    // Reject syntax that might expand into another method instead of approximating it.
                    if (filter.IndexOfAny(['*', '?', '[', ']']) >= 0)
                    {
                        throw new ArgumentException("Compiled Type.Method cannot be represented by an exact BDN filter: " + filter);
                    }

                    partitions.Add(new JsonObject
                    {
                        ["id"] = partitions.Count.ToString("D5", System.Globalization.CultureInfo.InvariantCulture),
                        ["project"] = name,
                        ["filter"] = filter,
                        ["expected_keys"] = new JsonArray(group.Select(item => JsonValue.Create(name + ":" + CoreBenchmarkEvaluation.CaseKey(item!))).ToArray()),
                        ["status"] = "planned",
                    });
                }

                entry.Record["status"] = "catalogued";
            }

            if (expected.Count == 0)
            {
                throw new ArgumentException("No eligible Standalone Core cases match the requested project/category/mode selection.");
            }

            foreach ((string Project, string Binary, string Inputs, string Outputs, JsonObject Record) entry in entries)
            {
                if (BenchmarkInputs(entry.Project, "Core") != entry.Inputs || BenchmarkCatalog.OutputHash(Path.GetDirectoryName(entry.Binary)!) != entry.Outputs)
                {
                    throw new InvalidOperationException("Benchmark sources or dependencies changed during catalog preparation.");
                }
            }

            manifest["plan_complete"] = true;
            manifest["max_timed_process_seconds"] = BenchmarkList ? 0 : partitions.Count * BenchmarkTimeoutSeconds;
            // Includes two source identity commands, builds and catalogs; excludes local hashing/report IO.
            manifest["max_child_process_seconds"] = (2 + (projects.Length * 2) + (BenchmarkList ? 0 : partitions.Count)) * BenchmarkTimeoutSeconds;
            Save();
            if (BenchmarkList)
            {
                manifest["status"] = "planned-untimed";
                return;
            }

            foreach (JsonNode? partition in partitions)
            {
                string name = partition!["project"]!.GetValue<string>();
                (string Project, string Binary, string Inputs, string Outputs, JsonObject Record) entry = entries.Single(item => Path.GetFileNameWithoutExtension(item.Project) == name);
                // Validate the complete frozen build set, not just the current executable.
                foreach ((string Project, string Binary, string Inputs, string Outputs, JsonObject Record) frozen in entries)
                {
                    if (BenchmarkInputs(frozen.Project, "Core") != frozen.Inputs || BenchmarkCatalog.OutputHash(Path.GetDirectoryName(frozen.Binary)!) != frozen.Outputs)
                    {
                        throw new InvalidOperationException("Benchmark sources or execution dependencies changed after planning.");
                    }
                }

                string id = partition["id"]!.GetValue<string>();
                string output = Path.Combine(root, "measurements", id);
                Directory.CreateDirectory(output);
                partition["status"] = "running";
                Save();
                await CallAsync(BenchmarkCatalog.Arguments(entry.Binary, mode, partition["filter"]!.GetValue<string>(), "Standalone", output, false),
                    "partition-" + id, Path.GetDirectoryName(entry.Project)!, BenchmarkEnvironment(environment, entry.Project)).ConfigureAwait(false);
                JsonArray observed = CoreBenchmarkEvaluation.ParseReports(Directory.GetFiles(output, "*-report-full.json", SearchOption.AllDirectories), mode);
                foreach (JsonNode? row in observed)
                {
                    string identity = CoreBenchmarkEvaluation.EnvironmentIdentity(row!["host_environment"]);
                    if (manifest["environment_identity"] is not null && manifest["environment_identity"]!.GetValue<string>() != identity)
                    {
                        throw new InvalidOperationException("Benchmark host/runtime/SDK identity changed between partitions.");
                    }

                    manifest["environment_identity"] = identity;
                    string key = name + ":" + row["key"]!.GetValue<string>();
                    row["key"] = key;
                    row["partition"] = id;
                    if (expected.TryGetValue(key, out JsonNode? benchmark))
                    {
                        row["categories"] = benchmark["Categories"]!.DeepClone();
                        row["budget_share_percent"] = CoreBenchmarkEvaluation.BudgetShare(benchmark, row["median_ns"]!.GetValue<double>(), BenchmarkBudgetShare);
                    }

                    results.Add(row.DeepClone());
                }

                CoreBenchmarkEvaluation.RequireComplete(CoreBenchmarkEvaluation.Coverage(
                    partition["expected_keys"]!.AsArray().Select(key => key!.GetValue<string>()),
                    observed.Select(row => row!["key"]!.GetValue<string>()), filtered: true));
                if (BenchmarkInputs(entry.Project, "Core") != entry.Inputs || BenchmarkCatalog.OutputHash(Path.GetDirectoryName(entry.Binary)!) != entry.Outputs)
                {
                    throw new InvalidOperationException("Benchmark inputs or dependencies changed during measurement.");
                }

                partition["status"] = "completed";
                Save();
            }

            CoreBenchmarkEvaluation.RequireComplete(CoreBenchmarkEvaluation.Coverage(expected.Keys, results.Select(row => row!["key"]!.GetValue<string>()), filtered));
            manifest["status"] = filtered ? "completed-subset" : "completed";
        }
        catch (Exception error)
        {
            manifest["status"] = "incomplete";
            manifest["error"] = error.Message;
            foreach (JsonNode? partition in partitions.Where(item => item!["status"]!.GetValue<string>() == "running"))
            {
                partition!["status"] = error is ScreeningFailure failure ? failure.Status : "failed";
            }

            throw;
        }
        finally
        {
            Save();
        }
    }

    private async Task RunBenchmarksAsync(string area)
    {
        string? mode = BenchmarkList && string.IsNullOrWhiteSpace(BenchmarkMode) ? null : BenchmarkCatalog.NormalizeMode(BenchmarkMode);
        if (Configuration != "Release")
        {
            throw new ArgumentException("Benchmarks require Release configuration.");
        }

        if (!double.IsFinite(BenchmarkTimeoutSeconds) || BenchmarkTimeoutSeconds <= 0 || BenchmarkTimeoutSeconds > 900)
        {
            throw new ArgumentOutOfRangeException(nameof(BenchmarkTimeoutSeconds), "The shared deadline must be positive and at most 900 seconds.");
        }

        string[] projects = BenchmarkCatalog.SelectProjects(RepositoryDirectory, BenchmarkProject, area);
        string root = Path.GetFullPath(Output ?? Path.Combine(ArtifactRoot, area.ToLowerInvariant() + "-benchmarks", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff", System.Globalization.CultureInfo.InvariantCulture)));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(RepositoryDirectory, "artifacts", "nuget"));
        var process = new ComponentScreeningProcess(root, BenchmarkTimeoutSeconds);
        var childEnvironment = new Dictionary<string, string?>
        {
            ["PATH"] = Path.GetDirectoryName(Path.GetFullPath(DotNetExecutable)) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
        };
        var manifest = new JsonObject
        {
            ["schema"] = area.ToLowerInvariant() + "-benchmark-run-v1",
            ["area"] = area,
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
                string inputHash = BenchmarkInputs(project, area);
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
                    throw new FileNotFoundException("Benchmark binary is missing; build it before using --no-build.", binary);
                }

                string binaryHash = ComponentScreening.FileHash(binary);
                string outputHash = BenchmarkCatalog.OutputHash(Path.GetDirectoryName(binary)!);
                if (BenchmarkInputs(project, area) != inputHash)
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
                    JsonObject catalog = BenchmarkCatalog.ParseCatalog(result);
                    JsonArray selected = BenchmarkCatalog.SelectCases(catalog, mode, BenchmarkCategory);
                    selections.Add(selected);
                    entry.Record["selected_cases"] = selected.DeepClone();
                    catalog["Cases"] = selected.DeepClone();
                    entry.Record["catalog"] = catalog;
                    if (BenchmarkCatalog.OutputHash(Path.GetDirectoryName(entry.Binary)!) != entry.Record["outputs_sha256"]!.ToString())
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

                BenchmarkCatalog.RequireSelectedCases(selections, BenchmarkCategory, mode);
                entries = selectedEntries;
            }

            // All builds finish before any timed job starts.
            foreach ((string project, string binary, JsonObject record) in entries)
            {
                string name = Path.GetFileNameWithoutExtension(project);
                if (!BenchmarkList || record["catalog"] is null)
                {
                    string result = await process.CallAsync(DotNetExecutable,
                        BenchmarkCatalog.Arguments(binary, mode, BenchmarkFilter, BenchmarkCategory, Path.Combine(root, name), BenchmarkList),
                        Path.GetDirectoryName(project)!, name + (BenchmarkList ? "-catalog" : "-" + mode), BenchmarkEnvironment(childEnvironment, project)).ConfigureAwait(false);
                    if (BenchmarkList)
                    {
                        record["catalog"] = BenchmarkCatalog.ParseCatalog(result);
                    }
                }

                if (BenchmarkCatalog.OutputHash(Path.GetDirectoryName(binary)!) != record["outputs_sha256"]!.ToString())
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

    private string BenchmarkInputs(string project, string area)
    {
        var inputs = new List<string>
        {
            ComponentScreening.TreeHash(Path.Combine(RepositoryDirectory, "src", "Core")),
            ComponentScreening.TreeHash(Path.GetDirectoryName(project)!),
            ComponentScreening.TreeHash(Path.Combine(RepositoryDirectory, "benchmarks", "Core", "Shared")),
        };
        if (area != "Core")
        {
            inputs.Add(ComponentScreening.TreeHash(Path.Combine(RepositoryDirectory, "src", area)));
        }

        if (area == "Input")
        {
            inputs.Add(ComponentScreening.TreeHash(Path.Combine(RepositoryDirectory, "src", "Diagnostics")));
        }

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
