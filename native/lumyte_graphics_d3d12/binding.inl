struct Binding
{
    ComPtr<ID3D12DescriptorHeap> resources, samplers;
    ComPtr<ID3D12Resource> uniform;
    UINT resource_stride = 0, sampler_stride = 0;
};
API HRESULT binding_create(Device* device, const void* bytes, UINT size, Binding** output) noexcept
{
    *output = nullptr;
    auto binding = std::unique_ptr<Binding>(new (std::nothrow) Binding());
    if (!binding)
    {
        return E_OUTOFMEMORY;
    }
    CHECK(buffer_create(device, (static_cast<UINT64>(size) + 255) & ~255ull, 1, 0, &binding->uniform));
    void* mapped;
    CHECK(buffer_map(binding->uniform.Get(), size, 0, &mapped));
    std::memcpy(mapped, bytes, size);
    buffer_unmap(binding->uniform.Get(), size, 1);
    D3D12_DESCRIPTOR_HEAP_DESC heap{D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV, 256,
                                    D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE, 0};
    CHECK(device->device->CreateDescriptorHeap(&heap, IID_PPV_ARGS(&binding->resources)));
    heap.Type = D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER;
    heap.NumDescriptors = 16;
    CHECK(device->device->CreateDescriptorHeap(&heap, IID_PPV_ARGS(&binding->samplers)));
    binding->resource_stride = device->device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
    binding->sampler_stride = device->device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER);
    // Initialize all entries: tier-1/2 hardware may require valid descriptors even for unused slots.
    for (UINT i = 0; i < 256; i++)
    {
        auto handle = binding->resources->GetCPUDescriptorHandleForHeapStart();
        handle.ptr += i * binding->resource_stride;
        if (i < 192)
        {
            D3D12_SHADER_RESOURCE_VIEW_DESC view{};
            view.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            if (i < 128)
            {
                view.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
                view.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
                view.Texture2D.MipLevels = 1;
            }
            else
            {
                view.Format = DXGI_FORMAT_R32_TYPELESS;
                view.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
                view.Buffer.NumElements = 1;
                view.Buffer.Flags = D3D12_BUFFER_SRV_FLAG_RAW;
            }
            device->device->CreateShaderResourceView(nullptr, &view, handle);
        }
        else
        {
            D3D12_UNORDERED_ACCESS_VIEW_DESC view{};
            view.Format = DXGI_FORMAT_R32_TYPELESS;
            view.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
            view.Buffer.NumElements = 1;
            view.Buffer.Flags = D3D12_BUFFER_UAV_FLAG_RAW;
            device->device->CreateUnorderedAccessView(nullptr, nullptr, &view, handle);
        }
    }
    for (UINT i = 0; i < 16; i++)
    {
        auto handle = binding->samplers->GetCPUDescriptorHandleForHeapStart();
        handle.ptr += i * binding->sampler_stride;
        D3D12_SAMPLER_DESC sampler{};
        sampler.Filter = D3D12_FILTER_MIN_MAG_MIP_POINT;
        sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
        sampler.MaxAnisotropy = 1;
        sampler.ComparisonFunc = D3D12_COMPARISON_FUNC_ALWAYS;
        device->device->CreateSampler(&sampler, handle);
    }
    *output = binding.release();
    return S_OK;
}
API void binding_destroy(Binding* binding) noexcept
{
    delete binding;
}
API void binding_buffer(Device* device, Binding* binding, UINT slot, ID3D12Resource* buffer, UINT64 size,
                        int writable) noexcept
{
    auto handle = binding->resources->GetCPUDescriptorHandleForHeapStart();
    handle.ptr += (slot + (writable ? 192 : 128)) * binding->resource_stride;
    if (writable)
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC view{};
        view.Format = DXGI_FORMAT_R32_TYPELESS;
        view.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
        view.Buffer.NumElements = static_cast<UINT>(size / 4);
        view.Buffer.Flags = D3D12_BUFFER_UAV_FLAG_RAW;
        device->device->CreateUnorderedAccessView(buffer, nullptr, &view, handle);
    }
    else
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC view{};
        view.Format = DXGI_FORMAT_R32_TYPELESS;
        view.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        view.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        view.Buffer.NumElements = static_cast<UINT>(size / 4);
        view.Buffer.Flags = D3D12_BUFFER_SRV_FLAG_RAW;
        device->device->CreateShaderResourceView(buffer, &view, handle);
    }
}
API void binding_texture(Device* device, Binding* binding, UINT slot, ID3D12Resource* texture, UINT format,
                         UINT dimension, UINT mip, UINT mips, UINT layer, UINT layers) noexcept
{
    auto handle = binding->resources->GetCPUDescriptorHandleForHeapStart();
    handle.ptr += slot * binding->resource_stride;
    D3D12_SHADER_RESOURCE_VIEW_DESC view{};
    view.Format = static_cast<DXGI_FORMAT>(format);
    view.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
    // The portable helper ABI samples Texture2D. Other view dimensions may be created but cannot bind to this helper.
    (void)dimension;
    (void)layers;
    view.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
    view.Texture2D = {mip, mips, 0, 0};
    (void)layer;
    device->device->CreateShaderResourceView(texture, &view, handle);
}
API void binding_sampler(Device* device, Binding* binding, UINT slot, UINT filter, UINT u, UINT v, UINT w, FLOAT min,
                         FLOAT max, UINT anisotropy, UINT compare) noexcept
{
    auto handle = binding->samplers->GetCPUDescriptorHandleForHeapStart();
    handle.ptr += slot * binding->sampler_stride;
    D3D12_SAMPLER_DESC desc{};
    desc.Filter = static_cast<D3D12_FILTER>(filter);
    desc.AddressU = static_cast<D3D12_TEXTURE_ADDRESS_MODE>(u);
    desc.AddressV = static_cast<D3D12_TEXTURE_ADDRESS_MODE>(v);
    desc.AddressW = static_cast<D3D12_TEXTURE_ADDRESS_MODE>(w);
    desc.MinLOD = min;
    desc.MaxLOD = max;
    desc.MaxAnisotropy = anisotropy;
    desc.ComparisonFunc = static_cast<D3D12_COMPARISON_FUNC>(compare);
    device->device->CreateSampler(&desc, handle);
}
API void commands_binding(Commands* commands, Binding* binding, int compute) noexcept
{
    ID3D12DescriptorHeap* heaps[]{binding->resources.Get(), binding->samplers.Get()};
    commands->list->SetDescriptorHeaps(2, heaps);
    if (compute)
    {
        commands->list->SetComputeRootConstantBufferView(0, binding->uniform->GetGPUVirtualAddress());
        commands->list->SetComputeRootDescriptorTable(1, binding->resources->GetGPUDescriptorHandleForHeapStart());
        commands->list->SetComputeRootDescriptorTable(2, binding->samplers->GetGPUDescriptorHandleForHeapStart());
    }
    else
    {
        commands->list->SetGraphicsRootConstantBufferView(0, binding->uniform->GetGPUVirtualAddress());
        commands->list->SetGraphicsRootDescriptorTable(1, binding->resources->GetGPUDescriptorHandleForHeapStart());
        commands->list->SetGraphicsRootDescriptorTable(2, binding->samplers->GetGPUDescriptorHandleForHeapStart());
    }
}
