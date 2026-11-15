using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Characteristics;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Validators;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

namespace Lumyte.Benchmarks;

/// <summary>Shared, source-linked entry point for product benchmark executables.</summary>
internal static class BenchmarkEntry
{
    private static readonly string[] _modes = ["screen", "compare", "diagnose"];

    internal static int Run(string[] args, Assembly assembly)
    {
        try
        {
            EntryOptions options = Parse(args);
            if (options.Catalog)
            {
                Console.WriteLine(CreateCatalog(assembly, options.CatalogFilter));
                return 0;
            }

            // BDN owns help/list parsing, including its regular filters and formatting.
            bool informational = options.Arguments.Any(argument => argument is "--help" or "-h" or "--list");
            if (options.Mode == "diagnose" && !informational)
            {
                EnsureTraceSupported();
            }

            var errors = new ErrorLogger();
            ManualConfig config = ManualConfig.Create(options.Mode is null
                ? DefaultConfig.Instance
                : CreateConfig(options.Mode, Environment.GetEnvironmentVariable("LUMYTE_BENCHMARK_PROJECT")))
                .AddLogger(errors);
            Summary[] summaries = BenchmarkSwitcher.FromAssembly(assembly).Run(options.Arguments, config).ToArray();
            if (errors.HasErrors)
            {
                return 1;
            }

            if (informational)
            {
                return 0;
            }

            return summaries.Length > 0 && summaries.All(summary =>
                !summary.HasCriticalValidationErrors && summary.Reports.Length > 0
                && summary.Reports.All(report => report.Success && report.ResultStatistics is { N: > 0 })) ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Benchmark entry failed: {exception.Message}");
            return 1;
        }
    }

    internal static EntryOptions Parse(string[] args)
    {
        string? mode = null;
        bool catalog = false;
        var remaining = new List<string>();
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument == "--mode" || argument.StartsWith("--mode=", StringComparison.Ordinal))
            {
                if (mode is not null)
                {
                    throw new ArgumentException("--mode may be supplied only once.");
                }

                if (argument == "--mode")
                {
                    if (++index == args.Length || args[index].StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new ArgumentException("--mode requires screen, compare, or diagnose.");
                    }

                    mode = args[index];
                }
                else
                {
                    mode = argument[7..];
                }

                if (!_modes.Contains(mode, StringComparer.Ordinal))
                {
                    throw new ArgumentException($"Unknown benchmark mode '{mode}'.");
                }
            }
            else if (argument == "--catalog")
            {
                if (catalog)
                {
                    throw new ArgumentException("--catalog may be supplied only once.");
                }

                catalog = true;
            }
            else
            {
                // These BDN switches can bypass the required Release/optimization policy.
                if (argument is "--disableOptimizationsValidator" or "--inProcess" or "-i"
                    || argument.StartsWith("--configuration", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"'{argument}' cannot override the benchmark execution policy.");
                }

                remaining.Add(argument);
            }
        }

        string? catalogFilter = null;
        if (catalog && remaining.Count > 0)
        {
            if (remaining.Count != 2 || remaining[0] != "--filter" || string.IsNullOrWhiteSpace(remaining[1])
                || remaining[1].StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException("--catalog accepts only --filter followed by one nonempty native BDN glob.");
            }

            catalogFilter = remaining[1];
            remaining.Clear();
        }

        bool informational = remaining.Any(argument => argument is "--help" or "-h" or "--list");
        if (mode is null && !catalog && args.Length > 0 && !informational)
        {
            throw new ArgumentException("Benchmark options require an explicit --mode screen, compare, or diagnose.");
        }

        return new EntryOptions(mode, catalog || args.Length == 0, remaining.ToArray(), catalogFilter);
    }

    internal static IConfig CreateConfig(string mode, string? projectFile = null)
    {
        Job job = CreateJob(mode);
        if (projectFile is not null)
        {
            var project = new FileInfo(projectFile);
            if (!project.Exists || project.Extension != ".csproj")
            {
                throw new ArgumentException("The explicit benchmark project must be an existing csproj.", nameof(projectFile));
            }

            job = job.WithToolchain(new ProjectToolchain(project, CsProjCoreToolchain.NetCoreApp10_0));
        }

        ManualConfig config = ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(job)
            .AddFilter(new ModeFilter(mode))
            .AddValidator(JitOptimizationsValidator.FailOnError)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(JsonExporter.Full);
        if (mode == "diagnose")
        {
            config.AddDiagnoser(new EventPipeProfiler(EventPipeProfile.CpuSampling, performExtraBenchmarksRun: true));
        }

        return config;
    }

    internal static Job CreateJob(string mode)
    {
        Job job = Job.Default.WithId($"Core-{mode}").WithOutlierMode(OutlierMode.DontRemove);
        job.Infrastructure.BuildConfiguration = "Release";
        return mode switch
        {
            "screen" or "diagnose" => job.WithLaunchCount(1).WithWarmupCount(3)
                .WithIterationCount(8).WithIterationTime(TimeInterval.FromMilliseconds(150)),

            // Independent processes within this revision; cross-revision qualification is external.
            "compare" => job.WithLaunchCount(3),
            _ => throw new ArgumentException($"'{mode}' is not a measurement mode.", nameof(mode)),
        };
    }

    internal static string CreateCatalog(Assembly assembly, string? filter = null)
    {
        var config = ManualConfig.Create(DefaultConfig.Instance);
        if (filter is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filter);
            config.AddFilter(new GlobFilter([filter]));
        }

        var cases = new List<CatalogCase>();
        foreach (Type type in assembly.GetTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (!type.GetMethods().Any(method => method.IsDefined(typeof(BenchmarkAttribute), true)))
            {
                continue;
            }

            // Catalog discovery includes cases without any measurement purpose too.
            using BenchmarkRunInfo run = BenchmarkConverter.TypeToBenchmarks(type, config);
            foreach (BenchmarkCase benchmark in run.BenchmarksCases.Where(benchmark => config.GetFilters().All(candidate => candidate.Predicate(benchmark))))
            {
                string resultKey = FullNameProvider.GetBenchmarkName(benchmark) + "|" + benchmark.Parameters.PrintInfo;
                cases.Add(new CatalogCase(
                    type.FullName ?? type.Name,
                    benchmark.Descriptor.WorkloadMethod.Name,
                    benchmark.Descriptor.Categories,
                    benchmark.Parameters.Items.Select(parameter => new CatalogParameter(
                        parameter.Name, parameter.ToSourceCode(), parameter.IsArgument)).ToArray(),
                    GetModes(benchmark),
                    resultKey));
            }
        }

        var catalog = new
        {
            SchemaVersion = 1,
            Assembly = new
            {
                Name = assembly.GetName().Name,
                Version = assembly.GetName().Version?.ToString(),
                assembly.Location,
                ModuleVersionId = assembly.ManifestModule.ModuleVersionId,
            },
            Runtime = new
            {
                Framework = RuntimeInformation.FrameworkDescription,
                Version = Environment.Version.ToString(),
                OS = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            },
            Modes = _modes,
            Cases = cases,
        };
        return JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static string[] GetModes(BenchmarkCase benchmark)
    {
        var modes = new List<string>();
        bool diagnosticOnly = benchmark.Descriptor.HasCategory("diagnostic-only");
        if (!diagnosticOnly && benchmark.Descriptor.HasCategory("Screening"))
        {
            modes.Add("screen");
        }

        if (!diagnosticOnly && benchmark.Descriptor.HasCategory("Comparison"))
        {
            modes.Add("compare");
        }

        if (benchmark.Descriptor.HasCategory("Diagnostic"))
        {
            modes.Add("diagnose");
        }

        return modes.ToArray();
    }

    internal static void EnsureTraceSupported()
    {
        if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            || !RuntimeInformation.FrameworkDescription.StartsWith(".NET ", StringComparison.Ordinal)
            || Environment.GetEnvironmentVariable("DOTNET_EnableDiagnostics") == "0"
            || Environment.GetEnvironmentVariable("DOTNET_EnableDiagnostics_IPC") == "0"
            || Environment.GetEnvironmentVariable("COMPlus_EnableDiagnostics") == "0")
        {
            throw new PlatformNotSupportedException("EventPipe tracing requires .NET 3+ on Windows, Linux, or macOS with diagnostic IPC enabled.");
        }
    }

    internal sealed class ModeFilter(string mode) : IFilter
    {
        public bool Predicate(BenchmarkCase benchmarkCase) => GetModes(benchmarkCase).Contains(mode, StringComparer.Ordinal);
    }

    internal sealed class ErrorLogger : ILogger
    {
        public string Id => nameof(ErrorLogger);

        public int Priority => 0;

        public bool HasErrors { get; private set; }

        public void Write(LogKind logKind, string text)
        {
            if (logKind == LogKind.Error)
            {
                HasErrors = true;
            }
        }

        public void WriteLine(LogKind logKind, string text) => Write(logKind, text);

        public void WriteLine()
        {
        }

        public void Flush()
        {
        }
    }

    internal sealed class ProjectGenerator(FileInfo project) : CsProjGenerator("net10.0", null!, null!, null!)
    {
        internal FileInfo Project => project;

        protected override FileInfo GetProjectFilePath(Type benchmarkTarget, ILogger logger)
        {
            if (Path.GetFileNameWithoutExtension(project.Name) != benchmarkTarget.Assembly.GetName().Name)
            {
                throw new InvalidOperationException("The explicit project does not own the benchmark assembly.");
            }

            return project;
        }
    }

    internal sealed class ProjectToolchain(FileInfo project, IToolchain standard)
        : Toolchain(standard.Name, new ProjectGenerator(project), standard.Builder, standard.Executor)
    {
        public override IEnumerable<ValidationError> Validate(BenchmarkCase benchmarkCase, IResolver resolver)
            => standard.Validate(benchmarkCase, resolver);
    }

    internal sealed record EntryOptions(string? Mode, bool Catalog, string[] Arguments, string? CatalogFilter = null);

    internal sealed record CatalogCase(string Type, string Method, string[] Categories, CatalogParameter[] Parameters, string[] Modes, string ResultKey);

    internal sealed record CatalogParameter(string Name, string Value, bool IsArgument);
}
