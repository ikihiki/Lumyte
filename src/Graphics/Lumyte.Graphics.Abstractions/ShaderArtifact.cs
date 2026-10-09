using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Contains compiled code and Slang reflection without owning a native module.</summary>
public sealed class ShaderArtifact
{
    private readonly byte[] _code;

    /// <summary>Initializes a new instance of the <see cref="ShaderArtifact"/> class with copied code.</summary>
    /// <param name="target">The code format.</param>
    /// <param name="stage">The compiled entry stage.</param>
    /// <param name="entryPoint">The compiled entry name.</param>
    /// <param name="code">The nonempty compiled code, copied into this artifact.</param>
    /// <param name="reflectionJson">The target-specific Slang JSON reflection.</param>
    public ShaderArtifact(ShaderTarget target, ShaderStage stage, string entryPoint, ReadOnlySpan<byte> code, string reflectionJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(reflectionJson);
        if (!Enum.IsDefined(target) || !Enum.IsDefined(stage) || code.IsEmpty)
        {
            throw new ArgumentException("Unknown shader target, stage or empty code.");
        }

        if (target == ShaderTarget.SpirV && (code.Length < 20 || code.Length % 4 != 0 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(code) != 0x07230203))
        {
            throw new ArgumentException("Invalid SPIR-V header or word length.", nameof(code));
        }

        if (target == ShaderTarget.Wgsl)
        {
            _ = new UTF8Encoding(false, true).GetString(code);
        }

        using var reflection = JsonDocument.Parse(reflectionJson);
        if (reflection.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Reflection must be a JSON object.", nameof(reflectionJson));
        }

        (Target, Stage, EntryPoint, ReflectionJson) = (target, stage, entryPoint, reflectionJson);
        _code = code.ToArray();
    }

    /// <summary>Gets the compiled code format.</summary>
    public ShaderTarget Target { get; }

    /// <summary>Gets the compiled entry stage.</summary>
    public ShaderStage Stage { get; }

    /// <summary>Gets the entry name used during Slang compilation.</summary>
    public string EntryPoint { get; }

    /// <summary>Gets the target-specific Slang reflection document.</summary>
    public string ReflectionJson { get; }

    /// <summary>Loads offline compiled code and reflection from assembly resources.</summary>
    /// <param name="assembly">The DLL containing the embedded artifacts.</param>
    /// <param name="resourcePrefix">The exact resource prefix preceding target suffixes.</param>
    /// <param name="target">The required backend target.</param>
    /// <param name="stage">The compiled entry stage.</param>
    /// <param name="entryPoint">The compiled entry name.</param>
    /// <returns>The copied, validated artifact.</returns>
    public static ShaderArtifact LoadEmbedded(Assembly assembly, string resourcePrefix, ShaderTarget target, ShaderStage stage, string entryPoint = "main")
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);
        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        string suffix = target == ShaderTarget.Wgsl ? "wgsl" : "spv";
        using Stream code = assembly.GetManifestResourceStream($"{resourcePrefix}.{suffix}") ?? throw new InvalidOperationException("Embedded shader code was not found.");
        using Stream reflection = assembly.GetManifestResourceStream($"{resourcePrefix}.{suffix}.reflection.json") ?? throw new InvalidOperationException("Embedded shader reflection was not found.");
        using var bytes = new MemoryStream();
        code.CopyTo(bytes);
        using var reader = new StreamReader(reflection, Encoding.UTF8);
        return new(target, stage, entryPoint, bytes.ToArray(), reader.ReadToEnd());
    }

    /// <summary>Copies compiled code to caller-owned storage.</summary>
    /// <returns>A new copy that does not permit changing this artifact.</returns>
    public byte[] GetCode() => (byte[])_code.Clone();
}
