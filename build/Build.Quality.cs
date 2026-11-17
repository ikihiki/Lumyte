using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target CheckFormat => definition => definition
        .Executes(async () =>
        {
            await DotNetWithEnvironmentAsync(new Dictionary<string, string?> { ["Configuration"] = Configuration },
                "format", Project ?? BuildSolution, "--no-restore", "--verify-no-changes").ConfigureAwait(false);
        });

    private Target CheckMarkdown => definition => definition
        .Executes(async () =>
        {
            await NodeAsync(await FindMarkdownLintAsync().ConfigureAwait(false)).ConfigureAwait(false);
        });

    private Target FormatMarkdown => definition => definition
        .Executes(async () =>
        {
            await NodeAsync(await FindMarkdownLintAsync().ConfigureAwait(false), "--fix").ConfigureAwait(false);
        });

    private Target CheckNativeFormat => definition => definition
        .Executes(async () =>
        {
            await FormatNativeSourcesAsync(fix: false).ConfigureAwait(false);
        });

    private Target FormatNative => definition => definition
        .Executes(async () =>
        {
            await FormatNativeSourcesAsync(fix: true).ConfigureAwait(false);
        });

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
