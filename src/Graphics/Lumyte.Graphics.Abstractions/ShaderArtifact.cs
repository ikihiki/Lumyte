using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns an opaque shader binary containing target code and compilation metadata.</summary>
public sealed class ShaderArtifact
{
    private const uint Magic = 0x4448534C;
    private readonly byte[] _binary;

    /// <summary>Initializes a new instance of the <see cref="ShaderArtifact"/> class.</summary>
    /// <param name="binary">The complete versioned shader binary, copied and validated.</param>
    public ShaderArtifact(ReadOnlySpan<byte> binary)
    {
        _binary = binary.ToArray();
        using var stream = new MemoryStream(_binary, false);
        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
        try
        {
            if (reader.ReadUInt32() != Magic || reader.ReadUInt32() != 1)
            {
                throw new ArgumentException("Unknown shader binary format.", nameof(binary));
            }

            var stage = (ShaderStage)reader.ReadUInt32();
            string entry = reader.ReadString();
            string compiler = reader.ReadString();
            string layout = reader.ReadString();
            if (!Enum.IsDefined(stage) || string.IsNullOrWhiteSpace(entry) || string.IsNullOrWhiteSpace(compiler) || layout != "row-major")
            {
                throw new ArgumentException("Invalid compilation metadata.", nameof(binary));
            }

            uint count = reader.ReadUInt32();
            var targets = new HashSet<ShaderTarget>();
            if (count == 0 || count > (uint)Enum.GetValues<ShaderTarget>().Length)
            {
                throw new ArgumentException("Invalid target count.", nameof(binary));
            }

            for (uint i = 0; i < count; i++)
            {
                var target = (ShaderTarget)reader.ReadUInt32();
                if (!Enum.IsDefined(target) || !targets.Add(target))
                {
                    throw new ArgumentException("Unknown or duplicate target.", nameof(binary));
                }

                byte[] code = ReadCode(reader);
                string reflectionJson = reader.ReadString();
                if (target == ShaderTarget.SpirV && (code.Length < 20 || code.Length % 4 != 0 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(code) != 0x07230203))
                {
                    throw new ArgumentException("Invalid SPIR-V code.", nameof(binary));
                }

                if (target == ShaderTarget.Wgsl)
                {
                    _ = new UTF8Encoding(false, true).GetString(code);
                }

                using var reflection = JsonDocument.Parse(reflectionJson);
                if (reflection.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException("Invalid reflection.", nameof(binary));
                }
            }

            if (stream.Position != stream.Length)
            {
                throw new ArgumentException("Trailing shader binary data.", nameof(binary));
            }
        }
        catch (EndOfStreamException error)
        {
            throw new ArgumentException("Truncated shader binary.", nameof(binary), error);
        }
    }

    /// <summary>Loads the complete shader binary from a DLL resource.</summary>
    /// <param name="assembly">The DLL containing the binary.</param>
    /// <param name="resourceName">The exact resource name.</param>
    /// <returns>The validated opaque artifact.</returns>
    public static ShaderArtifact LoadEmbedded(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        using Stream resource = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException("Embedded shader binary was not found.");
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        return new(bytes.ToArray());
    }

    /// <summary>Packs one online target into the same binary format used offline.</summary>
    /// <param name="target">The compiler target.</param>
    /// <param name="stage">The compiled stage.</param>
    /// <param name="entryPoint">The compiled entry.</param>
    /// <param name="compilerVersion">The actual compiler version output.</param>
    /// <param name="code">The generated code.</param>
    /// <param name="reflectionJson">The corresponding reflection.</param>
    /// <returns>The validated single-target binary.</returns>
    public static ShaderArtifact PackTarget(ShaderTarget target, ShaderStage stage, string entryPoint, string compilerVersion, ReadOnlySpan<byte> code, string reflectionJson)
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
        {
            writer.Write(Magic);
            writer.Write(1u);
            writer.Write((uint)stage);
            writer.Write(entryPoint);
            writer.Write(compilerVersion);
            writer.Write("row-major");
            writer.Write(1u);
            writer.Write((uint)target);
            writer.Write(code.Length);
            writer.Write(code);
            writer.Write(reflectionJson);
        }

        return new(bytes.ToArray());
    }

    /// <summary>Extracts a backend's code and metadata without caller-supplied entry or stage.</summary>
    /// <param name="target">The backend's required target.</param>
    /// <returns>The matching compilation data.</returns>
    public ShaderTargetData GetTarget(ShaderTarget target)
    {
        using var stream = new MemoryStream(_binary, false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        reader.ReadUInt32();
        reader.ReadUInt32();
        var stage = (ShaderStage)reader.ReadUInt32();
        string entry = reader.ReadString();
        string compiler = reader.ReadString();
        string layout = reader.ReadString();
        uint count = reader.ReadUInt32();
        for (uint i = 0; i < count; i++)
        {
            var current = (ShaderTarget)reader.ReadUInt32();
            byte[] code = ReadCode(reader);
            string reflection = reader.ReadString();
            if (current == target)
            {
                return new(target, stage, entry, compiler, layout, code, reflection);
            }
        }

        throw new NotSupportedException("The shader binary does not contain this backend's target.");
    }

    /// <summary>Copies the complete opaque binary for storage or transport.</summary>
    /// <returns>A caller-owned copy.</returns>
    public byte[] GetBinary() => (byte[])_binary.Clone();

    private static byte[] ReadCode(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length <= 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new ArgumentException("Invalid shader code length.");
        }

        return reader.ReadBytes(length);
    }
}
