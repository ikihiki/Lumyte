namespace Lumyte.Graphics.Portable.Shaders.Offline.Tests;

internal sealed class TintFactAttribute : FactAttribute
{
    public TintFactAttribute()
    {
        if (TintTools.Find() is null) { Skip = "Official Tint v20260911.162847 is unavailable. Set LUMYTE_TINT_INFO to its tint_info executable."; }
    }
}

internal static class TintTools
{
    internal static string Root
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            { if (File.Exists(Path.Combine(directory.FullName, "Lumyte.slnx"))) { return directory.FullName; } }
            throw new DirectoryNotFoundException("Repository root was not found.");
        }
    }

    internal static string? Find()
    {
        string? configured = Environment.GetEnvironmentVariable("LUMYTE_TINT_INFO");
        if (configured is not null) { return File.Exists(configured) ? configured : null; }
        string expected = Path.Combine(Root, "artifacts", "experiments", "portable-wgsl-frontend",
            "Dawn-80ee0043018a51532ea0fa2e77496cc66634157e-windows-latest-Release", "bin", "tint_info.exe");
        return File.Exists(expected) ? expected : null;
    }

    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(Root, "tools",
        "Lumyte.Graphics.Portable.Shaders.Offline.Tests", "Fixtures", name));

    internal static PortableShaderCompileOptions Options(string? root = null, string[]? parameters = null) =>
        new(Find() ?? throw new FileNotFoundException("Official tint_info was not found."), "Generated.Test", "Example", root, parameters);
}
