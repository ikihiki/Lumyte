using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Lumyte.Text.Parley.Build.Tests;

internal sealed class BuildFixture : IDisposable
{
    public BuildFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "Lumyte Parley ビルド " + Guid.NewGuid().ToString("N"));
        Output = Path.Combine(Root, "output 日本語 with spaces");
        Tools = Path.Combine(Root, "fake tools 日本語");
        Sysroot = Path.Combine(Root, "Rust sysroot 日本語");
        Log = Path.Combine(Root, "process log");
        Directory.CreateDirectory(Tools);
        Directory.CreateDirectory(Path.Combine(Root, "native", "lumyte_text_parley"));
        File.WriteAllText(Path.Combine(Root, "native", "lumyte_text_parley", "Cargo.toml"), "[package]\nname = \"build-test\"\nversion = \"0.1.0\"\n");
        Directory.CreateDirectory(Path.Combine(Sysroot, "share", "doc", "rust"));
        File.WriteAllText(Path.Combine(Sysroot, "share", "doc", "rust", "COPYRIGHT-library.html"), "Exact test toolchain copyright.");
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fake-tool")))
        {
            File.Copy(file, Path.Combine(Tools, Path.GetFileName(file)));
        }

        string appHost = Path.Combine(Tools, OperatingSystem.IsWindows() ? "FakeTool.exe" : "FakeTool");
        Cargo = CopyTool(appHost, "cargo");
        Rustc = CopyTool(appHost, "rustc");
        Python = CopyTool(appHost, "python");
        Node = CopyTool(appHost, "node");
        Sdk = Path.Combine(Root, "SDK tools 日本語");
        Directory.CreateDirectory(Path.Combine(Sdk, "emscripten"));
        Directory.CreateDirectory(Path.Combine(Sdk, "bin"));
        File.WriteAllText(Path.Combine(Sdk, "emscripten", "emcc.py"), "# Never executed by the fake Cargo fixture.\n");
        Cache = Path.Combine(Root, "SDK cache 日本語");
        Directory.CreateDirectory(Cache);
        Environment = new Dictionary<string, string?>
        {
            ["PATH"] = Tools + Path.PathSeparator + Path.Combine(Root, "inherited path tail"),
            ["DOTNET_ROOT"] = Path.GetDirectoryName(Dotnet),
            ["PARLEY_TEST_INHERITED"] = "inherited value 日本語",
            ["PARLEY_TEST_LOG"] = Log,
            ["PARLEY_TEST_SYSROOT"] = Sysroot,
        };
    }

    public static string NativeFileName => OperatingSystem.IsWindows() ? "lumyte_text_parley.dll" : OperatingSystem.IsMacOS() ? "liblumyte_text_parley.dylib" : "liblumyte_text_parley.so";

    public string Root { get; }

    public string Output { get; }

    public string Tools { get; }

    public string Sysroot { get; }

    public string Log { get; }

    public string Cargo { get; }

    public string Rustc { get; }

    public string Python { get; }

    public string Node { get; }

    public string Sdk { get; }

    public string Cache { get; }

    public Dictionary<string, string?> Environment { get; }

    private static string Dotnet => Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

    public async Task<ProcessResult> RunAsync(string target, params string[] extra)
    {
        string assembly = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "runner-path.txt")).Trim();
        var start = new ProcessStartInfo(Dotnet)
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(assembly);
        foreach (string argument in new[] { "--target", target, "--root", Root, "--output", Output, "--cargo", Cargo, "--rustc", Rustc }.Concat(extra))
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string key, string? value) in Environment)
        {
            if (value is null)
            {
                start.Environment.Remove(key);
            }
            else
            {
                start.Environment[key] = value;
            }
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start the build runner.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            return new ProcessResult(process.ExitCode, await output, await error);
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

    public JsonElement Record(string tool)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Log, tool + ".json")));
        return document.RootElement.Clone();
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private string CopyTool(string source, string name)
    {
        string destination = Path.Combine(Tools, name + " fixture" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        File.Copy(source, destination);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return destination;
    }

    internal readonly record struct ProcessResult(int ExitCode, string Output, string Error);
}
