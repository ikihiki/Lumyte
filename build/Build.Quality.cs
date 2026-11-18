using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    [Parameter("Quality/format scope: all, managed, native, or markdown.")]
    public string QualityScope { get; set; } = "all";

    private Target Quality => definition => definition.Executes(() => RunQualityAsync(fix: false));

    private Target Format => definition => definition.Executes(() => RunQualityAsync(fix: true));

    private async Task RunQualityAsync(bool fix)
    {
        if (QualityScope is not ("all" or "managed" or "native" or "markdown"))
        {
            throw new ArgumentException("Invalid --quality-scope.");
        }

        if (QualityScope is "all" or "managed")
        {
            string[] arguments = ["format", Project ?? BuildSolution, "--no-restore", "--verbosity", "diagnostic"];
            string output = await DotNetWithEnvironmentAsync(new Dictionary<string, string?> { ["Configuration"] = Configuration },
                fix ? arguments : [.. arguments, "--verify-no-changes"]).ConfigureAwait(false);
            if (output.Contains("Msbuild failed when processing the file", StringComparison.OrdinalIgnoreCase)
                || output.Contains("error while loading project", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Quality refused an incomplete MSBuild workspace; no product compilation or restore fallback is permitted.");
            }
        }

        if (QualityScope is "all" or "native")
        {
            await FormatNativeSourcesAsync(fix).ConfigureAwait(false);
        }

        if (QualityScope is "all" or "markdown")
        {
            await CheckMarkdownAsync(fix).ConfigureAwait(false);
        }
    }

    private async Task CheckMarkdownAsync(bool fix)
    {
        string lint = await FindMarkdownLintAsync().ConfigureAwait(false);
        await NodeAsync(fix ? [lint, "--fix"] : [lint]).ConfigureAwait(false);
    }

    private async Task FormatNativeSourcesAsync(bool fix)
    {
        string output = await ToolAsync("git", "ls-files", "--cached", "--others", "--exclude-standard", "-z", "--",
            "*.c", "*.cc", "*.cpp", "*.cxx", "*.h", "*.hh", "*.hpp", "*.hxx", "*.inl").ConfigureAwait(false);
        foreach (string path in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!File.Exists(Path.Combine(RootDirectory, path)))
            {
                continue;
            }

            string style = "--style=file:" + Path.Combine(RootDirectory, ".clang-format");
            await ToolAsync("clang-format", fix ? [style, "-i", "--", path] : [style, "--dry-run", "--Werror", "--", path]).ConfigureAwait(false);
        }
    }

    private async Task<string> FindMarkdownLintAsync()
    {
        string executable = (await ToolAsync("mise", "which", "markdownlint-cli2").ConfigureAwait(false)).Trim();
        var file = new FileInfo(executable);
        FileSystemInfo? target = file.ResolveLinkTarget(returnFinalTarget: true);
        if (target is not null && target.Extension == ".mjs")
        {
            return target.FullName;
        }

        if (file.Extension == ".mjs")
        {
            return file.FullName;
        }

        DirectoryInfo? directory = file.Directory;
        for (int depth = 0; directory is not null && depth < 5; depth++, directory = directory.Parent)
        {
            foreach (string prefix in new[] { "node_modules", Path.Combine("lib", "node_modules") })
            {
                string entry = Path.Combine(directory.FullName, prefix, "markdownlint-cli2", "markdownlint-cli2-bin.mjs");
                if (File.Exists(entry))
                {
                    return entry;
                }
            }
        }

        throw new FileNotFoundException("Unable to resolve the markdownlint-cli2 Node entry point. Install the repository's pinned development tools.", executable);
    }

}
