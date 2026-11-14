using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

namespace Lumyte.Benchmarks;

/// <summary>Shared, source-linked entry point for product benchmark executables.</summary>
internal static class BenchmarkEntry
{
    private static readonly string[] _modes = ["verify", "screen", "compare", "diagnose"];

    internal static int Run(string[] args, Assembly assembly, Action verify)
    {
        try
        {
            EntryOptions options = Parse(args);
            if (options.Catalog)
            {
                Console.WriteLine(CreateCatalog(assembly));
                return 0;
            }

            // BDN owns help/list parsing, including its regular filters and formatting.
            bool informational = options.Arguments.Any(argument => argument is "--help" or "-h" or "--list");
            if (options.Mode == "verify" && !informational)
            {
                if (options.Arguments.Length != 0)
                {
                    throw new ArgumentException("Benchmark options require an explicit --mode screen, compare, or diagnose.");
                }

                verify();
                Console.WriteLine("verification-ok");
                return 0;
            }

            if (options.Mode == "diagnose" && !informational)
            {
                EnsureTraceSupported();
            }

            var errors = new ErrorLogger();
            ManualConfig config = ManualConfig.Create(CreateConfig(options.Mode == "verify" ? "diagnose" : options.Mode))
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
                        throw new ArgumentException("--mode requires verify, screen, compare, or diagnose.");
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

        if (catalog && remaining.Count > 0)
        {
            throw new ArgumentException("--catalog enumerates the complete assembly; do not combine it with BDN options.");
        }

        return new EntryOptions(mode ?? "verify", catalog, remaining.ToArray());
    }

    internal static IConfig CreateConfig(string mode)
    {
        Job job = CreateJob(mode);
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

    internal static string CreateCatalog(Assembly assembly)
    {
        var cases = new List<CatalogCase>();
        foreach (Type type in assembly.GetTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (!type.GetMethods().Any(method => method.IsDefined(typeof(BenchmarkAttribute), true)))
            {
                continue;
            }

            using BenchmarkRunInfo run = BenchmarkConverter.TypeToBenchmarks(type, CreateConfig("diagnose"));
            foreach (BenchmarkCase benchmark in run.BenchmarksCases)
            {
                cases.Add(new CatalogCase(
                    type.FullName ?? type.Name,
                    benchmark.Descriptor.WorkloadMethod.Name,
                    benchmark.Descriptor.Categories,
                    benchmark.Parameters.Items.Select(parameter => new CatalogParameter(
                        parameter.Name, parameter.ToSourceCode(), parameter.IsArgument)).ToArray(),
                    GetModes(benchmark)));
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

    internal static string[] GetModes(BenchmarkCase benchmark) =>
        benchmark.Descriptor.HasCategory("diagnostic-only") ? ["verify", "diagnose"] : _modes;

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

    internal sealed record EntryOptions(string Mode, bool Catalog, string[] Arguments);

    internal sealed record CatalogCase(string Type, string Method, string[] Categories, CatalogParameter[] Parameters, string[] Modes);

    internal sealed record CatalogParameter(string Name, string Value, bool IsArgument);
}
