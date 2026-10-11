namespace Lumyte.Graphics.Drawing.Gltf;

/// <summary>Preserves an encoded image for an application-selected decoder.</summary>
/// <param name="Name">The optional image name.</param>
/// <param name="MimeType">The declared image content type, when present.</param>
/// <param name="Content">The owned encoded PNG or JPEG bytes; no GPU upload is performed.</param>
public sealed record GltfImage(string? Name, string? MimeType, ReadOnlyMemory<byte> Content);
