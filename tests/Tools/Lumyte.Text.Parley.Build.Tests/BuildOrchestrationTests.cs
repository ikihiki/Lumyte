using System.Text.Json;
using Xunit;

namespace Lumyte.Text.Parley.Build.Tests;

/// <summary>Runs the real .NET build entry point against controlled native-tool processes.</summary>
public sealed class BuildOrchestrationTests
{
    /// <summary>Native orchestration preserves argument boundaries, inherited state and exact toolchain notices without Python on PATH.</summary>
    /// <returns>A task representing completion of the build assertion.</returns>
    [Fact]
    public async Task NativeBuildCopiesArtifactsWithoutPythonOnPathAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Native", "--toolchain", "test-native");
        AssertSuccess(result);
        Assert.Equal("Test-only Parley artifact.", File.ReadAllText(Path.Combine(fixture.Output, BuildFixture.NativeFileName)));
        Assert.Equal("Exact test toolchain copyright.", File.ReadAllText(Path.Combine(fixture.Output, "RUST-STANDARD-LIBRARY-NOTICES.html")));
        JsonElement cargo = fixture.Record("cargo");
        Assert.Equal(
            ["+test-native", "build", "--manifest-path", Path.Combine(fixture.Root, "native", "lumyte_text_parley", "Cargo.toml"), "--locked", "--release", "--target-dir", Path.Combine(fixture.Output, "cargo")],
            Arguments(cargo));
        Assert.Equal(fixture.Environment["PATH"], Variable(cargo, "PATH"));
        Assert.Equal("inherited value 日本語", Variable(cargo, "PARLEY_TEST_INHERITED"));
        Assert.Equal(["+test-native", "--print", "sysroot"], Arguments(fixture.Record("rustc")));
        Assert.False(File.Exists(Path.Combine(fixture.Log, "python.json")));
    }

    /// <summary>The build returns failure when Cargo fails and does not publish stale outputs or query another tool.</summary>
    /// <returns>A task representing completion of the failed-build assertion.</returns>
    [Fact]
    public async Task CargoFailureStopsArtifactPublicationAsync()
    {
        using var fixture = new BuildFixture();
        fixture.Environment["PARLEY_TEST_CARGO_EXIT"] = "17";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Native");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("17", result.Output + result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(fixture.Output, BuildFixture.NativeFileName)));
        Assert.False(File.Exists(Path.Combine(fixture.Log, "rustc.json")));
    }

    /// <summary>A successful process exit cannot masquerade as a successful build when its expected output is missing.</summary>
    /// <returns>A task representing completion of the missing-output assertion.</returns>
    [Fact]
    public async Task MissingCargoArtifactFailsTheBuildAsync()
    {
        using var fixture = new BuildFixture();
        fixture.Environment["PARLEY_TEST_SKIP_ARTIFACT"] = "1";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Native");
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(fixture.Output, BuildFixture.NativeFileName)));
    }

    /// <summary>WASM rebuilds std using the workload SDK, preserves encoded flags and selects the explicit interpreter.</summary>
    /// <returns>A task representing completion of the WASM build assertions.</returns>
    [Fact]
    public async Task WasmBuildUsesTheSdkAndIsolatedPythonAsync()
    {
        using var fixture = new BuildFixture();
        fixture.Environment["CARGO_ENCODED_RUSTFLAGS"] = "--cfg\u001ftest_inherited";
        fixture.Environment["RUSTFLAGS"] = "ignored when encoded flags are supplied";
        fixture.Environment["PYTHON"] = "wrong ambient interpreter";
        fixture.Environment["PYTHONHOME"] = "wrong ambient home";
        fixture.Environment["PYTHONPATH"] = "wrong ambient libraries";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Wasm", WasmArguments(fixture));
        AssertSuccess(result);
        Assert.Equal("Test-only Parley artifact.", File.ReadAllText(Path.Combine(fixture.Output, "liblumyte_text_parley.a")));
        Assert.Equal("Exact test toolchain copyright.", File.ReadAllText(Path.Combine(fixture.Output, "RUST-STANDARD-LIBRARY-NOTICES.html")));
        JsonElement cargo = fixture.Record("cargo");
        Assert.Equal(
            ["+nightly-2026-10-20", "rustc", "-Zbuild-std=std,panic_abort", "--target", "wasm32-unknown-emscripten", "--crate-type", "staticlib", "--manifest-path", Path.Combine(fixture.Root, "native", "lumyte_text_parley", "Cargo.toml"), "--locked", "--release", "--target-dir", Path.Combine(fixture.Output, "cargo")],
            Arguments(cargo));
        Assert.Equal("--cfg\u001ftest_inherited\u001f-Cpanic=abort\u001f-Zllvm-target-feature=-bulk-memory-opt,-call-indirect-overlong", Variable(cargo, "CARGO_ENCODED_RUSTFLAGS"));
        Assert.Null(Variable(cargo, "RUSTFLAGS"));
        Assert.Equal("false", Variable(cargo, "CARGO_PROFILE_RELEASE_LTO"));
        Assert.Equal(fixture.Python, Variable(cargo, "EMSDK_PYTHON"));
        Assert.Equal(fixture.Python, Variable(cargo, "PYTHON"));
        Assert.Equal(fixture.Python, Variable(cargo, "PYTHON3"));
        Assert.Null(Variable(cargo, "PYTHONHOME"));
        Assert.Null(Variable(cargo, "PYTHONPATH"));
        Assert.Equal("1", Variable(cargo, "PYTHONNOUSERSITE"));
        Assert.Equal(Path.Combine(fixture.Sdk, "emscripten", ".emscripten"), Variable(cargo, "EM_CONFIG"));
        Assert.Equal(fixture.Cache, Variable(cargo, "EM_CACHE"));
        Assert.Equal("1", Variable(cargo, "EM_FROZEN_CACHE"));
        Assert.Equal(Path.Combine(fixture.Sdk, "bin"), Variable(cargo, "DOTNET_EMSCRIPTEN_LLVM_ROOT"));
        Assert.Equal(fixture.Sdk, Variable(cargo, "DOTNET_EMSCRIPTEN_BINARYEN_ROOT"));
        Assert.Equal(fixture.Node, Variable(cargo, "DOTNET_EMSCRIPTEN_NODE_JS"));
        string compiler = Path.Combine(fixture.Sdk, "emscripten", OperatingSystem.IsWindows() ? "emcc.bat" : "emcc");
        Assert.Equal(compiler, Variable(cargo, "CC_wasm32_unknown_emscripten"));
        Assert.Equal(compiler, Variable(cargo, "CARGO_TARGET_WASM32_UNKNOWN_EMSCRIPTEN_LINKER"));
        Assert.EndsWith(fixture.Environment["PATH"]!, Variable(cargo, "PATH"), StringComparison.Ordinal);
        JsonElement python = fixture.Record("python");
        Assert.Equal(["-I", "--version"], Arguments(python));
        Assert.Null(Variable(python, "PYTHONHOME"));
        Assert.Null(Variable(python, "PYTHONPATH"));
    }

    /// <summary>Ambiguous Rust flags fail before Cargo can receive a silently different compilation configuration.</summary>
    /// <returns>A task representing completion of the invalid-flags assertion.</returns>
    [Fact]
    public async Task WasmRejectsUnencodedRustFlagsAsync()
    {
        using var fixture = new BuildFixture();
        fixture.Environment["CARGO_ENCODED_RUSTFLAGS"] = null;
        fixture.Environment["RUSTFLAGS"] = "--cfg ambiguous";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Wasm", WasmArguments(fixture));
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CARGO_ENCODED_RUSTFLAGS", result.Output + result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "cargo.json")));
    }

    /// <summary>Invalid SDK settings are rejected before starting compiler or interpreter processes.</summary>
    /// <returns>A task representing completion of the invalid-SDK assertion.</returns>
    [Fact]
    public async Task InvalidSdkDoesNotStartChildToolsAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Wasm", "--emsdk", Path.Combine(fixture.Root, "missing SDK"), "--cache", fixture.Cache, "--node", fixture.Node, "--python", fixture.Python);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(fixture.Log));
    }

    /// <summary>The MSBuild handoff contains the selected interpreter's complete Unicode path.</summary>
    /// <returns>A task representing completion of the interpreter-handoff assertions.</returns>
    [Fact]
    public async Task PrepareWasmToolsWritesTheResolvedInterpreterAsync()
    {
        using var fixture = new BuildFixture();
        string pathFile = Path.Combine(fixture.Output, "interpreter path 日本語.txt");
        BuildFixture.ProcessResult result = await fixture.RunAsync("PrepareWasmTools", "--emsdk", fixture.Sdk, "--python", fixture.Python, "--python-path-file", pathFile);
        AssertSuccess(result);
        Assert.Equal(fixture.Python, File.ReadAllText(pathFile).TrimEnd('\r', '\n'));
        Assert.Equal(["-I", "--version"], Arguments(fixture.Record("python")));
        Assert.False(File.Exists(Path.Combine(fixture.Log, "cargo.json")));
        Assert.False(File.Exists(Path.Combine(fixture.Log, "rustc.json")));
    }

    /// <summary>An explicit missing interpreter fails instead of silently selecting an ambient Python or downloading one.</summary>
    /// <returns>A task representing completion of the missing-interpreter assertions.</returns>
    [Fact]
    public async Task ExplicitMissingPythonFailsBeforeCompilationAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Wasm", "--emsdk", fixture.Sdk, "--cache", fixture.Cache, "--node", fixture.Node, "--python", Path.Combine(fixture.Root, "missing Python"));
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Python", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(fixture.Log));
    }

    /// <summary>An unsupported interpreter cannot start a compiler even when its executable exits successfully.</summary>
    /// <returns>A task representing completion of the interpreter-version assertions.</returns>
    [Fact]
    public async Task UnsupportedPythonVersionFailsBeforeCompilationAsync()
    {
        using var fixture = new BuildFixture();
        fixture.Environment["PARLEY_TEST_PYTHON_VERSION"] = "Python 2.7.18";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Wasm", WasmArguments(fixture));
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Python", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(fixture.Log, "python.json")));
        Assert.False(File.Exists(Path.Combine(fixture.Log, "cargo.json")));
    }

    private static string[] WasmArguments(BuildFixture fixture)
        => ["--emsdk", fixture.Sdk, "--cache", fixture.Cache, "--node", fixture.Node, "--python", fixture.Python];

    private static string[] Arguments(JsonElement record) => record.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static string? Variable(JsonElement record, string name)
        => record.GetProperty("environment").GetProperty(name).GetString();

    private static void AssertSuccess(BuildFixture.ProcessResult result)
        => Assert.True(result.ExitCode == 0, result.Output + "\n" + result.Error);
}
