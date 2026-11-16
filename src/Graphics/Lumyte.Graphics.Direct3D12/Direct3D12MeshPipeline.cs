using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe class Direct3D12MeshPipeline : IMeshPipeline
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
        ValidateNativeOutput(metadata);
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

    public MeshPipelineDesc Desc { get; }

    internal Direct3D12Device Owner { get; }

    internal bool RequiresArguments { get; }

    internal ShaderWorkgroupLimits DispatchLimits => Desc.TaskShader == null ? Owner.Caps.MeshShaderLimits!.Workgroup : Owner.Caps.TaskShaderLimits!.Workgroup;

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

    internal static void ValidateNativeOutput(JsonElement metadata)
    {
        // D3D12 rounds output arrays to 32-element blocks and permits at most
        // 32 rows of vertex/primitive attributes, including reflected system values.
        ulong vertexBytes = 0;
        ulong primitiveBytes = 0;
        uint vertexRows = 0;
        uint primitiveRows = 0;
        foreach (JsonElement output in metadata.GetProperty("outputs").EnumerateArray())
        {
            string type = output.GetProperty("type").GetString()!;
            uint components = type.StartsWith("vector:", StringComparison.Ordinal)
                ? uint.Parse(type.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture)
                : char.IsAsciiDigit(type[^1]) ? (uint)(type[^1] - '0') : 1;
            if (output.GetProperty("perPrimitive").GetBoolean())
            {
                primitiveBytes += components * 4;
                primitiveRows++;
            }
            else
            {
                vertexBytes += components * 4;
                vertexRows++;
            }
        }

        ulong vertices = (metadata.GetProperty("maxOutputVertices").GetUInt32() + 31ul) & ~31ul;
        ulong primitives = (metadata.GetProperty("maxOutputPrimitives").GetUInt32() + 31ul) & ~31ul;
        ulong outputBytes = (vertexBytes * vertices) + (primitiveBytes * primitives);
        ulong payloadBytes = metadata.GetProperty("payloadBytes").GetUInt64();
        if (vertexRows + primitiveRows > 32 || outputBytes > 32768 || payloadBytes > 48128 || outputBytes > 48128 - payloadBytes)
        {
            throw new NotSupportedException("Mesh output exceeds D3D12 rounded output storage or attribute limits.");
        }
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
