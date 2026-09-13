using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Lumyte.Graphics.Portable.Shaders.Offline;

/// <summary>Reflects the final WGSL through the official Tint frontend; never rewrites shader source.</summary>
public static class PortableShaderCompiler
{
    public const string TintRevision = "80ee0043018a51532ea0fa2e77496cc66634157e";
    public const string TintRelease = "v20260911.162847";

    public static async Task<PortableShaderBuildResult> CompileAsync(PortableShaderSource source,
        PortableShaderCompileOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (source.Language != PortableShaderSourceLanguage.Wgsl)
        {
            throw new NotSupportedException("The Portable offline compiler currently accepts complete WGSL modules. Slang root accessor and language prelude integration is not implemented; no source rewrite or root buffer fallback is performed.");
        }
        PortableGeneration.RequireNames(options);
        cancellationToken.ThrowIfCancellationRequested();
        string executable = Path.GetFullPath(options.TintInfoPath);
        if (!File.Exists(executable)) { throw new FileNotFoundException("The official tint_info executable was not found.", executable); }
        string tint = options.TintPath ?? Path.Combine(Path.GetDirectoryName(executable)!, OperatingSystem.IsWindows() ? "tint.exe" : "tint");
        tint = Path.GetFullPath(tint);
        if (!File.Exists(tint)) { throw new FileNotFoundException("The official tint executable was not found.", tint); }
        string directory = Path.Combine(Path.GetTempPath(), "Lumyte.Portable.Shaders", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Exception? primaryFailure = null;
        try
        {
            string input = Path.Combine(directory, "module.wgsl");
            await File.WriteAllTextAsync(input, source.Module, new UTF8Encoding(false, true), cancellationToken).ConfigureAwait(false);
            var json = await RunAsync(executable, ["--json", input], cancellationToken).ConfigureAwait(false);
            var layout = await RunAsync(executable, [input], cancellationToken).ConfigureAwait(false);
            var ir = await RunAsync(tint, ["--format", "wgsl", "--ir-roundtrip", "true", "--dump-ir", "true",
                "--output-name", Path.Combine(directory, "roundtrip.wgsl"), input], cancellationToken).ConfigureAwait(false);
            byte[] executableHash;
            await using (FileStream stream = File.OpenRead(executable))
            { executableHash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false); }
            byte[] tintHash;
            await using (FileStream stream = File.OpenRead(tint))
            { tintHash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false); }
            return Build(source, options, json.Output, layout.Output, ir.Output,
                Convert.ToHexStringLower(executableHash) + ":" + Convert.ToHexStringLower(tintHash),
                new[] { json.Diagnostics, layout.Diagnostics, ir.Diagnostics }.Where(message => !string.IsNullOrWhiteSpace(message)));
        }
        catch (Exception error) { primaryFailure = error; throw; }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (Exception cleanupError) when (primaryFailure is not null)
            { throw new AggregateException("Portable shader compilation and temporary file cleanup failed.", primaryFailure, cleanupError); }
        }
    }

    internal static PortableShaderBuildResult Build(PortableShaderSource source, PortableShaderCompileOptions options,
        string json, string text, string ir, string compilerHash, IEnumerable<string>? diagnostics = null)
    {
        var reflected = TintReflection.Read(json, text, ir, source.EntryPoints, options);
        string identity = string.Join('\n', "Lumyte.Portable.Shaders.Offline/1", TintRevision, compilerHash,
            source.Module, json, text, ir, options.RootTypeName,
            string.Join(',', options.ParameterTypeNames), string.Join(',', source.EntryPoints));
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var package = new PortableShaderPackage(PortableShaderPackage.CurrentVersion, source.Module,
            reflected.EntryPoints, reflected.Features, reflected.Groups, reflected.Root, reflected.Parameters,
            new PortableShaderBindingSchema(reflected.Bindings), hash);
        return new(package, PortableGeneration.Sources(package, options, reflected.HostLayouts),
            PortableGeneration.ResourceInputs(package, options), json, text, ir, diagnostics ?? []);
    }

    private static async Task<(string Output, string Diagnostics)> RunAsync(string executable, string[] arguments,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        using var process = Process.Start(start) ?? throw new IOException("Could not start official tint_info.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch { }
            throw;
        }
        string output = await stdout.ConfigureAwait(false);
        string errors = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
        { throw new InvalidDataException($"Official tint_info failed with exit code {process.ExitCode}:\n{errors}\n{output}"); }
        return (output, errors);
    }
}
