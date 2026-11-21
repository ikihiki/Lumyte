using System.Buffers.Binary;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe class Direct3D12MeshPipeline : IMeshPipeline, IGraphicsNativeOrigin
{
    private readonly Direct3D12Shader _mesh;
    private readonly Direct3D12Shader? _task;
    private readonly Direct3D12Shader? _fragment;
    private readonly Dictionary<string, nint> _variants = [];
    private readonly PrimitiveTopology _topology;
    private bool _disposed;

    internal Direct3D12MeshPipeline(Direct3D12Device owner, MeshPipelineDesc desc)
    {
        (Owner, Desc) = (owner, desc);
        _mesh = Borrow(desc.MeshShader, owner);
        _task = desc.TaskShader == null ? null : Borrow(desc.TaskShader, owner);
        _fragment = desc.FragmentShader == null ? null : Borrow(desc.FragmentShader, owner);
        ShaderTargetData mesh = PipelineValidation.Shader(_mesh, ShaderTarget.Dxil, ShaderStage.Mesh, owner.Caps);
        ShaderTargetData? task = _task == null ? null : PipelineValidation.Shader(_task, ShaderTarget.Dxil, ShaderStage.Task, owner.Caps);
        ShaderTargetData? fragment = _fragment == null ? null : PipelineValidation.Shader(_fragment, ShaderTarget.Dxil, ShaderStage.Fragment, owner.Caps);
        MeshShaderValidation.Validate(mesh, owner.Caps);
        if (task != null)
        {
            MeshShaderValidation.Validate(task, owner.Caps);
        }

        MeshShaderValidation.Program(mesh, task, fragment);
        if (!Enum.IsDefined(desc.Optimization) || (desc.AlphaToCoverageEnable && fragment == null))
        {
            throw new ArgumentException("Invalid mesh compilation options.");
        }

        JsonElement metadata = MeshShaderValidation.Read(mesh);
        ValidateNativeOutput(metadata, mesh.Code);
        _topology = metadata.GetProperty("outputTopology").GetString() switch
        {
            "line" => PrimitiveTopology.LineList,
            "triangle" => PrimitiveTopology.TriangleList,
            _ => throw new NotSupportedException("Only line and triangle mesh output is supported."),
        };
        foreach (ShaderTargetData data in new[] { mesh, task, fragment }.OfType<ShaderTargetData>())
        {
            PipelineValidation.RootCapacity(data);
            RequiresArguments |= ShaderDataLayout.HasRoot(data);
        }
    }

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => this;

    public MeshPipelineDesc Desc { get; }

    internal Direct3D12Device Owner { get; }

    internal bool RequiresArguments { get; }

    internal ShaderWorkgroupLimits DispatchLimits => Desc.TaskShader == null ? Owner.Caps.MeshShaderLimits!.Workgroup : Owner.Caps.TaskShaderLimits!.Workgroup;

    void IGraphicsNativeOrigin.ValidateOrigin()
    {
        ObjectDisposedException.ThrowIf(Owner.Status == GraphicsDeviceStatus.Disposed, Owner);
        if (Owner.Status == GraphicsDeviceStatus.Lost)
        {
            throw new GraphicsDeviceLostException(Owner.Loss!);
        }

        ValidateAlive();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (nint variant in _variants.Values)
        {
            NativeMethods.PipelineDestroy(variant);
        }

        _variants.Clear();
        _disposed = true;
    }

    internal static void ValidateNativeOutput(JsonElement metadata, ReadOnlySpan<byte> dxil)
    {
        ValidateNativeOutput(metadata);
        if (ReadOutputSignatureRows(dxil) > 32)
        {
            throw new NotSupportedException("Mesh output exceeds D3D12 packed signature row limits.");
        }
    }

    internal static void ValidateNativeOutput(JsonElement metadata)
    {
        // Storage remains component-based; declaration count is not signature row count.
        // D3D12 rounds output arrays to 32-element blocks.
        ulong vertexBytes = 0;
        ulong primitiveBytes = 0;
        foreach (JsonElement output in metadata.GetProperty("outputs").EnumerateArray())
        {
            string type = output.GetProperty("type").GetString()!;
            uint components = type.StartsWith("vector:", StringComparison.Ordinal)
                ? uint.Parse(type.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture)
                : char.IsAsciiDigit(type[^1]) ? (uint)(type[^1] - '0') : 1;
            if (output.GetProperty("perPrimitive").GetBoolean())
            {
                primitiveBytes += components * 4;
            }
            else
            {
                vertexBytes += components * 4;
            }
        }

        ulong vertices = (metadata.GetProperty("maxOutputVertices").GetUInt32() + 31ul) & ~31ul;
        ulong primitives = (metadata.GetProperty("maxOutputPrimitives").GetUInt32() + 31ul) & ~31ul;
        ulong outputBytes = (vertexBytes * vertices) + (primitiveBytes * primitives);
        ulong payloadBytes = metadata.GetProperty("payloadBytes").GetUInt64();
        if (outputBytes > 32768 || payloadBytes > 48128 || outputBytes > 48128 - payloadBytes)
        {
            throw new NotSupportedException("Mesh output exceeds D3D12 rounded output storage or attribute limits.");
        }
    }

    internal static ulong ReadOutputSignatureRows(ReadOnlySpan<byte> dxil)
    {
        // DXC DxilContainer.h: OSG1 is the vertex signature; PSG1 is the mesh
        // primitive signature (the same container part used for hull patch constants).
        if (dxil.Length < 32 || !dxil[..4].SequenceEqual("DXBC"u8) ||
            ReadUInt32(dxil, 20) != 1 || ReadUInt32(dxil, 24) != dxil.Length)
        {
            throw new ArgumentException("A complete DXIL container is required.", nameof(dxil));
        }

        uint count = ReadUInt32(dxil, 28);
        if (count > (dxil.Length - 32) / 4)
        {
            throw new ArgumentException("Invalid DXIL part table.", nameof(dxil));
        }

        ulong rows = 0;
        bool vertexFound = false;
        bool primitiveFound = false;
        for (int i = 0; i < count; i++)
        {
            uint offset = ReadUInt32(dxil, 32 + (i * 4));
            if (offset < 32ul + (count * 4ul) || offset > dxil.Length - 8 || (offset & 3) != 0)
            {
                throw new ArgumentException("Invalid DXIL part offset.", nameof(dxil));
            }

            ReadOnlySpan<byte> part = dxil[(int)offset..];
            uint size = ReadUInt32(part, 4);
            if (size > part.Length - 8)
            {
                throw new ArgumentException("Truncated DXIL part.", nameof(dxil));
            }

            bool vertex = part[..4].SequenceEqual("OSG1"u8);
            bool primitive = part[..4].SequenceEqual("PSG1"u8);
            if (!vertex && !primitive)
            {
                continue;
            }

            if ((vertex && vertexFound) || (primitive && primitiveFound))
            {
                throw new ArgumentException("Duplicate DXIL output signature.", nameof(dxil));
            }

            vertexFound |= vertex;
            primitiveFound |= primitive;
            rows += ReadSignatureRows(part.Slice(8, (int)size));
        }

        if (!vertexFound)
        {
            throw new ArgumentException("DXIL vertex output signature is missing.", nameof(dxil));
        }

        return rows;
    }

    internal void ValidateAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = _mesh.Native;
        if (_task != null)
        {
            _ = _task.Native;
        }

        if (_fragment != null)
        {
            _ = _fragment.Native;
        }
    }

    internal nint Resolve(RenderStateSnapshot snapshot, TextureFormat[] formats, TextureFormat? depthFormat)
    {
        ValidateAlive();
        GraphicsRenderStateDesc state = snapshot.Desc;
        if (state.Topology != _topology || state.StripIndexFormat != null)
        {
            throw new ArgumentException("Mesh render state must match the output list topology without strip restart.");
        }

        if (Desc.AlphaToCoverageEnable)
        {
            throw new NotSupportedException("Alpha-to-coverage requires multisampled attachments.");
        }

        if (formats.Length != state.ColorTargets.Count ||
            ((state.DepthStencil.DepthTestEnable || state.DepthStencil.DepthWriteEnable || state.DepthStencil.StencilTestEnable) && depthFormat == null) ||
            (state.DepthStencil.StencilTestEnable && depthFormat != TextureFormat.Depth24Stencil8))
        {
            throw new ArgumentException("Render state does not match the pass attachments.");
        }

        string key = "mesh:" + _topology + ":" + snapshot.Key + ":" + depthFormat + ":" + string.Join(',', formats);
        if (_variants.TryGetValue(key, out nint cached))
        {
            return cached;
        }

        uint[] words = Direct3D12GraphicsPipeline.Encode(state, formats, depthFormat, _topology == PrimitiveTopology.LineList ? PrimitiveTopologyClass.Line : PrimitiveTopologyClass.Triangle);
        byte[] ms = _mesh.Native;
        byte[] ts = _task?.Native ?? [];
        byte[] ps = _fragment?.Native ?? [];
        fixed (byte* m = ms, t = ts, p = ps)
        {
            fixed (uint* s = words)
            {
                Owner.Check(NativeMethods.MeshCreate(Owner.Native, m, (ulong)ms.Length, t, (ulong)ts.Length, p, (ulong)ps.Length, s, out nint pipeline));
                try
                {
                    _variants.Add(key, pipeline);
                }
                catch
                {
                    NativeMethods.PipelineDestroy(pipeline);
                    throw;
                }

                return pipeline;
            }
        }
    }

    private static uint ReadSignatureRows(ReadOnlySpan<byte> signature)
    {
        if (signature.Length < 8)
        {
            throw new ArgumentException("Truncated DXIL output signature.");
        }

        uint count = ReadUInt32(signature, 0);
        uint offset = ReadUInt32(signature, 4);
        if (offset < 8 || offset > signature.Length || (offset & 3) != 0 || count > (signature.Length - offset) / 32)
        {
            throw new ArgumentException("Invalid DXIL signature element table.");
        }

        uint rows = 0;
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> element = signature.Slice((int)offset + (i * 32), 32);
            uint register = ReadUInt32(element, 20);

            // SV_CullPrimitive and other unallocated system values use UINT_MAX.
            if (register == uint.MaxValue)
            {
                continue;
            }

            if (ReadUInt32(element, 0) != 0 || register >= 32 || element[24] == 0 || (element[24] & 0xf0) != 0)
            {
                throw new ArgumentException("Invalid mesh signature register allocation.");
            }

            rows = Math.Max(rows, register + 1);
        }

        return rows;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));

    private static Direct3D12Shader Borrow(IGraphicsShader shader, Direct3D12Device owner)
    {
        ArgumentNullException.ThrowIfNull(shader);
        if (shader is not Direct3D12Shader native || !ReferenceEquals(native.Owner, owner))
        {
            throw new ArgumentException("Mesh shader belongs to another device.");
        }

        _ = native.Native;
        return native;
    }
}
