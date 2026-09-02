using System.Security.Cryptography;
using System.Text;

using Lumyte.Graphics;

using MessagePack;

namespace Lumyte.Shaders;

public sealed record GpuShaderArtifactSource(
    GpuShaderCodeFormat Format,
    GpuShaderStage Stage,
    string EntryPoint,
    string Target,
    string Profile,
    string Capability,
    ReadOnlyMemory<byte> AbiHash,
    ReadOnlyMemory<byte> Payload);

public static class GpuShaderPackageWriter
{
    private static readonly MessagePackSerializerOptions s_options = MessagePackSerializerOptions.Standard;

    public static byte[] Write(IEnumerable<GpuShaderArtifactSource> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        GpuShaderPackageEntryContract[] entries = artifacts
            .Select(Normalize)
            .OrderBy(entry => entry.Format)
            .ThenBy(entry => entry.Stage)
            .ThenBy(entry => entry.EntryPoint, StringComparer.Ordinal)
            .ThenBy(entry => entry.Target, StringComparer.Ordinal)
            .ThenBy(entry => entry.Profile, StringComparer.Ordinal)
            .ThenBy(entry => entry.Capability, StringComparer.Ordinal)
            .ToArray();
        if (entries.Length == 0)
        {
            throw new ArgumentException("At least one shader artifact is required.", nameof(artifacts));
        }

        var envelope = new GpuShaderPackageEnvelope
        {
            Magic = GpuShaderPackage.CurrentMagic,
            Version = GpuShaderPackage.CurrentVersion,
            Entries = entries,
        };
        byte[] bytes = MessagePackSerializer.Serialize(envelope, s_options);
        _ = GpuShaderPackage.Read(bytes);
        return bytes;
    }

    private static GpuShaderPackageEntryContract Normalize(GpuShaderArtifactSource source)
    {
        byte[] payload = source.Payload.ToArray();
        return new()
        {
            Format = source.Format,
            Stage = source.Stage,
            EntryPoint = Normalize(source.EntryPoint),
            Target = Normalize(source.Target),
            Profile = Normalize(source.Profile),
            Capability = Normalize(source.Capability),
            AbiHash = source.AbiHash.ToArray(),
            PayloadHash = SHA256.HashData(payload),
            Payload = payload,
        };
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Normalize(NormalizationForm.FormC);
}
