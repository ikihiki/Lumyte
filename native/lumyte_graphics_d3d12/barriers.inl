static D3D12_BARRIER_SYNC barrier_sync(UINT stages) noexcept
{
    D3D12_BARRIER_SYNC result = D3D12_BARRIER_SYNC_NONE;
    if (stages & 2)
    {
        result |= D3D12_BARRIER_SYNC_COPY;
    }
    if (stages & 4)
    {
        result |= D3D12_BARRIER_SYNC_EXECUTE_INDIRECT | D3D12_BARRIER_SYNC_VERTEX_SHADING;
    }
    if (stages & 8)
    {
        result |= D3D12_BARRIER_SYNC_INDEX_INPUT;
    }
    if (stages & 16)
    {
        result |= D3D12_BARRIER_SYNC_VERTEX_SHADING;
    }
    if (stages & 32)
    {
        result |= D3D12_BARRIER_SYNC_PIXEL_SHADING;
    }
    if (stages & 64)
    {
        result |= D3D12_BARRIER_SYNC_COMPUTE_SHADING;
    }
    if (stages & 128)
    {
        result |= D3D12_BARRIER_SYNC_RENDER_TARGET;
    }
    if (stages & 256)
    {
        result |= D3D12_BARRIER_SYNC_DEPTH_STENCIL;
    }
    return result;
}
static D3D12_BARRIER_ACCESS barrier_access(UINT access, bool unordered_reads = true) noexcept
{
    D3D12_BARRIER_ACCESS result = D3D12_BARRIER_ACCESS_COMMON;
    if (access & 4)
    {
        result |= D3D12_BARRIER_ACCESS_COPY_SOURCE;
    }
    if (access & 8)
    {
        result |= D3D12_BARRIER_ACCESS_COPY_DEST;
    }
    if (access & 16)
    {
        result |= D3D12_BARRIER_ACCESS_INDEX_BUFFER;
    }
    if (access & 32)
    {
        result |= D3D12_BARRIER_ACCESS_INDIRECT_ARGUMENT | D3D12_BARRIER_ACCESS_SHADER_RESOURCE;
    }
    if (access & 64)
    {
        result |= D3D12_BARRIER_ACCESS_SHADER_RESOURCE;
        // ShaderRead also covers loads through RWByteAddressBuffer descriptors.
        if (unordered_reads)
        {
            result |= D3D12_BARRIER_ACCESS_UNORDERED_ACCESS;
        }
    }
    if (access & 128)
    {
        result |= D3D12_BARRIER_ACCESS_UNORDERED_ACCESS;
    }
    if (access & 768)
    {
        result |= D3D12_BARRIER_ACCESS_RENDER_TARGET;
    }
    if (access & 1024)
    {
        result |= D3D12_BARRIER_ACCESS_DEPTH_STENCIL_READ;
    }
    if (access & 2048)
    {
        result |= D3D12_BARRIER_ACCESS_DEPTH_STENCIL_WRITE;
    }
    return result == D3D12_BARRIER_ACCESS_COMMON ? D3D12_BARRIER_ACCESS_NO_ACCESS : result;
}
API void commands_global_dependency(Commands* commands, UINT before_stages, UINT before_access, UINT after_stages,
                                    UINT after_access) noexcept
{
    D3D12_GLOBAL_BARRIER barrier{barrier_sync(before_stages), barrier_sync(after_stages), barrier_access(before_access),
                                 barrier_access(after_access)};
    D3D12_BARRIER_GROUP group{};
    group.Type = D3D12_BARRIER_TYPE_GLOBAL;
    group.NumBarriers = 1;
    group.pGlobalBarriers = &barrier;
    commands->barriers->Barrier(1, &group);
}
API void commands_buffer_dependency(Commands* commands, ID3D12Resource* buffer, UINT64 offset, UINT64 size,
                                    UINT before_stages, UINT before_access, UINT after_stages,
                                    UINT after_access) noexcept
{
    const bool unordered_reads = (buffer->GetDesc().Flags & D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS) != 0;
    // D3D12 currently requires whole-buffer barriers; managed validation still checks the requested slice.
    (void)offset;
    (void)size;
    D3D12_BUFFER_BARRIER barrier{barrier_sync(before_stages),
                                 barrier_sync(after_stages),
                                 barrier_access(before_access, unordered_reads),
                                 barrier_access(after_access, unordered_reads),
                                 buffer,
                                 0,
                                 UINT64_MAX};
    D3D12_BARRIER_GROUP group{};
    group.Type = D3D12_BARRIER_TYPE_BUFFER;
    group.NumBarriers = 1;
    group.pBufferBarriers = &barrier;
    commands->barriers->Barrier(1, &group);
}
API void commands_texture_dependency(Commands* commands, ID3D12Resource* texture, UINT mip, UINT mips, UINT layer,
                                     UINT layers, UINT before_layout, UINT after_layout, UINT before_stages,
                                     UINT before_access, UINT after_stages, UINT after_access) noexcept
{
    D3D12_TEXTURE_BARRIER barrier{};
    barrier.SyncBefore = barrier_sync(before_stages);
    barrier.SyncAfter = barrier_sync(after_stages);
    barrier.AccessBefore =
        before_layout == UINT_MAX ? D3D12_BARRIER_ACCESS_NO_ACCESS : barrier_access(before_access, false);
    barrier.AccessAfter = barrier_access(after_access, false);
    barrier.LayoutBefore = static_cast<D3D12_BARRIER_LAYOUT>(before_layout);
    barrier.LayoutAfter = static_cast<D3D12_BARRIER_LAYOUT>(after_layout);
    barrier.pResource = texture;
    barrier.Subresources = {mip,    mips, layer,
                            layers, 0,    texture->GetDesc().Format == DXGI_FORMAT_D24_UNORM_S8_UINT ? 2u : 1u};
    barrier.Flags = before_layout == UINT_MAX ? D3D12_TEXTURE_BARRIER_FLAG_DISCARD : D3D12_TEXTURE_BARRIER_FLAG_NONE;
    D3D12_BARRIER_GROUP group{};
    group.Type = D3D12_BARRIER_TYPE_TEXTURE;
    group.NumBarriers = 1;
    group.pTextureBarriers = &barrier;
    commands->barriers->Barrier(1, &group);
}
