using System.Security.Cryptography;

using Lumyte.Graphics;

using MessagePack;

namespace Lumyte.Graphics.Shader.Tests;

public sealed class GpuShaderPackageTests
{
    private static readonly byte[] s_abi = SHA256.HashData("abi-v1"u8);

    [Fact]
    public void RoundtripSelectsMultipleFormatsAndStages()
    {
        byte[] bytes = GpuShaderPackageWriter.Write([
            Artifact(GpuShaderCodeFormat.Wgsl, GpuShaderStage.Vertex, "vertex", "webgpu", "wgsl", "", "@vertex fn vertex() {}"u8.ToArray()),
            Artifact(GpuShaderCodeFormat.SpirV, GpuShaderStage.Pixel, "pixel", "vulkan", "spirv1.3", "shader", [3, 2, 35, 7]),
            Artifact(GpuShaderCodeFormat.Dxil, GpuShaderStage.Pixel, "pixel", "d3d12", "sm6.8", "", [68, 88])]);

        GpuShaderPackage package = GpuShaderPackage.Read(bytes);

        GpuShaderArtifact spirv = package.Select(GpuShaderCodeFormat.SpirV, GpuShaderStage.Pixel, "pixel", s_abi);
        Assert.Equal("vulkan", spirv.Target);
        Assert.Equal([3, 2, 35, 7], spirv.Payload.ToArray());
        Assert.Equal(3, package.Entries.Count);
    }

    [Fact]
    public void WriterProducesDeterministicOrderingAndBytes()
    {
        GpuShaderArtifactSource first = Artifact(GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "z", "vulkan", "", "", [1]);
        GpuShaderArtifactSource second = Artifact(GpuShaderCodeFormat.SpirV, GpuShaderStage.Pixel, "a", "vulkan", "", "", [2]);

        byte[] forward = GpuShaderPackageWriter.Write([first, second]);
        byte[] reverse = GpuShaderPackageWriter.Write([second, first]);

        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void RuntimeAssemblyDoesNotDependOnTooling()
    {
        string[] references = typeof(GpuShaderPackage).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty).ToArray();

        Assert.DoesNotContain("Lumyte.Graphics.Shader", references);
    }

    [Fact]
    public void ReaderRejectsUnknownVersionAndFormat()
    {
        GpuShaderPackageEnvelope magic = Envelope(ArtifactContract());
        magic.Magic = 0;
        GpuShaderPackageEnvelope version = Envelope(ArtifactContract());
        version.Version = 99;
        GpuShaderPackageEnvelope format = Envelope(ArtifactContract());
        format.Entries[0].Format = (GpuShaderCodeFormat)255;

        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(Serialize(magic)));
        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(Serialize(version)));
        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(Serialize(format)));
    }

    [Fact]
    public void ReaderRejectsDuplicateEntries()
    {
        GpuShaderPackageEntryContract entry = ArtifactContract();
        GpuShaderPackageEnvelope envelope = Envelope(entry, Clone(entry));

        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(Serialize(envelope)));
    }

    [Fact]
    public void ReaderRejectsEmptyAndHashMismatchedPayloads()
    {
        GpuShaderPackageEntryContract empty = ArtifactContract();
        empty.Payload = [];
        empty.PayloadHash = SHA256.HashData([]);
        GpuShaderPackageEntryContract corrupt = ArtifactContract();
        corrupt.Payload[0] ^= 0xff;

        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(Serialize(Envelope(empty))));
        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(Serialize(Envelope(corrupt))));
    }

    [Fact]
    public void ReaderRejectsMalformedTruncatedTrailingAndOversizedData()
    {
        byte[] valid = Serialize(Envelope(ArtifactContract()));
        byte[] trailing = [.. valid, 0xc0];

        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(new byte[] { 0xc1 }));
        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(new byte[] { 0xa1, 0xff }));
        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(valid[..^1]));
        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(trailing));
        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(new byte[GpuShaderPackage.MaximumPackageBytes + 1]));
    }

    [Fact]
    public void ReaderRejectsInvalidWgslAndAbiMismatch()
    {
        GpuShaderPackageEntryContract invalidAbi = ArtifactContract();
        invalidAbi.AbiHash = [1];
        GpuShaderPackageEntryContract wgsl = ArtifactContract();
        wgsl.Format = GpuShaderCodeFormat.Wgsl;
        wgsl.Payload = [0xc3, 0x28];
        wgsl.PayloadHash = SHA256.HashData(wgsl.Payload);
        GpuShaderPackage package = GpuShaderPackage.Read(GpuShaderPackageWriter.Write([
            Artifact(GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "main", "vulkan", "", "", [1])]));

        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(Serialize(Envelope(invalidAbi))));
        Assert.Throws<InvalidDataException>(() => GpuShaderPackage.Read(Serialize(Envelope(wgsl))));
        Assert.Throws<InvalidOperationException>(() => package.Select(
            GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "main", new byte[32]));
    }

    [Fact]
    public void SelectionRejectsMissingAndAmbiguousArtifacts()
    {
        GpuShaderPackage package = GpuShaderPackage.Read(GpuShaderPackageWriter.Write([
            Artifact(GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "main", "vulkan-a", "", "", [1]),
            Artifact(GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "main", "vulkan-b", "", "", [2])]));

        Assert.Throws<InvalidOperationException>(() => package.Select(
            GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "missing"));
        Assert.Throws<InvalidOperationException>(() => package.Select(
            GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "main"));
    }

    private static GpuShaderArtifactSource Artifact(
        GpuShaderCodeFormat format, GpuShaderStage stage, string entryPoint,
        string target, string profile, string capability, byte[] payload) =>
        new(format, stage, entryPoint, target, profile, capability, s_abi, payload);

    private static GpuShaderPackageEntryContract ArtifactContract()
    {
        byte[] payload = [1, 2, 3, 4];
        return new()
        {
            Format = GpuShaderCodeFormat.SpirV,
            Stage = GpuShaderStage.Vertex,
            EntryPoint = "main",
            Target = "vulkan",
            Profile = "spirv1.3",
            AbiHash = [.. s_abi],
            Payload = payload,
            PayloadHash = SHA256.HashData(payload),
        };
    }

    private static GpuShaderPackageEnvelope Envelope(params GpuShaderPackageEntryContract[] entries) =>
        new() { Magic = GpuShaderPackage.CurrentMagic, Version = GpuShaderPackage.CurrentVersion, Entries = entries };

    private static GpuShaderPackageEntryContract Clone(GpuShaderPackageEntryContract entry) =>
        MessagePackSerializer.Deserialize<GpuShaderPackageEntryContract>(MessagePackSerializer.Serialize(entry));

    private static byte[] Serialize(GpuShaderPackageEnvelope envelope) => MessagePackSerializer.Serialize(envelope);
}
