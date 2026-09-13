using System.Text;
using System.Text.Json;
using Lumyte.Graphics.Shaders.Offline;
using Xunit;

namespace Lumyte.Graphics.Native.Shaders.Offline.Tests.Build;

public sealed class ShaderBuildOutputsTests
{
    [Fact]
    public async Task PublishesOnlyCurrentSourcesAndSchemasAsAbsoluteBuildInputs()
    {
        using var directory = new OutputDirectory();
        Dictionary<string, byte[]> outputs = new()
        {
            ["program/package.g.cs"] = Bytes("public class Package {}"),
            ["program/root.native.resources.xml"] = Bytes("<input />"),
            ["program/compute.dxil"] = [1, 2, 3],
        };

        await ShaderBuildOutputs.PublishAsync(directory.Path, outputs, CancellationToken.None);

        Assert.Equal([directory.File("program/package.g.cs")], await File.ReadAllLinesAsync(directory.File("generated-sources.txt")));
        Assert.Equal([directory.File("program/root.native.resources.xml")], await File.ReadAllLinesAsync(directory.File("generated-resources.txt")));
        Assert.Equal(outputs.Keys.Order(StringComparer.Ordinal),
            JsonSerializer.Deserialize<string[]>(await File.ReadAllBytesAsync(directory.File("generated-files.json"))));
    }

    [Fact]
    public async Task RemovesOldGroupsWithoutDeletingUnrelatedFiles()
    {
        using var directory = new OutputDirectory();
        await ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { ["program/group0.portable.resources.xml"] = Bytes("old group") }, CancellationToken.None);
        string unrelated = directory.File("program/notes.txt");
        await File.WriteAllTextAsync(unrelated, "caller owned");

        await ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { ["program/group1.portable.resources.xml"] = Bytes("new group") }, CancellationToken.None);

        Assert.False(File.Exists(directory.File("program/group0.portable.resources.xml")));
        Assert.Equal("caller owned", await File.ReadAllTextAsync(unrelated));
        Assert.Equal([directory.File("program/group1.portable.resources.xml")],
            await File.ReadAllLinesAsync(directory.File("generated-resources.txt")));
    }

    [Fact]
    public async Task IdenticalOutputsKeepTheirRecordedWriteTimes()
    {
        using var directory = new OutputDirectory();
        Dictionary<string, byte[]> outputs = new() { ["program/package.g.cs"] = Bytes("unchanged") };
        await ShaderBuildOutputs.PublishAsync(directory.Path, outputs, CancellationToken.None);
        string[] paths = [directory.File("program/package.g.cs"), directory.File("generated-files.json"),
            directory.File("generated-sources.txt"), directory.File("generated-resources.txt")];
        DateTime fixedTime = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        foreach (string path in paths) { File.SetLastWriteTimeUtc(path, fixedTime); }

        await ShaderBuildOutputs.PublishAsync(directory.Path, outputs, CancellationToken.None);

        Assert.All(paths, path => Assert.True(File.GetLastWriteTimeUtc(path) == fixedTime,
            "An unchanged generated file was rewritten: " + path));
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("/absolute.cs")]
    [InlineData("C:\\outside.cs")]
    [InlineData("program/nested/file.cs")]
    [InlineData("program/../file.cs")]
    [InlineData("program/generated-files.json")]
    [InlineData("program/GENERATED-SOURCES.TXT")]
    [InlineData("program/generated-resources.txt")]
    [InlineData("program/line\nbreak.cs")]
    [InlineData("program/trailing.cs.")]
    public async Task UnsafeOutputNamesAreRejectedBeforePublication(string name)
    {
        using var directory = new OutputDirectory();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { [name] = [1] }, CancellationToken.None));

        Assert.Equal("outputs", error.ParamName);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task InvalidInventoryPreservesThePublishedFiles()
    {
        using var directory = new OutputDirectory();
        await ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { ["program/package.g.cs"] = Bytes("original") }, CancellationToken.None);
        await File.WriteAllTextAsync(directory.File("generated-files.json"), "[\"../outside.cs\"]");

        await Assert.ThrowsAsync<InvalidDataException>(() => ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { ["program/package.g.cs"] = Bytes("replacement") }, CancellationToken.None));

        Assert.Equal("original", await File.ReadAllTextAsync(directory.File("program/package.g.cs")));
    }

    [Fact]
    public async Task UnownedFileCollisionsArePreserved()
    {
        using var directory = new OutputDirectory();
        Directory.CreateDirectory(directory.File("program"));
        await File.WriteAllTextAsync(directory.File("program/package.g.cs"), "caller owned");

        await Assert.ThrowsAsync<IOException>(() => ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { ["program/package.g.cs"] = Bytes("generated") }, CancellationToken.None));

        Assert.Equal("caller owned", await File.ReadAllTextAsync(directory.File("program/package.g.cs")));
        Assert.False(File.Exists(directory.File("generated-files.json")));
    }

    [Fact]
    public async Task MissingInventoryDoesNotAdoptExistingOutputs()
    {
        using var directory = new OutputDirectory();
        await ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { ["program/package.g.cs"] = Bytes("original") }, CancellationToken.None);
        File.Delete(directory.File("generated-files.json"));

        await Assert.ThrowsAsync<IOException>(() => ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { ["program/package.g.cs"] = Bytes("replacement") }, CancellationToken.None));

        Assert.Equal("original", await File.ReadAllTextAsync(directory.File("program/package.g.cs")));
    }

    [Fact]
    public async Task PreparationFailureLeavesThePreviousGenerationAvailable()
    {
        using var directory = new OutputDirectory();
        await ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { ["program/package.g.cs"] = Bytes("original") }, CancellationToken.None);
        Directory.CreateDirectory(directory.File("blocked/failure.cs"));

        await Assert.ThrowsAsync<IOException>(() => ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]>
            {
                ["program/package.g.cs"] = Bytes("replacement"),
                ["blocked/failure.cs"] = Bytes("cannot publish"),
            }, CancellationToken.None));

        Assert.Equal("original", await File.ReadAllTextAsync(directory.File("program/package.g.cs")));
        Assert.Equal([directory.File("program/package.g.cs")], await File.ReadAllLinesAsync(directory.File("generated-sources.txt")));
    }

    [Fact]
    public async Task EmptyBuildRemovesItsPreviousOutputInventory()
    {
        using var directory = new OutputDirectory();
        await ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { ["program/package.g.cs"] = Bytes("old program") }, CancellationToken.None);

        await ShaderBuildOutputs.PublishAsync(directory.Path, new Dictionary<string, byte[]>(), CancellationToken.None);

        Assert.False(File.Exists(directory.File("program/package.g.cs")));
        Assert.Empty(await File.ReadAllLinesAsync(directory.File("generated-sources.txt")));
        Assert.Empty(await File.ReadAllLinesAsync(directory.File("generated-resources.txt")));
        Assert.Empty(JsonSerializer.Deserialize<string[]>(await File.ReadAllBytesAsync(directory.File("generated-files.json")))!);
    }

    [Fact]
    public async Task CancelledPublicationPreservesThePreviousGeneration()
    {
        using var directory = new OutputDirectory();
        await ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { ["program/package.g.cs"] = Bytes("original") }, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ShaderBuildOutputs.PublishAsync(directory.Path,
            new Dictionary<string, byte[]> { ["program/package.g.cs"] = Bytes("replacement") }, cancellation.Token));

        Assert.Equal("original", await File.ReadAllTextAsync(directory.File("program/package.g.cs")));
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private sealed class OutputDirectory : IDisposable
    {
        internal OutputDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Lumyte.ShaderOutputs." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }
        internal string File(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relative));
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
