using System.Text.Json;
using System.Xml.Linq;

namespace Lumyte.Build.Tests.FakeTool;

internal static class Program
{
    private static readonly string[] _observedVariables =
    [
        "PATH", "PARLEY_TEST_INHERITED", "CARGO_ENCODED_RUSTFLAGS", "RUSTFLAGS",
        "CARGO_PROFILE_RELEASE_LTO", "DOTNET_EMSCRIPTEN_LLVM_ROOT", "DOTNET_EMSCRIPTEN_BINARYEN_ROOT",
        "DOTNET_EMSCRIPTEN_NODE_JS", "EM_CONFIG", "EM_CACHE", "EM_FROZEN_CACHE", "EMSDK_PYTHON",
        "PYTHON", "PYTHON3", "PYTHONHOME", "PYTHONPATH", "PYTHONNOUSERSITE", "CC_wasm32_unknown_emscripten",
        "AR_wasm32_unknown_emscripten", "CARGO_TARGET_WASM32_UNKNOWN_EMSCRIPTEN_LINKER",
        "LUMYTE_BUILD_ASSEMBLY",
    ];

    private static int Main(string[] args)
    {
        string tool = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty).Split(' ')[0];
        string log = Environment.GetEnvironmentVariable("PARLEY_TEST_LOG") ?? throw new InvalidOperationException("Missing test log directory.");
        Dictionary<string, string?> observed = _observedVariables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        Directory.CreateDirectory(log);
        string record = JsonSerializer.Serialize(new { arguments = args, environment = observed });
        File.WriteAllText(Path.Combine(log, tool + ".json"), record);
        File.AppendAllText(Path.Combine(log, tool + ".jsonl"), record + "\n");

        if (tool == "dotnet")
        {
            string? failingCommand = Environment.GetEnvironmentVariable("LUMYTE_TEST_DOTNET_FAIL_COMMAND");
            if ((failingCommand is null || args.FirstOrDefault() == failingCommand) &&
                int.TryParse(Environment.GetEnvironmentVariable("LUMYTE_TEST_DOTNET_EXIT"), out int dotnetExitCode))
            {
                Console.Error.WriteLine("Requested fake dotnet failure: " + dotnetExitCode);
                return dotnetExitCode;
            }

            if (args.FirstOrDefault() == "msbuild" && args.Contains("-getProperty:IsPackable", StringComparer.Ordinal))
            {
                var project = XDocument.Load(args[1]);
                Console.WriteLine(project.Descendants("IsPackable").LastOrDefault()?.Value ?? "true");
            }

            return 0;
        }

        if (tool == "python")
        {
            Console.WriteLine(Environment.GetEnvironmentVariable("PARLEY_TEST_PYTHON_VERSION") ?? "Python 3.12.16");
            return 0;
        }

        if (tool == "rustc")
        {
            Console.WriteLine(Environment.GetEnvironmentVariable("PARLEY_TEST_SYSROOT"));
            return 0;
        }

        if (tool != "cargo")
        {
            Console.Error.WriteLine("Unexpected fake tool: " + tool);
            return 13;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("PARLEY_TEST_CARGO_EXIT"), out int exitCode) && exitCode != 0)
        {
            Console.Error.WriteLine("Requested fake Cargo failure: " + exitCode);
            return exitCode;
        }

        if (Environment.GetEnvironmentVariable("PARLEY_TEST_SKIP_ARTIFACT") == "1")
        {
            return 0;
        }

        int targetIndex = Array.IndexOf(args, "--target-dir");
        if (targetIndex < 0 || targetIndex + 1 >= args.Length)
        {
            throw new InvalidOperationException("Cargo did not receive its target directory as an argument.");
        }

        string target = args[targetIndex + 1];
        string[] files = args.Contains("wasm32-unknown-emscripten", StringComparer.Ordinal)
            ? [Path.Combine(target, "wasm32-unknown-emscripten", "release", "liblumyte_text_parley.a")]
            : [Path.Combine(target, "release", "liblumyte_text_parley.so"), Path.Combine(target, "release", "liblumyte_text_parley.dylib"), Path.Combine(target, "release", "lumyte_text_parley.dll")];
        foreach (string file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "Test-only Parley artifact.");
        }

        return 0;
    }
}
