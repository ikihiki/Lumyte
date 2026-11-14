namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes a C# wire field for optional shader layout diagnostics.</summary>
public sealed record ShaderTransferField(string Path, int Offset, int Size, Type ValueType, bool IsReference = false);
