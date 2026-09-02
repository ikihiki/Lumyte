using System.Buffers;
using System.Security.Cryptography;
using System.Text;

using MessagePack;

namespace Lumyte.Graphics;

public enum GpuShaderCodeFormat : byte { SpirV = 1, Dxil = 2, Wgsl = 3 }
public enum GpuShaderStage : byte { Vertex = 1, Pixel = 2, Compute = 3, Mesh = 4 }

[MessagePackObject]
public sealed class GpuShaderPackageEnvelope
{
    [Key(0)] public uint Magic { get; set; }
    [Key(1)] public uint Version { get; set; }
    [Key(2)] public GpuShaderPackageEntryContract[] Entries { get; set; } = [];
}

[MessagePackObject]
public sealed class GpuShaderPackageEntryContract
{
    [Key(0)] public GpuShaderCodeFormat Format { get; set; }
    [Key(1)] public GpuShaderStage Stage { get; set; }
    [Key(2)] public string EntryPoint { get; set; } = string.Empty;
    [Key(3)] public string Target { get; set; } = string.Empty;
    [Key(4)] public string Profile { get; set; } = string.Empty;
    [Key(5)] public string Capability { get; set; } = string.Empty;
    [Key(6)] public byte[] AbiHash { get; set; } = [];
    [Key(7)] public byte[] PayloadHash { get; set; } = [];
    [Key(8)] public byte[] Payload { get; set; } = [];
}

public sealed class GpuShaderArtifact
{
    private readonly byte[] payload;
    private readonly byte[] abiHash;

    internal GpuShaderArtifact(GpuShaderPackageEntryContract value)
    {
        Format = value.Format;
        Stage = value.Stage;
        EntryPoint = value.EntryPoint;
        Target = value.Target;
        Profile = value.Profile;
        Capability = value.Capability;
        abiHash = value.AbiHash.ToArray();
        payload = [.. value.Payload];
    }

    public GpuShaderCodeFormat Format { get; }
    public GpuShaderStage Stage { get; }
    public string EntryPoint { get; }
    public string Target { get; }
    public string Profile { get; }
    public string Capability { get; }
    public ReadOnlyMemory<byte> AbiHash => abiHash.ToArray();
    public ReadOnlyMemory<byte> Payload => payload.ToArray();

    public GpuShaderBinary ToBinary() => new(payload, Format, Stage, EntryPoint, abiHash);
}

public sealed class GpuShaderPackage
{
    public const uint CurrentMagic = 0x5048534c; // "LSHP" in little-endian diagnostic form.
    public const uint CurrentVersion = 1;
    public const int MaximumPackageBytes = 64 * 1024 * 1024;
    public const int MaximumEntries = 1024;
    public const int MaximumStringBytes = 4096;
    public const int MaximumPayloadBytes = 32 * 1024 * 1024;
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);
    private static readonly MessagePackSerializerOptions s_options =
        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);
    private readonly IReadOnlyList<GpuShaderArtifact> entries;

    private GpuShaderPackage(GpuShaderArtifact[] entries, byte[] packageHash)
    {
        this.entries = Array.AsReadOnly(entries);
        PackageHash = packageHash;
    }

    public IReadOnlyList<GpuShaderArtifact> Entries => entries;
    public ReadOnlyMemory<byte> PackageHash { get; }

    public static GpuShaderPackage Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumPackageBytes)
        {
            throw new InvalidDataException("Shader package size is outside the supported range.");
        }

        try
        {
            Scan(bytes);
        }
        catch (Exception exception) when (exception is MessagePackSerializationException or EndOfStreamException or InvalidOperationException)
        {
            throw new InvalidDataException("Shader package is malformed or truncated.", exception);
        }
        GpuShaderPackageEnvelope envelope;
        try
        {
            envelope = MessagePackSerializer.Deserialize<GpuShaderPackageEnvelope>(bytes, s_options);
        }
        catch (Exception exception) when (exception is MessagePackSerializationException or EndOfStreamException)
        {
            throw new InvalidDataException("Shader package is malformed or truncated.", exception);
        }
        if (envelope.Magic != CurrentMagic)
        {
            throw new InvalidDataException("Shader package magic is invalid.");
        }
        if (envelope.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported shader package version {envelope.Version}.");
        }

        if (envelope.Entries is null || envelope.Entries.Length == 0 || envelope.Entries.Length > MaximumEntries)
        {
            throw new InvalidDataException("Shader package entry count is outside the supported range.");
        }

        var keys = new HashSet<EntryKey>();
        var artifacts = new GpuShaderArtifact[envelope.Entries.Length];
        for (int index = 0; index < envelope.Entries.Length; index++)
        {
            GpuShaderPackageEntryContract entry = envelope.Entries[index]
                ?? throw new InvalidDataException("Shader package contains a null entry.");
            Validate(entry);
            var key = new EntryKey(entry.Format, entry.Stage, entry.EntryPoint, entry.Target, entry.Profile, entry.Capability);
            if (!keys.Add(key))
            {
                throw new InvalidDataException("Shader package contains a duplicate entry key.");
            }

            artifacts[index] = new(entry);
        }
        return new(artifacts, SHA256.HashData(bytes.Span));
    }

    public GpuShaderArtifact Select(
        GpuShaderCodeFormat format,
        GpuShaderStage stage,
        string entryPoint,
        ReadOnlySpan<byte> expectedAbiHash = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        GpuShaderArtifact[] matches = entries.Where(entry =>
            entry.Format == format && entry.Stage == stage && entry.EntryPoint == entryPoint).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException($"Expected one {format}/{stage}/{entryPoint} artifact, found {matches.Length}.");
        }

        if (!expectedAbiHash.IsEmpty && !CryptographicOperations.FixedTimeEquals(matches[0].AbiHash.Span, expectedAbiHash))
        {
            throw new InvalidOperationException($"Shader ABI hash mismatch for {entryPoint}.");
        }

        return matches[0];
    }

    private static void Validate(GpuShaderPackageEntryContract entry)
    {
        if (!Enum.IsDefined(entry.Format) || !Enum.IsDefined(entry.Stage))
        {
            throw new InvalidDataException("Shader entry has an unknown format or stage.");
        }

        ValidateString(entry.EntryPoint, true);
        ValidateString(entry.Target, false);
        ValidateString(entry.Profile, false);
        ValidateString(entry.Capability, false);
        if (entry.AbiHash is null || entry.AbiHash.Length != SHA256.HashSizeInBytes)
        {
            throw new InvalidDataException("Shader ABI hash must be SHA-256 sized.");
        }

        if (entry.PayloadHash is null || entry.PayloadHash.Length != SHA256.HashSizeInBytes)
        {
            throw new InvalidDataException("Shader payload hash must be SHA-256 sized.");
        }

        if (entry.Payload is null || entry.Payload.Length == 0 || entry.Payload.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException("Shader payload size is outside the supported range.");
        }

        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(entry.Payload), entry.PayloadHash))
        {
            throw new InvalidDataException("Shader payload hash mismatch.");
        }

        if (entry.Format == GpuShaderCodeFormat.Wgsl)
        {
            try { _ = s_strictUtf8.GetCharCount(entry.Payload); }
            catch (DecoderFallbackException exception) { throw new InvalidDataException("WGSL payload is not valid UTF-8.", exception); }
        }
    }

    private static void ValidateString(string? value, bool required)
    {
        if (value is null || (required && string.IsNullOrWhiteSpace(value)))
        {
            throw new InvalidDataException("Shader metadata string is missing.");
        }

        if (s_strictUtf8.GetByteCount(value) > MaximumStringBytes)
        {
            throw new InvalidDataException("Shader metadata string is too large.");
        }
    }

    private static void Scan(ReadOnlyMemory<byte> bytes)
    {
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
        ScanValue(ref reader, 0);
        if (!reader.End)
        {
            throw new InvalidDataException("Shader package contains trailing data.");
        }
    }

    private static void ScanValue(ref MessagePackReader reader, int depth)
    {
        if (depth > 16)
        {
            throw new InvalidDataException("Shader package nesting is too deep.");
        }

        switch (reader.NextMessagePackType)
        {
            case MessagePackType.Nil: reader.ReadNil(); break;
            case MessagePackType.Boolean: reader.ReadBoolean(); break;
            case MessagePackType.Integer: reader.Skip(); break;
            case MessagePackType.Float: reader.Skip(); break;
            case MessagePackType.String:
                ReadOnlySequence<byte>? text = reader.ReadStringSequence();
                if (text is { Length: > MaximumStringBytes })
                {
                    throw new InvalidDataException("MessagePack string is too large.");
                }

                if (text is not null)
                {
                    try { _ = s_strictUtf8.GetCharCount(text.Value.ToArray()); }
                    catch (DecoderFallbackException exception) { throw new InvalidDataException("MessagePack string is not valid UTF-8.", exception); }
                }

                break;
            case MessagePackType.Binary:
                ReadOnlySequence<byte>? binary = reader.ReadBytes();
                if (binary is { Length: > MaximumPayloadBytes })
                {
                    throw new InvalidDataException("MessagePack binary is too large.");
                }

                break;
            case MessagePackType.Array:
                int count = reader.ReadArrayHeader();
                if (count > MaximumEntries * 10)
                {
                    throw new InvalidDataException("MessagePack collection is too large.");
                }

                for (int index = 0; index < count; index++)
                {
                    ScanValue(ref reader, depth + 1);
                }

                break;
            default:
                throw new InvalidDataException("Unsupported MessagePack token in shader package.");
        }
    }

    private readonly record struct EntryKey(
        GpuShaderCodeFormat Format,
        GpuShaderStage Stage,
        string EntryPoint,
        string Target,
        string Profile,
        string Capability);
}
