// Included after the device/command definitions; the C ABI uses fixed-width values only.
struct Pipeline
{
    ComPtr<ID3D12RootSignature> root;
    ComPtr<ID3D12PipelineState> state;
    ComPtr<ID3D12StateObject> executable;
    D3D12_PROGRAM_IDENTIFIER identifier{};
};
struct PartialProgram
{
    ComPtr<ID3D12RootSignature> root;
    ComPtr<ID3D12StateObject> collection;
    UINT optimization = 0;
};
static HRESULT create_root(Device* device, ID3D12RootSignature** output) noexcept
{
    D3D12_FEATURE_DATA_D3D12_OPTIONS options{};
    CHECK(device->device->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS, &options, sizeof(options)));
    D3D12_FEATURE_DATA_SHADER_MODEL shader{D3D_SHADER_MODEL_6_6};
    CHECK(device->device->CheckFeatureSupport(D3D12_FEATURE_SHADER_MODEL, &shader, sizeof(shader)));
    if (options.ResourceBindingTier < D3D12_RESOURCE_BINDING_TIER_2 || shader.HighestShaderModel < D3D_SHADER_MODEL_6_6)
    {
        return DXGI_ERROR_UNSUPPORTED;
    }
    D3D12_DESCRIPTOR_RANGE ranges[3]{};
    ranges[0] = {D3D12_DESCRIPTOR_RANGE_TYPE_SRV, 128, 0, 0, 0};
    ranges[1] = {D3D12_DESCRIPTOR_RANGE_TYPE_SRV, 64, 0, 1, 128};
    ranges[2] = {D3D12_DESCRIPTOR_RANGE_TYPE_UAV, 64, 0, 1, 192};
    D3D12_DESCRIPTOR_RANGE samplers{D3D12_DESCRIPTOR_RANGE_TYPE_SAMPLER, 16, 0, 0, 0};
    D3D12_ROOT_PARAMETER parameters[5]{};
    parameters[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
    parameters[0].Descriptor = {8, 0};
    parameters[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    parameters[1].DescriptorTable = {3, ranges};
    parameters[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    parameters[2].DescriptorTable = {1, &samplers};
    parameters[3].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
    parameters[3].Constants = {9, 0, 2};
    parameters[3].ShaderVisibility = D3D12_SHADER_VISIBILITY_VERTEX;
    parameters[4].ParameterType = D3D12_ROOT_PARAMETER_TYPE_SRV;
    parameters[4].Descriptor = {0, 2};
    parameters[4].ShaderVisibility = D3D12_SHADER_VISIBILITY_VERTEX;
    D3D12_ROOT_SIGNATURE_DESC desc{5, parameters, 0, nullptr,
                                   D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT};
    ComPtr<ID3DBlob> blob, errors;
    CHECK(D3D12SerializeRootSignature(&desc, D3D_ROOT_SIGNATURE_VERSION_1, &blob, &errors));
    return device->device->CreateRootSignature(0, blob->GetBufferPointer(), blob->GetBufferSize(),
                                               IID_PPV_ARGS(output));
}
#include "partial_programs.inl"

API HRESULT compute_create(Device* device, const void* code, UINT64 size, Pipeline** output) noexcept
{
    *output = nullptr;
    auto pipeline = std::unique_ptr<Pipeline>(new (std::nothrow) Pipeline());
    if (!pipeline)
    {
        return E_OUTOFMEMORY;
    }
    CHECK(report_failure(device, create_root(device, &pipeline->root)));
    D3D12_COMPUTE_PIPELINE_STATE_DESC desc{};
    desc.pRootSignature = pipeline->root.Get();
    desc.CS = {code, static_cast<SIZE_T>(size)};
    CHECK(device->device->CreateComputePipelineState(&desc, IID_PPV_ARGS(&pipeline->state)));
    *output = pipeline.release();
    return S_OK;
}
API void pipeline_destroy(Pipeline* pipeline) noexcept
{
    delete pipeline;
}

struct NativeDrawState
{
    UINT topology, strip, cull, ccw, clip;
    INT bias;
    FLOAT slope, clamp;
    UINT depth_test, depth_write, depth_compare, stencil, read_mask, write_mask;
    UINT front[4], back[4];
    UINT mask, count, depth_format;
    UINT formats[8];
    UINT colors[8][8];
};
static_assert(sizeof(NativeDrawState) == 97 * sizeof(UINT), "Managed draw-state ABI must match.");
API HRESULT graphics_create(Device* device, const void* vs, UINT64 vs_size, const void* ps, UINT64 ps_size,
                            const NativeDrawState* state, PartialProgram* partial, Pipeline** output) noexcept
{
    *output = nullptr;
    auto pipeline = std::unique_ptr<Pipeline>(new (std::nothrow) Pipeline());
    if (!pipeline)
    {
        return E_OUTOFMEMORY;
    }
    if (partial)
    {
        pipeline->root = partial->root;
    }
    else
    {
        CHECK(report_failure(device, create_root(device, &pipeline->root)));
    }
    D3D12_GRAPHICS_PIPELINE_STATE_DESC desc{};
    desc.pRootSignature = pipeline->root.Get();
    desc.VS = {vs, static_cast<SIZE_T>(vs_size)};
    desc.PS = {ps, static_cast<SIZE_T>(ps_size)};
    desc.PrimitiveTopologyType = static_cast<D3D12_PRIMITIVE_TOPOLOGY_TYPE>(state->topology);
    desc.IBStripCutValue = static_cast<D3D12_INDEX_BUFFER_STRIP_CUT_VALUE>(state->strip);
    desc.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
    desc.RasterizerState.CullMode = static_cast<D3D12_CULL_MODE>(state->cull);
    desc.RasterizerState.FrontCounterClockwise = state->ccw;
    desc.RasterizerState.DepthClipEnable = state->clip;
    desc.RasterizerState.DepthBias = state->bias;
    desc.RasterizerState.SlopeScaledDepthBias = state->slope;
    desc.RasterizerState.DepthBiasClamp = state->clamp;
    desc.DepthStencilState.DepthEnable = state->depth_test || state->depth_write;
    desc.DepthStencilState.DepthWriteMask =
        state->depth_write ? D3D12_DEPTH_WRITE_MASK_ALL : D3D12_DEPTH_WRITE_MASK_ZERO;
    desc.DepthStencilState.DepthFunc =
        state->depth_test ? static_cast<D3D12_COMPARISON_FUNC>(state->depth_compare) : D3D12_COMPARISON_FUNC_ALWAYS;
    desc.DepthStencilState.StencilEnable = state->stencil;
    desc.DepthStencilState.StencilReadMask = static_cast<UINT8>(state->read_mask);
    desc.DepthStencilState.StencilWriteMask = static_cast<UINT8>(state->write_mask);
    auto face = [](const UINT* v)
    {
        return D3D12_DEPTH_STENCILOP_DESC{static_cast<D3D12_STENCIL_OP>(v[0]), static_cast<D3D12_STENCIL_OP>(v[1]),
                                          static_cast<D3D12_STENCIL_OP>(v[2]),
                                          static_cast<D3D12_COMPARISON_FUNC>(v[3])};
    };
    desc.DepthStencilState.FrontFace = face(state->front);
    desc.DepthStencilState.BackFace = face(state->back);
    desc.SampleMask = state->mask;
    desc.SampleDesc.Count = 1;
    desc.NumRenderTargets = state->count;
    desc.DSVFormat = static_cast<DXGI_FORMAT>(state->depth_format);
    desc.BlendState.IndependentBlendEnable = TRUE;
    for (UINT i = 0; i < state->count; i++)
    {
        desc.RTVFormats[i] = static_cast<DXGI_FORMAT>(state->formats[i]);
        auto& b = desc.BlendState.RenderTarget[i];
        const auto* c = state->colors[i];
        b.BlendEnable = c[0];
        b.SrcBlend = static_cast<D3D12_BLEND>(c[1]);
        b.DestBlend = static_cast<D3D12_BLEND>(c[2]);
        b.BlendOp = static_cast<D3D12_BLEND_OP>(c[3]);
        b.SrcBlendAlpha = static_cast<D3D12_BLEND>(c[4]);
        b.DestBlendAlpha = static_cast<D3D12_BLEND>(c[5]);
        b.BlendOpAlpha = static_cast<D3D12_BLEND_OP>(c[6]);
        b.RenderTargetWriteMask = static_cast<UINT8>(c[7]);
    }
    if (partial)
    {
        CHECK(link_partial(device, partial, desc, pipeline.get()));
    }
    else
    {
        CHECK(
            report_failure(device, device->device->CreateGraphicsPipelineState(&desc, IID_PPV_ARGS(&pipeline->state))));
    }
    *output = pipeline.release();
    return S_OK;
}
API HRESULT commands_pipeline(Commands* commands, Pipeline* pipeline, int compute) noexcept
{
    if (pipeline->executable)
    {
        ComPtr<ID3D12GraphicsCommandList10> list;
        CHECK(commands->list.As(&list));
        D3D12_SET_PROGRAM_DESC desc{};
        desc.Type = D3D12_PROGRAM_TYPE_GENERIC_PIPELINE;
        desc.GenericPipeline.ProgramIdentifier = pipeline->identifier;
        list->SetProgram(&desc);
    }
    else
    {
        commands->list->SetPipelineState(pipeline->state.Get());
    }
    if (compute)
    {
        commands->list->SetComputeRootSignature(pipeline->root.Get());
    }
    else
    {
        commands->list->SetGraphicsRootSignature(pipeline->root.Get());
    }
    return S_OK;
}
API void commands_viewport(Commands* commands, FLOAT x, FLOAT y, FLOAT width, FLOAT height, FLOAT min,
                           FLOAT max) noexcept
{
    D3D12_VIEWPORT v{x, y, width, height, min, max};
    commands->list->RSSetViewports(1, &v);
}
API void commands_scissor(Commands* commands, LONG x, LONG y, LONG width, LONG height) noexcept
{
    D3D12_RECT r{x, y, x + width, y + height};
    commands->list->RSSetScissorRects(1, &r);
}
API void commands_blend(Commands* commands, FLOAT r, FLOAT g, FLOAT b, FLOAT a) noexcept
{
    const FLOAT v[]{r, g, b, a};
    commands->list->OMSetBlendFactor(v);
}
API void commands_stencil(Commands* commands, UINT reference) noexcept
{
    commands->list->OMSetStencilRef(reference);
}
API void commands_topology(Commands* commands, UINT topology) noexcept
{
    commands->list->IASetPrimitiveTopology(static_cast<D3D_PRIMITIVE_TOPOLOGY>(topology));
}
API void commands_indices(Commands* commands, ID3D12Resource* buffer, UINT64 offset, UINT size, UINT format) noexcept
{
    D3D12_INDEX_BUFFER_VIEW v{buffer->GetGPUVirtualAddress() + offset, size, static_cast<DXGI_FORMAT>(format)};
    commands->list->IASetIndexBuffer(&v);
}
API void commands_draw(Commands* commands, UINT count, UINT instances, UINT first, INT base, UINT first_instance,
                       int indexed) noexcept
{
    const UINT info[]{0, indexed ? static_cast<UINT>(base) : first};
    commands->list->SetGraphicsRoot32BitConstants(3, 2, info, 0);
    commands->list->SetGraphicsRootShaderResourceView(4, 0);
    if (indexed)
    {
        commands->list->DrawIndexedInstanced(count, instances, first, base, first_instance);
    }
    else
    {
        commands->list->DrawInstanced(count, instances, first, first_instance);
    }
}
API void commands_dispatch(Commands* commands, UINT x, UINT y, UINT z) noexcept
{
    commands->list->Dispatch(x, y, z);
}
API HRESULT commands_indirect(Device* device, Commands* commands, ID3D12Resource* buffer, UINT64 offset, UINT kind,
                              ID3D12CommandSignature** output) noexcept
{
    D3D12_INDIRECT_ARGUMENT_DESC argument{};
    argument.Type = kind == 0   ? D3D12_INDIRECT_ARGUMENT_TYPE_DRAW
                    : kind == 1 ? D3D12_INDIRECT_ARGUMENT_TYPE_DRAW_INDEXED
                                : D3D12_INDIRECT_ARGUMENT_TYPE_DISPATCH;
    D3D12_COMMAND_SIGNATURE_DESC desc{kind == 0 ? 16u : kind == 1 ? 20u : 12u, 1, &argument, 0};
    CHECK(device->device->CreateCommandSignature(&desc, nullptr, IID_PPV_ARGS(output)));
    if (kind < 2)
    {
        const UINT info[]{kind + 1, 0};
        commands->list->SetGraphicsRoot32BitConstants(3, 2, info, 0);
        commands->list->SetGraphicsRootShaderResourceView(4, buffer->GetGPUVirtualAddress() + offset);
    }
    commands->list->ExecuteIndirect(*output, 1, buffer, offset, nullptr, 0);
    return S_OK;
}

struct Attachment
{
    ID3D12Resource* resource;
    UINT format, mip, layer, clear;
    FLOAT values[4];
};
API HRESULT commands_render_begin(Device* device, Commands* commands, const Attachment* colors, UINT count,
                                  const Attachment* depth) noexcept
{
    // A fresh heap per pass is retained by a separate managed owner until GPU completion.
    D3D12_CPU_DESCRIPTOR_HANDLE handles[8]{};
    if (count)
    {
        D3D12_DESCRIPTOR_HEAP_DESC heap{D3D12_DESCRIPTOR_HEAP_TYPE_RTV, count, D3D12_DESCRIPTOR_HEAP_FLAG_NONE, 0};
        CHECK(device->device->CreateDescriptorHeap(&heap, IID_PPV_ARGS(&commands->render_targets)));
        auto start = commands->render_targets->GetCPUDescriptorHandleForHeapStart();
        auto stride = device->device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        for (UINT i = 0; i < count; i++)
        {
            handles[i].ptr = start.ptr + i * stride;
            D3D12_RENDER_TARGET_VIEW_DESC view{};
            view.Format = static_cast<DXGI_FORMAT>(colors[i].format);
            view.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2DARRAY;
            view.Texture2DArray = {colors[i].mip, colors[i].layer, 1, 0};
            device->device->CreateRenderTargetView(colors[i].resource, &view, handles[i]);
        }
    }
    D3D12_CPU_DESCRIPTOR_HANDLE dsv{};
    if (depth)
    {
        D3D12_DESCRIPTOR_HEAP_DESC heap{D3D12_DESCRIPTOR_HEAP_TYPE_DSV, 1, D3D12_DESCRIPTOR_HEAP_FLAG_NONE, 0};
        CHECK(device->device->CreateDescriptorHeap(&heap, IID_PPV_ARGS(&commands->depth_target)));
        dsv = commands->depth_target->GetCPUDescriptorHandleForHeapStart();
        D3D12_DEPTH_STENCIL_VIEW_DESC view{};
        view.Format = static_cast<DXGI_FORMAT>(depth->format);
        view.ViewDimension = D3D12_DSV_DIMENSION_TEXTURE2DARRAY;
        view.Texture2DArray = {depth->mip, depth->layer, 1};
        device->device->CreateDepthStencilView(depth->resource, &view, dsv);
    }
    commands->list->OMSetRenderTargets(count, handles, FALSE, depth ? &dsv : nullptr);
    for (UINT i = 0; i < count; i++)
    {
        if (colors[i].clear)
        {
            commands->list->ClearRenderTargetView(handles[i], colors[i].values, 0, nullptr);
        }
    }
    if (depth && depth->clear)
    {
        commands->list->ClearDepthStencilView(dsv, static_cast<D3D12_CLEAR_FLAGS>(depth->clear), depth->values[0],
                                              static_cast<UINT8>(depth->values[1]), 0, nullptr);
    }
    return S_OK;
}
API void commands_detach_heaps(Commands* commands, ID3D12DescriptorHeap** rtv, ID3D12DescriptorHeap** dsv) noexcept
{
    *rtv = commands->render_targets.Detach();
    *dsv = commands->depth_target.Detach();
}
