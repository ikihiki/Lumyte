using System.Diagnostics;
using System.Security;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Tests the imported targets against clean, isolated compiler source trees.</summary>
public sealed class ShaderToolOutputPathTests
{
    /// <summary>Execution and incremental inputs follow the compiler's evaluated output directory.</summary>
    /// <param name="artifacts">Whether to use SDK artifacts output rather than BaseOutputPath.</param>
    /// <returns>The isolated build verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlternateOutputUsesSelectedCompilerAndDependenciesAsync(bool artifacts)
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "tools", "shaders", "Slang.targets")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        string directory = Path.Combine(Path.GetTempPath(), "lumyte-tool-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Only this source closure is copied: no default bin/obj can conceal a stale-tool fallback.
            string[] sources = ["tools/shaders/Lumyte.ShaderCompiler", "src/Graphics/Lumyte.Graphics.Shaders", "src/Graphics/Lumyte.Graphics.Abstractions"];
            foreach (string source in sources)
            {
                foreach (string file in Directory.EnumerateFiles(Path.Combine(root, source), "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(Path.Combine(root, source), file);
                    if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                    {
                        continue;
                    }

                    string destination = Path.Combine(directory, source, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                }
            }

            string shaders = Path.Combine(directory, "tools", "shaders");
            Directory.CreateDirectory(Path.Combine(shaders, "include"));
            File.Copy(Path.Combine(root, "tools", "shaders", "Slang.targets"), Path.Combine(shaders, "Slang.targets"));
            File.Copy(Path.Combine(root, "tools", "shaders", "include", "lumyte.slang"), Path.Combine(shaders, "include", "lumyte.slang"));
            File.Copy(Path.Combine(root, "global.json"), Path.Combine(directory, "global.json"));
            File.Copy(Path.Combine(root, "mise.toml"), Path.Combine(directory, "mise.toml"));
            string output = SecurityElement.Escape(Path.Combine(directory, "alternate-output"));
            string properties = $"""
                <Project><PropertyGroup>
                  <Nullable>enable</Nullable>
                  <UseArtifactsOutput>{artifacts.ToString().ToLowerInvariant()}</UseArtifactsOutput>
                  {(artifacts ? $"<ArtifactsPath>{output}</ArtifactsPath>" : $"<BaseOutputPath>{output}/$(MSBuildProjectName)/</BaseOutputPath>")}
                </PropertyGroup></Project>
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.props"), properties);
            await File.WriteAllTextAsync(Path.Combine(directory, "fixture.slang"), """[shader("compute")] [numthreads(1,1,1)] void main() {}""");
            string project = Path.Combine(directory, "fixture.proj");
            string fixture = """
                <Project>
                  <PropertyGroup><IntermediateOutputPath>shader-output/</IntermediateOutputPath></PropertyGroup>
                  <ItemGroup><SlangShader Include="fixture.slang" ResourceName="fixture" Stage="compute" Targets="spirv" /></ItemGroup>
                  <Import Project="tools/shaders/Slang.targets" />
                </Project>
                """;
            await File.WriteAllTextAsync(project, fixture);
            string compilerProject = Path.Combine(shaders, "Lumyte.ShaderCompiler", "Lumyte.ShaderCompiler.csproj");
            await RunAsync(directory, "restore", compilerProject, "-p:Configuration=Release", "--verbosity:quiet");
            string compiler = Environment.GetEnvironmentVariable("PATH")!
                .Split(Path.PathSeparator)
                .Select(path => Path.Combine(path, OperatingSystem.IsWindows() ? "slangc.exe" : "slangc"))
                .First(File.Exists);
            string[] build = ["msbuild", project, "-t:CompileSlangShaders", "-v:minimal", "-nologo", "-nr:false", "-m:1", "-p:SlangCompilerPath=" + compiler];
            await RunAsync(directory, build);
            string artifact = Path.Combine(directory, "shader-output", "Slang", "fixture.lshader");
            Assert.True(File.Exists(artifact));
            Assert.False(Directory.Exists(Path.Combine(shaders, "Lumyte.ShaderCompiler", "bin")));
            DateTime first = File.GetLastWriteTimeUtc(artifact);
            await RunAsync(directory, build);
            Assert.Equal(first, File.GetLastWriteTimeUtc(artifact));

            // A populated but invalid default output must never be selected instead.
            string staleDefault = Path.Combine(shaders, "Lumyte.ShaderCompiler", "bin", "Release", "net10.0");
            Directory.CreateDirectory(staleDefault);
            await File.WriteAllTextAsync(Path.Combine(staleDefault, "Lumyte.ShaderCompiler.dll"), "stale default compiler trap");
            File.Delete(artifact);
            await RunAsync(directory, build);
            Assert.True(File.Exists(artifact));

            // Touch runtime implementations, never reference assemblies: compiler
            // recompilation would conceal a missing dependency incremental input.
            string[] assemblies = ["Lumyte.ShaderCompiler.dll", "Lumyte.Graphics.Shaders.dll", "Lumyte.Graphics.Abstractions.dll"];
            foreach (string assembly in assemblies)
            {
                string runtimeConfig = Assert.Single(Directory.GetFiles(Path.Combine(directory, "alternate-output"), "Lumyte.ShaderCompiler.runtimeconfig.json", SearchOption.AllDirectories));
                string selected = Path.Combine(Path.GetDirectoryName(runtimeConfig)!, assembly);
                Assert.True(File.Exists(selected));
                await Task.Delay(1100);
                DateTime updated = DateTime.UtcNow;
                File.SetLastWriteTimeUtc(selected, updated);
                foreach (string built in Directory.GetFiles(directory, assembly, SearchOption.AllDirectories))
                {
                    if (!built.StartsWith(staleDefault + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                        !Path.GetRelativePath(directory, built).Split(Path.DirectorySeparatorChar).Any(part => part is "ref" or "refint"))
                    {
                        // Keep implementation copies consistent when Build prepares the tool.
                        File.SetLastWriteTimeUtc(built, updated);
                    }
                }

                string selectedCompiler = Path.Combine(Path.GetDirectoryName(runtimeConfig)!, "Lumyte.ShaderCompiler.dll");
                DateTime compilerTime = File.GetLastWriteTimeUtc(selectedCompiler);
                DateTime previous = File.GetLastWriteTimeUtc(artifact);
                await RunAsync(directory, build);
                Assert.Equal(compilerTime, File.GetLastWriteTimeUtc(selectedCompiler));
                Assert.True(File.GetLastWriteTimeUtc(artifact) > previous, assembly + " must invalidate shader output.");
            }

            // Build a real packable shader consumer, then forbid every source build,
            // restore and compiler invocation. NoBuild must consume its embedded assembly.
            string consumer = Path.Combine(directory, "package");
            Directory.CreateDirectory(consumer);
            string consumerProject = Path.Combine(consumer, "fixture.csproj");
            string consumerXml = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup><SlangShader Include="../fixture.slang" ResourceName="fixture" Targets="spirv" /></ItemGroup>
                  <Import Project="../tools/shaders/Slang.targets" />
                </Project>
                """;
            await File.WriteAllTextAsync(consumerProject, consumerXml);
            await RunAsync(directory, "build", consumerProject, "-c", "Release", "-p:SlangCompilerPath=" + compiler);
            string consumerAssembly = Assert.Single(
                Directory.GetFiles(Path.Combine(directory, "alternate-output"), "fixture.dll", SearchOption.AllDirectories),
                path => !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar).Any(part => part is "ref" or "refint" or "obj"));
            byte[] assemblyBytes = await File.ReadAllBytesAsync(consumerAssembly);
            using (var embedded = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(assemblyBytes)))
            {
                System.Reflection.Metadata.MetadataReader metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(embedded);
                Assert.Contains(metadata.ManifestResources, handle => metadata.GetString(metadata.GetManifestResource(handle).Name) == "fixture.lshader");
            }

            string traps = """
                <Project>
                  <Target Name="ForbidSourceWork" BeforeTargets="Build;CoreCompile;Restore">
                    <Error Text="NoBuild packaging attempted a build or restore." />
                  </Target>
                </Project>
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.targets"), traps);
            string[] pack = ["pack", consumerProject, "-c", "Release", "--no-build", "--no-restore", "-o", Path.Combine(directory, "packages"), "-p:DotNetHostPath=" + Path.Combine(directory, "compiler-execution-trap"), "-p:SlangCompilerPath=" + Path.Combine(directory, "native-compiler-trap")];
            string consumerArtifact = Assert.Single(Directory.GetFiles(consumer, "fixture.lshader", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(directory, "alternate-output"), "fixture.lshader", SearchOption.AllDirectories)));
            DateTime artifactTime = File.GetLastWriteTimeUtc(consumerArtifact);
            await File.AppendAllTextAsync(Path.Combine(directory, "fixture.slang"), "\n// newer source must not compile during pack");
            File.SetLastWriteTimeUtc(Path.Combine(directory, "fixture.slang"), DateTime.UtcNow.AddMinutes(1));
            await RunAsync(directory, pack);
            Assert.Equal(artifactTime, File.GetLastWriteTimeUtc(consumerArtifact));

            // Missing intermediates and missing tool outputs are also valid: the final
            // assembly already contains the shader. Missing final assembly is not valid.
            File.Delete(consumerArtifact);
            string toolRuntime = Assert.Single(
                Directory.GetFiles(Path.Combine(directory, "alternate-output"), "Lumyte.ShaderCompiler.runtimeconfig.json", SearchOption.AllDirectories),
                path => Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar).Contains("Lumyte.ShaderCompiler"));
            string selectedTool = Path.Combine(Path.GetDirectoryName(toolRuntime)!, "Lumyte.ShaderCompiler.dll");
            File.Delete(selectedTool);
            string resolvedPath = Path.Combine(directory, "resolved-tool.txt");
            string verifyToolXml = """
                  <Target Name="VerifyExistingTool" DependsOnTargets="PrepareLumyteShaderCompiler">
                    <WriteLinesToFile File="resolved-tool.txt" Lines="$(_LumyteShaderTool)" Overwrite="true" />
                  </Target>
                </Project>
                """;
            await File.WriteAllTextAsync(project, fixture.Replace("</Project>", verifyToolXml, StringComparison.Ordinal));
            await RunAsync(directory, "msbuild", project, "-t:VerifyExistingTool", "-p:NoBuild=true", "-nologo");
            Assert.Equal(selectedTool, (await File.ReadAllTextAsync(resolvedPath)).Trim());
            Assert.False(File.Exists(selectedTool));
            File.Delete(Path.Combine(directory, "fixture.slang"));
            File.Delete(Assert.Single(Directory.GetFiles(Path.Combine(directory, "packages"), "*.nupkg")));
            await RunAsync(directory, pack);
            Assert.False(File.Exists(consumerArtifact));
            Assert.False(File.Exists(selectedTool));
            using (System.IO.Compression.ZipArchive package = System.IO.Compression.ZipFile.OpenRead(Assert.Single(Directory.GetFiles(Path.Combine(directory, "packages"), "*.nupkg"))))
            {
                using Stream packagedAssembly = package.GetEntry("lib/net10.0/fixture.dll")!.Open();
                using var copy = new MemoryStream();
                await packagedAssembly.CopyToAsync(copy);
                Assert.Equal(assemblyBytes, copy.ToArray());
            }

            File.Delete(consumerAssembly);
            string failure = await RunAsync(directory, false, pack);
            Assert.Contains("NU5026", failure);
            Assert.DoesNotContain("attempted a build or restore", failure);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A bare PATH compiler is a file dependency, not a perpetually missing relative file.</summary>
    /// <returns>The isolated fake-compiler invocation-count verification.</returns>
    /// <param name="absoluteCompiler">Selects the original explicit absolute compiler control.</param>
    [LinuxTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultPathCompilerIsIncrementalWithoutAnOverrideAsync(bool absoluteCompiler)
        => await VerifyCompilerLookupAsync(absoluteCompiler, false);

    /// <summary>The child tool prefers its current directory over PATH, including after only that compiler changes.</summary>
    /// <param name="shellOutsideProject">Whether the calling shell starts outside the project directory.</param>
    /// <returns>The isolated current-directory shadow verification.</returns>
    [LinuxTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentDirectoryCompilerShadowInvalidatesAsync(bool shellOutsideProject)
        => await VerifyCompilerLookupAsync(false, true, shellOutsideProject);

    /// <summary>Different or wrapped hosts cannot make the task assume its own executable directory.</summary>
    /// <param name="copiedExecutable">Whether to use a private copied muxer rather than an opaque wrapper.</param>
    /// <returns>The isolated executable-directory or wrapper verification.</returns>
    [LinuxTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DifferentChildHostConservativelyRebuildsAsync(bool copiedExecutable)
        => await VerifyCompilerLookupAsync(false, true, false, copiedExecutable, !copiedExecutable);

    /// <summary>Unqualified partial PATH permissions rebuild rather than pretending to match runtime ownership rules.</summary>
    /// <param name="partialPermissions">Selects the deliberately unqualified permission shape.</param>
    /// <returns>The isolated conservative rebuild verification.</returns>
    [LinuxTheory]
    [InlineData(true)]
    public async Task UncertainPathPermissionsRebuildAsync(bool partialPermissions)
        => await VerifyCompilerLookupAsync(false, false, partialPermissions: partialPermissions);

    /// <summary>Shell-normalized backslash arguments must not cache an unexecuted compiler identity.</summary>
    /// <param name="hostArgument">Whether the backslashes belong to the host rather than the compiler.</param>
    /// <returns>The isolated shell argument identity verification.</returns>
    [LinuxTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShellBackslashIdentityConservativelyRebuildsAsync(bool hostArgument)
        => await VerifyCompilerLookupAsync(hostArgument, false, shellCompiler: !hostArgument, shellHost: hostArgument);

    private static async Task VerifyCompilerLookupAsync(bool absoluteCompiler, bool currentShadow, bool shellOutsideProject = false, bool executableShadow = false, bool wrappedHost = false, bool partialPermissions = false, bool shellCompiler = false, bool shellHost = false)
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX fake execution is not Windows runtime coverage.
        }

        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "tools", "shaders", "Slang.targets")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        string directory = Path.Combine(Path.GetTempPath(), "lumyte-default-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string[] sources = ["tools/shaders/Lumyte.ShaderCompiler", "src/Graphics/Lumyte.Graphics.Shaders", "src/Graphics/Lumyte.Graphics.Abstractions"];
            foreach (string source in sources)
            {
                foreach (string file in Directory.EnumerateFiles(Path.Combine(root, source), "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(Path.Combine(root, source), file);
                    if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                    {
                        continue;
                    }

                    string destination = Path.Combine(directory, source, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                }
            }

            string targets = Path.Combine(directory, "tools/shaders/Slang.targets");
            File.Copy(Path.Combine(root, "tools/shaders/Slang.targets"), targets);
            Directory.CreateDirectory(Path.Combine(directory, "tools/shaders/include"));
            File.Copy(Path.Combine(root, "tools/shaders/include/lumyte.slang"), Path.Combine(directory, "tools/shaders/include/lumyte.slang"));
            File.Copy(Path.Combine(root, "global.json"), Path.Combine(directory, "global.json"));
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.props"), "<Project><PropertyGroup><Nullable>enable</Nullable><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(directory, "fixture.slang"), "[shader(\"compute\")] [numthreads(1,1,1)] void main() {}");
            string defaultFixture = """
                <Project>
                  <PropertyGroup><IntermediateOutputPath>shader-output/</IntermediateOutputPath></PropertyGroup>
                  <ItemGroup><SlangShader Include="fixture.slang" ResourceName="fixture" Targets="spirv" /></ItemGroup>
                  <Import Project="tools/shaders/Slang.targets" />
                  <Target Name="DescribeCompiler"><WriteLinesToFile File="evaluated-compiler.txt" Lines="$(SlangCompilerPath)|$(_LumyteSlangCompilerInput)|$(IntermediateOutputPath)Slang/fixture.lshader" Overwrite="true" /></Target>
                </Project>
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "fixture.proj"), defaultFixture);
            string tools = Path.Combine(directory, "fake tools ü");
            Directory.CreateDirectory(tools);
            string fake = Path.Combine(tools, "slangc");
            string fakeScript = """
                #!/bin/sh
                if [ "$1" = -version ]; then
                  /usr/bin/readlink "/proc/$PPID/exe" > "$LUMYTE_FAKE_CHILD_RUNTIME"
                  /bin/grep -m1 '/shared/Microsoft.NETCore.App/' "/proc/$PPID/maps" >> "$LUMYTE_FAKE_CHILD_RUNTIME"
                  echo fake-slang-version
                  exit 0
                fi
                printf 'compile\n' >> "$LUMYTE_FAKE_SLANG_LOG"
                while [ "$#" -gt 0 ]; do
                  case "$1" in -o) shift; output="$1" ;; -reflection-json) shift; reflection="$1" ;; esac
                  shift
                done
                printf '\003\002\043\007\000\000\000\000\000\000\000\000\000\000\000\000\000\000\000\000' > "$output"
                printf '{"parameters":[],"entryPoints":[]}' > "$reflection"
                """;
            await File.WriteAllTextAsync(fake, fakeScript);
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            if (partialPermissions)
            {
                File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            string pathLog = Path.Combine(directory, "invocations.txt");
            string cwdLog = Path.Combine(directory, "cwd-invocations.txt");
            string runtimeLog = Path.Combine(directory, "child-runtime.txt");
            string local = Path.Combine(directory, "slangc");
            if (currentShadow)
            {
                await File.WriteAllTextAsync(local, fakeScript.Replace("LUMYTE_FAKE_SLANG_LOG", "LUMYTE_FAKE_CWD_SLANG_LOG", StringComparison.Ordinal));
                File.SetUnixFileMode(local, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            string hostLog = Path.Combine(directory, "host-invocations.txt");
            string privateHost = Path.Combine(directory, "private-host");
            string hostCompiler = Path.Combine(privateHost, "slangc");
            string childHost = Path.Combine(privateHost, "dotnet");
            bool unknownHost = executableShadow || wrappedHost;
            if (unknownHost)
            {
                Directory.CreateDirectory(privateHost);
                string originalHost = Environment.ProcessPath!;
                if (executableShadow)
                {
                    File.Copy(originalHost, childHost);
                    string hostRoot = Path.GetDirectoryName(originalHost)!;
                    Directory.CreateSymbolicLink(Path.Combine(privateHost, "host"), Path.Combine(hostRoot, "host"));
                    Directory.CreateSymbolicLink(Path.Combine(privateHost, "shared"), Path.Combine(hostRoot, "shared"));
                }
                else
                {
                    await File.WriteAllTextAsync(childHost, "#!/bin/sh\nexec \"" + originalHost + "\" \"$@\"\n");
                }

                File.SetUnixFileMode(childHost, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                await File.WriteAllTextAsync(hostCompiler, fakeScript.Replace("LUMYTE_FAKE_SLANG_LOG", "LUMYTE_FAKE_HOST_SLANG_LOG", StringComparison.Ordinal));
                File.SetUnixFileMode(hostCompiler, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            string shellActualLog = Path.Combine(directory, "shell-actual-invocations.txt");
            string shellActual = Path.Combine(tools, "shell" + (char)92 + "tool");
            string shellTracked = Path.Combine(tools, "shell" + new string((char)92, 2) + "tool");
            string shellHostMarker = Path.Combine(directory, "shell-host-invocations.txt");
            string shellNormalizedLog = Path.Combine(directory, "shell-normalized-invocations.txt");
            string shellNormalizedHostLog = Path.Combine(directory, "shell-normalized-host-invocations.txt");
            string observedShellHostLog = shellHostMarker;
            string shellUnusedLog = Path.Combine(directory, "shell-unused-invocations.txt");
            string shellNormalized = Path.Combine(tools, "shell", "tool");
            if (shellCompiler || shellHost)
            {
                string script = fakeScript.Replace("LUMYTE_FAKE_SLANG_LOG", "LUMYTE_FAKE_SHELL_SLANG_LOG", StringComparison.Ordinal);
                if (shellHost)
                {
                    script = "#!/bin/sh\nprintf 'host\\n' >> \"$LUMYTE_FAKE_SHELL_HOST_LOG\"\nexec \"" + Environment.ProcessPath + "\" \"$@\"\n";
                }

                await File.WriteAllTextAsync(shellTracked, script.Replace("LUMYTE_FAKE_SHELL_", "LUMYTE_FAKE_UNUSED_", StringComparison.Ordinal));
                await File.WriteAllTextAsync(shellActual, script);
                File.SetUnixFileMode(shellTracked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.SetUnixFileMode(shellActual, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

                // MSBuild's filesystem input normalization can interpret backslashes
                // as separators. Keep that unrelated alias present too: it must not
                // accidentally hide a stale hit by forcing a missing-input rebuild.
                string aliasDirectory = Path.Combine(tools, "shell");
                Directory.CreateDirectory(aliasDirectory);
                await File.WriteAllTextAsync(shellNormalized, script.Replace("LUMYTE_FAKE_SHELL_SLANG_LOG", "LUMYTE_FAKE_NORMALIZED_SLANG_LOG", StringComparison.Ordinal).Replace("LUMYTE_FAKE_SHELL_HOST_LOG", "LUMYTE_FAKE_NORMALIZED_HOST_LOG", StringComparison.Ordinal));
                File.SetUnixFileMode(shellNormalized, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            string log = shellCompiler ? shellActualLog : executableShadow ? hostLog : currentShadow ? cwdLog : pathLog;
            var environment = new Dictionary<string, string> { ["PATH"] = tools + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"), ["LUMYTE_FAKE_SLANG_LOG"] = pathLog, ["LUMYTE_FAKE_CWD_SLANG_LOG"] = cwdLog, ["LUMYTE_FAKE_CHILD_RUNTIME"] = runtimeLog, ["LUMYTE_FAKE_HOST_SLANG_LOG"] = hostLog, ["LUMYTE_FAKE_SHELL_SLANG_LOG"] = shellActualLog, ["LUMYTE_FAKE_SHELL_HOST_LOG"] = shellHostMarker, ["LUMYTE_FAKE_NORMALIZED_SLANG_LOG"] = shellNormalizedLog, ["LUMYTE_FAKE_NORMALIZED_HOST_LOG"] = shellNormalizedHostLog, ["LUMYTE_FAKE_UNUSED_SLANG_LOG"] = shellUnusedLog, ["LUMYTE_FAKE_UNUSED_HOST_LOG"] = shellUnusedLog };
            string offlineFeed = Path.Combine(directory, "offline-feed");
            Directory.CreateDirectory(offlineFeed);
            await RunWithEnvironmentAsync(directory, true, environment, "restore", Path.Combine(directory, "tools/shaders/Lumyte.ShaderCompiler/Lumyte.ShaderCompiler.csproj"), "-p:Configuration=Release", "--verbosity:quiet", "--source", offlineFeed);
            string[] build = ["msbuild", Path.Combine(directory, "fixture.proj"), "-t:CompileSlangShaders;DescribeCompiler", "-v:normal", "-nologo", "-nr:false", "-m:1"];
            if (absoluteCompiler)
            {
                build = [.. build, "-p:SlangCompilerPath=" + fake];
            }

            if (unknownHost)
            {
                build = [.. build, "-p:DotNetHostPath=" + childHost];
            }

            if (shellCompiler)
            {
                build = [.. build, "-p:SlangCompilerPath=" + shellTracked];
            }
            else if (shellHost)
            {
                build = [.. build, "-p:DotNetHostPath=" + shellTracked];
            }

            // Start MSBuild outside the project directory for the CWD-shadow case.
            string buildDirectory = shellOutsideProject ? Path.GetDirectoryName(directory)! : directory;
            string first = await RunWithEnvironmentAsync(buildDirectory, true, environment, build);
            if (shellCompiler && File.Exists(shellNormalizedLog))
            {
                // Some MSBuild versions normalize command paths before the shell.
                // Observe the actual callee, rather than assuming either transform.
                log = shellNormalizedLog;
                shellActual = shellNormalized;
            }
            else if (shellHost && File.Exists(shellNormalizedHostLog))
            {
                shellActual = shellNormalized;
                observedShellHostLog = shellNormalizedHostLog;
            }

            Assert.Single(await File.ReadAllLinesAsync(log));
            if (shellCompiler || shellHost)
            {
                await File.AppendAllTextAsync(shellActual, "\n# only the post-shell selected executable changes\n");
                File.SetLastWriteTimeUtc(shellActual, DateTime.UtcNow.AddDays(-1));
                string changed = await RunWithEnvironmentAsync(buildDirectory, true, environment, build);
                string? shellEvidence = Environment.GetEnvironmentVariable("LUMYTE_SHADER_INCREMENTAL_EVIDENCE");
                if (shellEvidence is not null)
                {
                    Directory.CreateDirectory(shellEvidence);
                    File.Copy(targets, Path.Combine(shellEvidence, "tested-Slang.targets"), true);
                    File.Copy(log, Path.Combine(shellEvidence, "actual-invocations.txt"), true);
                    File.Copy(Path.Combine(directory, "evaluated-compiler.txt"), Path.Combine(shellEvidence, "evaluated-compiler.txt"), true);
                    await File.WriteAllTextAsync(Path.Combine(shellEvidence, "second.log"), changed);
                }

                Assert.Equal(2, (await File.ReadAllLinesAsync(log)).Length);
                Assert.False(File.Exists(shellUnusedLog));
                if (shellCompiler)
                {
                    Assert.False(File.Exists(pathLog));
                }
                else
                {
                    Assert.Equal(2, (await File.ReadAllLinesAsync(observedShellHostLog)).Length);
                }

                return;
            }

            string second = await RunWithEnvironmentAsync(buildDirectory, true, environment, build);
            string? evidence = Environment.GetEnvironmentVariable("LUMYTE_SHADER_INCREMENTAL_EVIDENCE");
            if (evidence is not null)
            {
                Directory.CreateDirectory(evidence);
                await File.WriteAllTextAsync(Path.Combine(evidence, "first.log"), first);
                await File.WriteAllTextAsync(Path.Combine(evidence, "second.log"), second);
                File.Copy(Path.Combine(directory, "evaluated-compiler.txt"), Path.Combine(evidence, "evaluated-compiler.txt"), true);
                File.Copy(targets, Path.Combine(evidence, "tested-Slang.targets"), true);
                File.Copy(log, Path.Combine(evidence, "invocations.txt"), true);
            }

            Assert.Equal(unknownHost || partialPermissions ? 2 : 1, (await File.ReadAllLinesAsync(log)).Length);
            if (partialPermissions)
            {
                await File.AppendAllTextAsync(fake, "\n# changed uncertain compiler\n");
                await RunWithEnvironmentAsync(buildDirectory, true, environment, build);
                Assert.Equal(3, (await File.ReadAllLinesAsync(log)).Length);
                return;
            }

            if (currentShadow)
            {
                Assert.False(File.Exists(pathLog));
                string actualCompiler = executableShadow ? hostCompiler : local;
                await File.AppendAllTextAsync(actualCompiler, "\n# only the actual selected compiler changes\n");
                File.SetLastWriteTimeUtc(actualCompiler, DateTime.UtcNow.AddDays(-1));
                string changed = await RunWithEnvironmentAsync(buildDirectory, true, environment, build);
                if (evidence is not null)
                {
                    await File.WriteAllTextAsync(Path.Combine(evidence, "shadow-change.log"), changed);
                    File.Copy(log, Path.Combine(evidence, executableShadow ? "host-invocations.txt" : "cwd-invocations.txt"), true);
                    File.Copy(runtimeLog, Path.Combine(evidence, "child-runtime.txt"), true);
                    await File.WriteAllTextAsync(Path.Combine(evidence, "path-invocations-count.txt"), File.Exists(pathLog) ? (await File.ReadAllLinesAsync(pathLog)).Length.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0");
                }

                Assert.Equal(unknownHost ? 3 : 2, (await File.ReadAllLinesAsync(log)).Length);
                Assert.False(File.Exists(pathLog));
                string observedHost = (await File.ReadAllLinesAsync(runtimeLog))[0];
                Assert.Equal(executableShadow ? childHost : Environment.ProcessPath, observedHost);
                if (executableShadow)
                {
                    Assert.False(File.Exists(cwdLog));
                }
                else
                {
                    Assert.False(File.Exists(hostLog));
                }

                return;
            }

            string artifact = Path.Combine(directory, "shader-output/Slang/fixture.lshader");
            byte[] originalArtifact = await File.ReadAllBytesAsync(artifact);
            int expected = 1;
            async Task RebuildForAsync(string input)
            {
                DateTime originalTime = File.GetLastWriteTimeUtc(input);
                File.SetLastWriteTimeUtc(input, File.GetLastWriteTimeUtc(artifact).AddMinutes(1));
                await RunWithEnvironmentAsync(directory, true, environment, build);
                Assert.Equal(++expected, (await File.ReadAllLinesAsync(log)).Length);
                File.SetLastWriteTimeUtc(input, originalTime);
            }

            await RebuildForAsync(Path.Combine(directory, "fixture.slang"));
            await RebuildForAsync(Path.Combine(directory, "tools/shaders/include/lumyte.slang"));
            await File.WriteAllTextAsync(Path.Combine(directory, "fixture.proj"), defaultFixture.Replace("Targets=\"spirv\"", "Targets=\"spirv\" Defines=\"-DFIXTURE_VARIANT=1\"", StringComparison.Ordinal));
            await RebuildForAsync(Path.Combine(directory, "fixture.proj"));
            await File.AppendAllTextAsync(fake, "\n# changed fake executable identity\n");
            File.SetLastWriteTimeUtc(fake, DateTime.UtcNow.AddDays(-1));
            await RunWithEnvironmentAsync(directory, true, environment, build);
            Assert.Equal(++expected, (await File.ReadAllLinesAsync(log)).Length);
            await RunWithEnvironmentAsync(directory, true, environment, build);
            Assert.Equal(expected, (await File.ReadAllLinesAsync(log)).Length);
            if (!absoluteCompiler)
            {
                string other = Path.Combine(directory, "older fake tools");
                Directory.CreateDirectory(other);
                string replacement = Path.Combine(other, "slangc");
                await File.WriteAllTextAsync(replacement, fakeScript);
                File.SetUnixFileMode(replacement, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                File.SetLastWriteTimeUtc(replacement, DateTime.UtcNow.AddDays(-2));
                environment["PATH"] = other + Path.PathSeparator + environment["PATH"];
                await RunWithEnvironmentAsync(directory, true, environment, build);
                Assert.Equal(++expected, (await File.ReadAllLinesAsync(log)).Length);
                await RunWithEnvironmentAsync(directory, true, environment, build);
                Assert.Equal(expected, (await File.ReadAllLinesAsync(log)).Length);

                string placeholder = Path.Combine(directory, "nonexecutable-placeholder");
                Directory.CreateDirectory(placeholder);
                string unavailable = Path.Combine(placeholder, "slangc");
                await File.WriteAllTextAsync(unavailable, "not executable");
                File.SetUnixFileMode(unavailable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                environment["PATH"] = placeholder + Path.PathSeparator + environment["PATH"];
                await File.WriteAllTextAsync(Path.Combine(directory, "fixture.proj"), defaultFixture.Replace("shader-output/", "placeholder-output/", StringComparison.Ordinal));
                await RunWithEnvironmentAsync(directory, true, environment, build);
                Assert.Equal(++expected, (await File.ReadAllLinesAsync(log)).Length);
                await File.WriteAllTextAsync(Path.Combine(directory, "fixture.proj"), defaultFixture);
            }

            Assert.Equal(originalArtifact, await File.ReadAllBytesAsync(artifact));
            await RunWithEnvironmentAsync(directory, true, environment, [.. build, "-p:SlangCompilerPath=" + Path.GetRelativePath(directory, fake)]);
            Assert.Equal(++expected, (await File.ReadAllLinesAsync(log)).Length);
            await RunWithEnvironmentAsync(directory, false, environment, [.. build, "-p:SlangCompilerPath=" + Path.Combine(directory, "absent-compiler")]);
            Assert.Equal(expected, (await File.ReadAllLinesAsync(log)).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task RunAsync(string directory, params string[] arguments) => await RunAsync(directory, true, arguments);

    private static async Task<string> RunAsync(string directory, bool success, params string[] arguments)
        => await RunWithEnvironmentAsync(directory, success, null, arguments);

    private static async Task<string> RunWithEnvironmentAsync(string directory, bool success, IReadOnlyDictionary<string, string>? environment, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        start.Environment["UseSharedCompilation"] = "false";
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                start.Environment[key] = value;
            }
        }

        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        string output = await stdout + await stderr;
        Assert.True(success ? process.ExitCode == 0 : process.ExitCode != 0, string.Join(' ', arguments) + Environment.NewLine + output);
        return output;
    }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class LinuxTheoryAttribute : TheoryAttribute
    {
        public LinuxTheoryAttribute()
        {
            if (!OperatingSystem.IsLinux())
            {
                Skip = "The fake executable and effective-access fixture are Linux-only; this is not Windows runtime coverage.";
            }
        }
    }
}
