API HRESULT partial_create(Device* device, const void* vs, UINT64 vs_size, const wchar_t* vs_entry, const void* ps,
                           UINT64 ps_size, const wchar_t* ps_entry, UINT topology, UINT optimization,
                           PartialProgram** output) noexcept
{
    *output = nullptr;
#ifdef LUMYTE_PREVIEW
    auto program = std::unique_ptr<PartialProgram>(new (std::nothrow) PartialProgram());
    if (!program)
    {
        return E_OUTOFMEMORY;
    }
    CHECK(create_root(device, &program->root));
    program->optimization = optimization;
    D3D12_EXPORT_DESC exports[2]{{L"lumyteVS", vs_entry, D3D12_EXPORT_FLAG_NONE},
                                 {L"lumytePS", ps_entry, D3D12_EXPORT_FLAG_NONE}};
    D3D12_DXIL_LIBRARY_DESC libraries[2]{{{vs, static_cast<SIZE_T>(vs_size)}, 1, &exports[0]},
                                         {{ps, static_cast<SIZE_T>(ps_size)}, 1, &exports[1]}};
    auto primitive = static_cast<D3D12_PRIMITIVE_TOPOLOGY_TYPE>(topology);
    D3D12_PIXEL_SHADER_PARTIAL_PROGRAM_FIELDS_DESC fields{};
    fields.LineRasterizationMode = D3D12_LINE_RASTERIZATION_MODE_ALIASED;
    fields.LateLinkRasterizerSubobject = fields.LateLinkBlendSubobject = fields.LateLinkSampleMaskSubobject = TRUE;
    fields.LateLinkSampleDescSubobject = fields.LateLinkDepthStencilFormatSubobject =
        fields.LateLinkRenderTargetFormatSubobject = fields.LateLinkDepthStencilSubobject = TRUE;
    D3D12_PRERASTERIZATION_SHADERS_PARTIAL_PROGRAM_FIELDS_DESC pre{ps_size == 0, FALSE};
    D3D12_STATE_OBJECT_CONFIG config{D3D12_STATE_OBJECT_FLAG_ALLOW_EXTERNAL_DEPENDENCIES_ON_LOCAL_DEFINITIONS};
    ID3D12RootSignature* root = program->root.Get();
    D3D12_STATE_SUBOBJECT objects[8]{};
    objects[0] = {D3D12_STATE_SUBOBJECT_TYPE_GLOBAL_ROOT_SIGNATURE, &root};
    objects[1] = {D3D12_STATE_SUBOBJECT_TYPE_DXIL_LIBRARY, &libraries[0]};
    objects[2] = {D3D12_STATE_SUBOBJECT_TYPE_PRIMITIVE_TOPOLOGY, &primitive};
    objects[3] = {D3D12_STATE_SUBOBJECT_TYPE_PRERASTERIZATION_SHADERS_PARTIAL_PROGRAM_FIELDS, &pre};
    objects[4] = {D3D12_STATE_SUBOBJECT_TYPE_STATE_OBJECT_CONFIG, &config};
    const D3D12_STATE_SUBOBJECT* parts[]{&objects[2], &objects[3], &objects[6]};
    LPCWSTR names[]{L"lumyteVS", L"lumytePS"};
    D3D12_PARTIAL_GRAPHICS_PROGRAM_DESC partial{L"lumytePartial",
                                                ps_size ? 2u : 1u,
                                                names,
                                                ps_size ? 3u : 2u,
                                                parts,
                                                static_cast<D3D12_PARTIAL_GRAPHICS_PROGRAM_TYPE>(ps_size ? 3 : 1)};
    objects[5] = {D3D12_STATE_SUBOBJECT_TYPE_PARTIAL_GRAPHICS_PROGRAM, &partial};
    objects[6] = {D3D12_STATE_SUBOBJECT_TYPE_PIXEL_SHADER_PARTIAL_PROGRAM_FIELDS, &fields};
    objects[7] = {D3D12_STATE_SUBOBJECT_TYPE_DXIL_LIBRARY, &libraries[1]};
    D3D12_STATE_OBJECT_DESC desc{D3D12_STATE_OBJECT_TYPE_COLLECTION, ps_size ? 8u : 6u, objects};
    ComPtr<ID3D12Device5> native;
    CHECK(device->device.As(&native));
    CHECK(native->CreateStateObject(&desc, IID_PPV_ARGS(&program->collection)));
    *output = program.release();
    return S_OK;
#else
    (void)device;
    (void)vs;
    (void)vs_size;
    (void)vs_entry;
    (void)ps;
    (void)ps_size;
    (void)ps_entry;
    (void)topology;
    (void)optimization;
    return DXGI_ERROR_UNSUPPORTED;
#endif
}
API void partial_destroy(PartialProgram* program) noexcept
{
    delete program;
}

static HRESULT link_partial(Device* device, PartialProgram* partial, const D3D12_GRAPHICS_PIPELINE_STATE_DESC& source,
                            Pipeline* pipeline) noexcept
{
#ifdef LUMYTE_PREVIEW
    D3D12_EXISTING_COLLECTION_DESC collection{partial->collection.Get(), 0, nullptr};
    D3D12_RASTERIZER_DESC2 raster{};
    raster.FillMode = source.RasterizerState.FillMode;
    raster.CullMode = source.RasterizerState.CullMode;
    raster.FrontCounterClockwise = source.RasterizerState.FrontCounterClockwise;
    raster.DepthBias = static_cast<FLOAT>(source.RasterizerState.DepthBias);
    raster.DepthBiasClamp = source.RasterizerState.DepthBiasClamp;
    raster.SlopeScaledDepthBias = source.RasterizerState.SlopeScaledDepthBias;
    raster.DepthClipEnable = source.RasterizerState.DepthClipEnable;
    raster.LineRasterizationMode = D3D12_LINE_RASTERIZATION_MODE_ALIASED;
    D3D12_RT_FORMAT_ARRAY formats{};
    formats.NumRenderTargets = source.NumRenderTargets;
    std::memcpy(formats.RTFormats, source.RTVFormats, sizeof(formats.RTFormats));
    D3D12_STATE_OBJECT_CONFIG config{partial->optimization ? D3D12_STATE_OBJECT_FLAG_FULL_SPECIALIZATION
                                                           : D3D12_STATE_OBJECT_FLAG_PREFER_MINIMAL_LINK};
    D3D12_STATE_SUBOBJECT objects[]{{D3D12_STATE_SUBOBJECT_TYPE_EXISTING_COLLECTION, &collection},
                                    {D3D12_STATE_SUBOBJECT_TYPE_RASTERIZER, &raster},
                                    {D3D12_STATE_SUBOBJECT_TYPE_BLEND, &source.BlendState},
                                    {D3D12_STATE_SUBOBJECT_TYPE_DEPTH_STENCIL, &source.DepthStencilState},
                                    {D3D12_STATE_SUBOBJECT_TYPE_RENDER_TARGET_FORMATS, &formats},
                                    {D3D12_STATE_SUBOBJECT_TYPE_DEPTH_STENCIL_FORMAT, &source.DSVFormat},
                                    {D3D12_STATE_SUBOBJECT_TYPE_SAMPLE_DESC, &source.SampleDesc},
                                    {D3D12_STATE_SUBOBJECT_TYPE_SAMPLE_MASK, &source.SampleMask},
                                    {D3D12_STATE_SUBOBJECT_TYPE_IB_STRIP_CUT_VALUE, &source.IBStripCutValue},
                                    {D3D12_STATE_SUBOBJECT_TYPE_STATE_OBJECT_CONFIG, &config},
                                    {D3D12_STATE_SUBOBJECT_TYPE_GENERIC_PROGRAM, nullptr}};
    const D3D12_STATE_SUBOBJECT* states[]{&objects[1], &objects[2], &objects[3], &objects[4],
                                          &objects[5], &objects[6], &objects[7], &objects[8]};
    LPCWSTR name = L"lumytePartial";
    D3D12_GENERIC_PROGRAM_DESC generic{L"lumyteLinked", 1, &name, 8, states};
    objects[10].pDesc = &generic;
    D3D12_STATE_OBJECT_DESC desc{D3D12_STATE_OBJECT_TYPE_EXECUTABLE, 11, objects};
    ComPtr<ID3D12Device5> native;
    CHECK(device->device.As(&native));
    CHECK(native->CreateStateObject(&desc, IID_PPV_ARGS(&pipeline->executable)));
    ComPtr<ID3D12StateObjectProperties1> properties;
    CHECK(pipeline->executable.As(&properties));
    pipeline->identifier = properties->GetProgramIdentifier(L"lumyteLinked");
    return S_OK;
#else
    (void)device;
    (void)partial;
    (void)source;
    (void)pipeline;
    return DXGI_ERROR_UNSUPPORTED;
#endif
}
