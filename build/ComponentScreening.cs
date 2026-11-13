using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumyte.Build;

internal static partial class ComponentScreening
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public static void Dump(string file, JsonNode data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllText(file + ".tmp", data.ToJsonString(_jsonOptions) + "\n");
        File.Move(file + ".tmp", file, overwrite: true);
    }

    public static string FileHash(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));

    public static string TreeHash(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in SourceFiles(directory))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, file).Replace('\\', '/')));
            hash.AppendData(File.ReadAllBytes(file));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static async Task<JsonObject> CreatePlanAsync(string repo, string baseline, string head, string output, bool all)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        var runner = new ComponentScreeningProcess(output, 900);
        async Task<string> GitAsync(string label, params string[] args) => await runner.CallAsync("git", new[] { "-C", repo }.Concat(args), repo, label).ConfigureAwait(false);
        string baseSha = await GitAsync("plan-base", "rev-parse", "--verify", baseline + "^{commit}").ConfigureAwait(false);
        string headSha = await GitAsync("plan-head", "rev-parse", "--verify", head + "^{commit}").ConfigureAwait(false);
        string[] paths = (await runner.CallAsync("git", ["-C", repo, "diff", "--no-renames", "--name-only", "-z", baseSha, headSha], repo, "plan-diff", trimOutput: false).ConfigureAwait(false)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        (ScreeningCase[] cases, string[] uncovered, string[] omissions) = Select(paths, all);
        var data = new JsonObject
        {
            ["schema_version"] = 2,
            ["harness_version"] = Version,
            ["case_version"] = 2,
            ["harness_sha256"] = TreeHash(Path.Combine(repo, Harness)),
            ["base_sha"] = baseSha,
            ["head_sha"] = headSha,
            ["selected"] = cases.Length > 0,
            ["changed_paths"] = JsonSerializer.SerializeToNode(paths),
            ["cases"] = CaseJson(cases),
            ["uncovered_paths"] = JsonSerializer.SerializeToNode(uncovered),
            ["omissions"] = JsonSerializer.SerializeToNode(omissions),
            ["selection_reason"] = all ? "manual --screening-all" : cases.Length > 1 ? "known Signals/common dependency" : cases.Length == 1 ? "unknown-change sentinel" : "documentation-only or empty diff",
            ["advisory"] = true,
            ["case_cap"] = 12,
        };
        Dump(Path.Combine(output, "plan.json"), data);
        if (cases.Length == 0)
        {
            Report(output, new JsonObject { ["schema_version"] = 2, ["status"] = "skipped", ["plan"] = data.DeepClone(), ["advisory"] = true });
        }

        string? github = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (!string.IsNullOrEmpty(github))
        {
            await File.AppendAllTextAsync(github, $"selected={(cases.Length > 0 ? "true" : "false")}\nbase_sha={baseSha}\nhead_sha={headSha}\n").ConfigureAwait(false);
        }

        return data;
    }

    public static ScreeningCase[] ValidatePlan(JsonNode plan, string repo)
    {
        if (plan["schema_version"]?.GetValue<int>() != 2 || plan["case_version"]?.GetValue<int>() != 2 || plan["harness_version"]?.ToString() != Version
            || plan["harness_sha256"]?.ToString() != TreeHash(Path.Combine(repo, Harness)))
        {
            throw new ScreeningFailure("incomparable", "Plan and runner versions/hashes differ");
        }

        ScreeningCase[] cases = (plan["cases"]?.AsArray() ?? []).Select(c => new ScreeningCase(c!["id"]!.GetValue<string>(), Finite(c["min_delta_ns"], "absolute floor"))).ToArray();
        if (cases.Length > 12 || cases.DistinctBy(c => c.Id).Count() != cases.Length || cases.Any(c => !Catalog.Contains(c)) || plan["selected"]?.GetValue<bool>() != (cases.Length > 0))
        {
            throw new ScreeningFailure("incomparable", "Invalid case catalog in plan");
        }

        foreach (string label in new[] { "base", "head" })
        {
            string sha = plan[label + "_sha"]?.ToString() ?? string.Empty;
            if (sha.Length != 40 || sha.Any(c => !Uri.IsHexDigit(c)))
            {
                throw new ScreeningFailure("incomparable", "Plan must contain exact commit SHAs");
            }
        }

        return cases;
    }

    public static async Task<string> SnapshotAsync(string repo, string sha, string destination, string harnessSource, ComponentScreeningProcess runner, string output, string label)
    {
        string archive = Path.Combine(output, label + "-source.tar");
        await runner.CallAsync("git", ["-C", repo, "archive", "--format=tar", "-o", archive, sha], repo, label + "-archive").ConfigureAwait(false);
        Directory.CreateDirectory(destination);
        double remaining = runner.LimitSeconds - runner.ElapsedSeconds;
        if (remaining <= 0)
        {
            throw new ScreeningFailure("timeout", "Total execution deadline exhausted before archive extraction");
        }

        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(remaining)))
        {
            try
            {
                await TarFile.ExtractToDirectoryAsync(archive, destination, overwriteFiles: false, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new ScreeningFailure("timeout", "Deadline exceeded during archive extraction; partial artifacts retained");
            }
        }

        File.Delete(archive);
        string target = Path.Combine(destination, Harness);
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        foreach (string file in SourceFiles(harnessSource))
        {
            string copy = Path.Combine(target, Path.GetRelativePath(harnessSource, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        Directory.CreateDirectory(Path.Combine(destination, "artifacts/nuget"));
        return Path.Combine(target, "Lumyte.Signals.Benchmark.csproj");
    }

    public static async Task<bool> RunAsync(string repo, string planFile, string output, string dotnet, double seconds, string? baseDirectory = null, string? headDirectory = null)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        var result = new JsonObject { ["schema_version"] = 2, ["status"] = "execution_error", ["advisory"] = true, ["harness_version"] = Version };
        var runner = new ComponentScreeningProcess(output, seconds);
        try
        {
            if (!double.IsFinite(seconds) || seconds <= 0)
            {
                throw new ScreeningFailure("execution_error", "Timeout must be finite and positive");
            }

            JsonNode plan = JsonNode.Parse(await File.ReadAllTextAsync(planFile).ConfigureAwait(false))!;
            result["plan"] = plan.DeepClone();
            ScreeningCase[] cases = ValidatePlan(plan, repo);
            if (cases.Length == 0)
            {
                result["status"] = "skipped";
                return true;
            }

            string scratch = Path.Combine(output, "scratch");
            if (Directory.Exists(scratch) || Directory.Exists(Path.Combine(output, "raw")))
            {
                throw new ScreeningFailure("execution_error", "Output scratch/raw already exists; use a fresh output directory");
            }

            var locations = new Dictionary<string, string> { ["base"] = Path.GetFullPath(baseDirectory ?? repo), ["head"] = Path.GetFullPath(headDirectory ?? repo) };
            JsonNode? policy = null;
            foreach ((string label, string location) in locations)
            {
                string sha = plan[label + "_sha"]!.ToString();
                if ((label == "base" ? baseDirectory : headDirectory) is not null)
                {
                    string actual = await runner.CallAsync("git", ["rev-parse", "HEAD"], location, label + "-sha").ConfigureAwait(false);
                    if (actual != sha)
                    {
                        throw new ScreeningFailure("incomparable", label + " checkout SHA differs from plan");
                    }
                }

                JsonNode current = JsonNode.Parse(await runner.CallAsync("git", ["show", sha + ":global.json"], location, label + "-global").ConfigureAwait(false))!;
                if (policy is not null && !JsonNode.DeepEquals(policy, current))
                {
                    throw new ScreeningFailure("incomparable", "global.json SDK policies differ; cross-SDK comparison unsupported");
                }

                policy = current;
            }

            var environment = new Dictionary<string, string?> { ["PATH"] = Path.GetDirectoryName(Path.GetFullPath(dotnet)) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") };
            var metadata = new JsonObject
            {
                ["machine"] = RuntimeInformation.OSArchitecture.ToString(),
                ["os"] = RuntimeInformation.OSDescription,
                ["cpuinfo"] = File.Exists("/proc/cpuinfo") ? await File.ReadAllTextAsync("/proc/cpuinfo").ConfigureAwait(false) : "unavailable",
                ["dotnet"] = dotnet,
                ["sdk_policy"] = policy!.DeepClone(),
                ["harness_sha256"] = TreeHash(Path.Combine(repo, Harness)),
                ["runner_sha256"] = FileHash(typeof(ComponentScreening).Assembly.Location),
                ["case_version"] = 2,
                ["environment"] = JsonSerializer.SerializeToNode(Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                    .Where(e => e.Key.ToString()!.StartsWith("DOTNET_", StringComparison.Ordinal) || e.Key.ToString()!.StartsWith("COMPlus_", StringComparison.Ordinal) || e.Key.ToString() is "LANG" or "TZ")
                    .ToDictionary(e => e.Key.ToString()!, e => e.Value?.ToString())),
            };
            result["metadata"] = metadata;
            string frozenHarness = Path.Combine(scratch, "frozen-harness");
            foreach (string source in SourceFiles(Path.Combine(repo, Harness)))
            {
                string target = Path.Combine(frozenHarness, Path.GetRelativePath(Path.Combine(repo, Harness), source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }

            if (TreeHash(frozenHarness) != plan["harness_sha256"]!.ToString())
            {
                throw new ScreeningFailure("incomparable", "Harness changed while freezing the plan overlay");
            }

            var projects = new Dictionary<string, string>();
            foreach ((string label, string location) in locations)
            {
                string destination = Path.Combine(scratch, label);
                projects[label] = await SnapshotAsync(location, plan[label + "_sha"]!.ToString(), destination, frozenHarness, runner, output, label).ConfigureAwait(false);
                string sdk;
                try
                {
                    sdk = await runner.CallAsync(dotnet, ["--version"], destination, label + "-sdk", environment).ConfigureAwait(false);
                }
                catch (ScreeningFailure error) when (error.Status != "timeout")
                {
                    throw new ScreeningFailure("incomparable", label + ": global.json SDK cannot be resolved; see SDK log");
                }

                metadata[label + "_sdk"] = sdk;
                if (sdk != policy["sdk"]?["version"]?.ToString())
                {
                    throw new ScreeningFailure("incomparable", "Resolved SDK is not the exact global.json version");
                }

                metadata[label + "_dotnet_info"] = await runner.CallAsync(dotnet, ["--info"], destination, label + "-dotnet-info", environment).ConfigureAwait(false);
            }

            var binaries = new Dictionary<string, string>();
            foreach ((string label, string project) in projects)
            {
                string directory = Path.GetDirectoryName(project)!;
                await runner.CallAsync(dotnet, ["build", project, "-c", "Release", "--nologo"], directory, label + "-build", environment).ConfigureAwait(false);
                string binary = Path.Combine(directory, "bin/Release/net10.0/Lumyte.Signals.Benchmark.dll");
                string smoke = await runner.CallAsync(dotnet, [binary, "--screening-smoke"], directory, label + "-smoke", environment).ConfigureAwait(false);
                if (smoke != "screening-smoke-ok:v2:12")
                {
                    throw new ScreeningFailure("execution_error", label + " smoke marker absent");
                }

                binaries[label] = binary;
                metadata[label + "_binary_hashes"] = JsonSerializer.SerializeToNode(Directory.GetFiles(Path.GetDirectoryName(binary)!, "*.dll").Order(StringComparer.Ordinal).ToDictionary(file => Path.GetFileName(file)!, FileHash));
            }

            var measurements = new Dictionary<string, JsonObject>();
            foreach ((string label, string binary) in binaries)
            {
                string raw = Path.Combine(output, "raw", label);
                await runner.CallAsync(dotnet, new[] { binary, "--screening", "--filter" }.Concat(cases.Select(c => c.Id)).Concat(["--artifacts", raw]), Path.GetDirectoryName(binary)!, label + "-timed", environment).ConfigureAwait(false);
                measurements[label] = ReadBdn(raw, cases);
                result[label + "_measurements"] = measurements[label].DeepClone();
            }

            JsonArray comparisons = Compare(measurements["base"], measurements["head"], cases);
            result["comparisons"] = comparisons;
            string status = comparisons.Any(c => c!["latency_candidate"]!.GetValue<bool>() || c["allocation_increase"]!.GetValue<bool>()) ? "candidates" : "no_candidates";
            result["status"] = status + (plan["uncovered_paths"]!.AsArray().Count > 0 ? "_partial_coverage" : string.Empty);
            return true;
        }
        catch (Exception error)
        {
            result["status"] = error is ScreeningFailure failure ? failure.Status : "execution_error";
            result["error"] = error.Message;
            return false;
        }
        finally
        {
            result["commands"] = JsonSerializer.SerializeToNode(runner.Commands);
            result["elapsed_seconds"] = runner.ElapsedSeconds;
            Report(output, result);
        }
    }

    public static void Report(string output, JsonObject result)
    {
        Dump(Path.Combine(output, "result.json"), result);
        var lines = new List<string>
        {
            "# Component screening (advisory)", string.Empty, "Status: **" + result["status"] + "**", string.Empty,
            "Single short BDN job, serial base then head. No statistical significance or performance guarantee.",
            "Candidates need dedicated follow-up; allocation increases are independent of latency.", string.Empty,
        };
        if (result["error"] is JsonNode error)
        {
            lines.Add("Error: " + error);
        }

        foreach (JsonNode? row in result["comparisons"]?.AsArray() ?? [])
        {
            string flag = row!["latency_candidate"]!.GetValue<bool>() ? "latency candidate" : "no latency candidate";
            if (row["allocation_increase"]!.GetValue<bool>())
            {
                flag += "; allocation increase";
            }

            lines.Add(FormattableString.Invariant($"- {row["id"]}: {row["base"]!["median_ns"]!.GetValue<double>():F3} → {row["head"]!["median_ns"]!.GetValue<double>():F3} ns ({row["delta_percent"]!.GetValue<double>():+0.0;-0.0;0.0}%, {row["delta_ns"]!.GetValue<double>():+0.000;-0.000;0.000} ns); {row["base"]!["allocated_bytes"]} → {row["head"]!["allocated_bytes"]} B/op — {flag}."));
        }

        JsonNode? plan = result["plan"];
        lines.AddRange([string.Empty, "## Compared revisions", "- Base: " + plan?["base_sha"], "- Head: " + plan?["head_sha"], "- Selection: " + plan?["selection_reason"], string.Empty, "## Coverage limitations"]);
        lines.AddRange((plan?["omissions"]?.AsArray() ?? []).Select(p => "- " + p));
        lines.AddRange((plan?["uncovered_paths"]?.AsArray() ?? []).Select(p => "- Uncovered changed path: " + p));
        if ((plan?["uncovered_paths"]?.AsArray().Count ?? 0) > 0)
        {
            lines.Add("- Sentinel results do not qualify uncovered components.");
        }

        lines.AddRange([string.Empty, "See result.json, command logs, metadata and raw/ for complete or partial evidence."]);
        File.WriteAllLines(Path.Combine(output, "summary.md"), lines);
        string? github = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(github))
        {
            File.AppendAllLines(github, lines);
        }
    }

    private static IEnumerable<string> SourceFiles(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Where(file => !Path.GetRelativePath(directory, file).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj" or "__pycache__"))
        .OrderBy(file => Path.GetRelativePath(directory, file).Replace('\\', '/'), StringComparer.Ordinal);

    private static JsonArray CaseJson(ScreeningCase[] cases) => new(cases.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["min_delta_ns"] = c.MinDeltaNs }).ToArray());
}
