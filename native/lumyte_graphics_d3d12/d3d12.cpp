#include <initguid.h>
#include <d3d12.h>
#include <d3d12sdklayers.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <memory>
#include <new>
#include <cstring>
#include <cstdio>

using Microsoft::WRL::ComPtr;
#define API extern "C" __declspec(dllexport)
#define CHECK(call)                                                                                                    \
    do                                                                                                                 \
    {                                                                                                                  \
        const HRESULT result = (call);                                                                                 \
        if (FAILED(result))                                                                                            \
            return result;                                                                                             \
    } while (false)

struct Device
{
    ComPtr<ID3D12DeviceFactory> factory;
    ComPtr<ID3D12Device> device;
    ComPtr<ID3D12CommandQueue> queue;
};
struct Commands
{
    ComPtr<ID3D12CommandAllocator> allocator;
    ComPtr<ID3D12GraphicsCommandList> list;
    ComPtr<ID3D12GraphicsCommandList7> barriers;
    ComPtr<ID3D12DescriptorHeap> render_targets;
    ComPtr<ID3D12DescriptorHeap> depth_target;
};

static HRESULT report_failure(Device* device, HRESULT result) noexcept
{
    if (FAILED(result))
    {
        ComPtr<ID3D12InfoQueue> messages;
        if (SUCCEEDED(device->device.As(&messages)))
        {
            for (UINT64 i = 0; i < messages->GetNumStoredMessagesAllowedByRetrievalFilter(); i++)
            {
                SIZE_T size = 0;
                if (FAILED(messages->GetMessage(i, nullptr, &size)))
                {
                    continue;
                }
                auto bytes = std::unique_ptr<char[]>(new (std::nothrow) char[size]);
                if (!bytes)
                {
                    break;
                }
                auto message = reinterpret_cast<D3D12_MESSAGE*>(bytes.get());
                if (SUCCEEDED(messages->GetMessage(i, message, &size)))
                {
                    std::fprintf(stderr, "D3D12: %s\n", message->pDescription);
                }
            }
            messages->ClearStoredMessages();
        }
    }
    return result;
}

API HRESULT device_create(UINT adapter_index, int warp, int debug, int partial, const char* sdk_path,
                          Device** output) noexcept
{
    *output = nullptr;
    ComPtr<ID3D12SDKConfiguration1> sdk;
    CHECK(D3D12GetInterface(CLSID_D3D12SDKConfiguration, IID_PPV_ARGS(&sdk)));
    ComPtr<ID3D12DeviceFactory> device_factory;
    CHECK(sdk->CreateDeviceFactory(LUMYTE_AGILITY_VERSION, sdk_path, IID_PPV_ARGS(&device_factory)));
    if (partial)
    {
#ifdef LUMYTE_PREVIEW
        CHECK(device_factory->EnableExperimentalFeatures(1, &D3D12StateObjectsExperiment, nullptr, nullptr));
#else
        return DXGI_ERROR_UNSUPPORTED;
#endif
    }
    if (debug)
    {
        ComPtr<ID3D12Debug> layer;
        CHECK(device_factory->GetConfigurationInterface(CLSID_D3D12Debug, IID_PPV_ARGS(&layer)));
        layer->EnableDebugLayer();
    }
    ComPtr<IDXGIFactory4> factory;
    CHECK(CreateDXGIFactory2(debug ? DXGI_CREATE_FACTORY_DEBUG : 0, IID_PPV_ARGS(&factory)));
    ComPtr<IDXGIAdapter1> adapter;
    if (warp)
    {
        CHECK(factory->EnumWarpAdapter(IID_PPV_ARGS(&adapter)));
    }
    else
    {
        CHECK(factory->EnumAdapters1(adapter_index, &adapter));
        DXGI_ADAPTER_DESC1 description{};
        CHECK(adapter->GetDesc1(&description));
        if (description.Flags & DXGI_ADAPTER_FLAG_SOFTWARE)
        {
            return DXGI_ERROR_UNSUPPORTED;
        }
    }
    auto owner = std::unique_ptr<Device>(new (std::nothrow) Device());
    if (!owner)
    {
        return E_OUTOFMEMORY;
    }
    owner->factory = device_factory;
    CHECK(device_factory->CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&owner->device)));
    D3D12_FEATURE_DATA_D3D12_OPTIONS12 options{};
    CHECK(owner->device->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS12, &options, sizeof(options)));
    if (!options.EnhancedBarriersSupported)
    {
        return DXGI_ERROR_UNSUPPORTED;
    }
    if (partial)
    {
#ifdef LUMYTE_PREVIEW
        D3D12_FEATURE_DATA_PARTIAL_GRAPHICS_PROGRAMS features{};
        CHECK(owner->device->CheckFeatureSupport(D3D12_FEATURE_PARTIAL_GRAPHICS_PROGRAMS, &features, sizeof(features)));
        if (features.PartialGraphicsProgramsTier < D3D12_PARTIAL_GRAPHICS_PROGRAMS_TIER_1_0)
        {
            return DXGI_ERROR_UNSUPPORTED;
        }
#endif
    }
    ComPtr<ID3D12DeviceConfiguration> configuration;
    CHECK(owner->device.As(&configuration));
    if (configuration->GetDesc().SDKVersion != LUMYTE_AGILITY_VERSION)
    {
        return D3D12_ERROR_INVALID_REDIST;
    }
    D3D12_COMMAND_QUEUE_DESC queue{};
    queue.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    CHECK(owner->device->CreateCommandQueue(&queue, IID_PPV_ARGS(&owner->queue)));
    *output = owner.release();
    return S_OK;
}

API void device_destroy(Device* device) noexcept
{
    delete device;
}
API UINT device_sdk_version(Device* device) noexcept
{
    ComPtr<ID3D12DeviceConfiguration> configuration;
    if (FAILED(device->device.As(&configuration)))
    {
        return 0;
    }
    return configuration->GetDesc().SDKVersion;
}
API HRESULT device_removed(Device* device) noexcept
{
    return device->device->GetDeviceRemovedReason();
}
API void object_release(IUnknown* object) noexcept
{
    if (object)
    {
        object->Release();
    }
}

API HRESULT buffer_create(Device* device, UINT64 size, int memory, int writable, ID3D12Resource** output) noexcept
{
    D3D12_HEAP_PROPERTIES heap{};
    heap.Type = memory == 1 ? D3D12_HEAP_TYPE_UPLOAD : memory == 2 ? D3D12_HEAP_TYPE_READBACK : D3D12_HEAP_TYPE_DEFAULT;
    heap.CreationNodeMask = heap.VisibleNodeMask = 1;
    D3D12_RESOURCE_DESC desc{};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    desc.Width = size;
    desc.Height = 1;
    desc.DepthOrArraySize = 1;
    desc.MipLevels = 1;
    desc.SampleDesc.Count = 1;
    desc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    desc.Flags = writable ? D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS : D3D12_RESOURCE_FLAG_NONE;
    const auto state = memory == 1   ? D3D12_RESOURCE_STATE_GENERIC_READ
                       : memory == 2 ? D3D12_RESOURCE_STATE_COPY_DEST
                                     : D3D12_RESOURCE_STATE_COMMON;
    return device->device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc, state, nullptr,
                                                   IID_PPV_ARGS(output));
}
API HRESULT buffer_map(ID3D12Resource* buffer, UINT64 size, int read, void** output) noexcept
{
    const D3D12_RANGE range{0, read ? static_cast<SIZE_T>(size) : 0};
    return buffer->Map(0, &range, output);
}
API void buffer_unmap(ID3D12Resource* buffer, UINT64 size, int write) noexcept
{
    const D3D12_RANGE range{0, write ? static_cast<SIZE_T>(size) : 0};
    buffer->Unmap(0, &range);
}
API HRESULT commands_create(Device* device, Commands** output) noexcept
{
    *output = nullptr;
    auto commands = std::unique_ptr<Commands>(new (std::nothrow) Commands());
    if (!commands)
    {
        return E_OUTOFMEMORY;
    }
    CHECK(device->device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&commands->allocator)));
    CHECK(device->device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, commands->allocator.Get(), nullptr,
                                            IID_PPV_ARGS(&commands->list)));
    CHECK(commands->list.As(&commands->barriers));
    *output = commands.release();
    return S_OK;
}
API void commands_destroy(Commands* commands) noexcept
{
    delete commands;
}
API HRESULT commands_finish(Commands* commands) noexcept
{
    return commands->list->Close();
}
API void commands_copy(Commands* commands, ID3D12Resource* source, UINT64 source_offset, ID3D12Resource* destination,
                       UINT64 destination_offset, UINT64 size) noexcept
{
    commands->list->CopyBufferRegion(destination, destination_offset, source, source_offset, size);
}
API HRESULT fence_create(Device* device, ID3D12Fence** output) noexcept
{
    return device->device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(output));
}
API HRESULT commands_execute(Device* device, Commands** commands, UINT count) noexcept
{
    auto lists = std::unique_ptr<ID3D12CommandList*[]>(new (std::nothrow) ID3D12CommandList*[count]);
    if (!lists)
    {
        return E_OUTOFMEMORY;
    }
    for (UINT i = 0; i < count; ++i)
    {
        lists[i] = commands[i]->list.Get();
    }
    device->queue->ExecuteCommandLists(count, lists.get());
    return S_OK;
}
API HRESULT queue_signal(Device* device, ID3D12Fence* fence) noexcept
{
    return device->queue->Signal(fence, 1);
}
API UINT64 fence_completed(ID3D12Fence* fence) noexcept
{
    return fence->GetCompletedValue();
}
API HRESULT queue_signal_value(Device* device, ID3D12Fence* fence, UINT64 value) noexcept
{
    return device->queue->Signal(fence, value);
}
API HRESULT queue_wait_value(Device* device, ID3D12Fence* fence, UINT64 value) noexcept
{
    return device->queue->Wait(fence, value);
}

API HRESULT texture_create(Device* device, UINT width, UINT height, UINT mips, UINT layers, UINT format, UINT usage,
                           ID3D12Resource** output) noexcept
{
    D3D12_RESOURCE_DESC desc{};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    desc.Width = width;
    desc.Height = height;
    desc.DepthOrArraySize = static_cast<UINT16>(layers);
    desc.MipLevels = static_cast<UINT16>(mips);
    desc.Format = static_cast<DXGI_FORMAT>(format);
    desc.SampleDesc.Count = 1;
    if (usage & 8)
    {
        desc.Flags = format == DXGI_FORMAT_D32_FLOAT || format == DXGI_FORMAT_D24_UNORM_S8_UINT
                         ? D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL
                         : D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
    }
    D3D12_HEAP_PROPERTIES heap{};
    heap.Type = D3D12_HEAP_TYPE_DEFAULT;
    heap.CreationNodeMask = heap.VisibleNodeMask = 1;
    return device->device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc, D3D12_RESOURCE_STATE_COMMON,
                                                   nullptr, IID_PPV_ARGS(output));
}

struct TextureRegion
{
    UINT subresource, x, y, width, height;
};
API void commands_copy_texture(Commands* commands, ID3D12Resource* source, TextureRegion src,
                               ID3D12Resource* destination, TextureRegion dst) noexcept
{
    D3D12_TEXTURE_COPY_LOCATION s{}, d{};
    s.pResource = source;
    s.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    s.SubresourceIndex = src.subresource;
    d.pResource = destination;
    d.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    d.SubresourceIndex = dst.subresource;
    D3D12_BOX box{src.x, src.y, 0, src.x + src.width, src.y + src.height, 1};
    commands->list->CopyTextureRegion(&d, dst.x, dst.y, 0, &s, &box);
}

API void commands_copy_buffer_texture(Commands* commands, ID3D12Resource* buffer, UINT64 offset, UINT pitch,
                                      ID3D12Resource* texture, TextureRegion region, int upload) noexcept
{
    D3D12_TEXTURE_COPY_LOCATION b{}, t{};
    b.pResource = buffer;
    b.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
    b.PlacedFootprint = {offset, {texture->GetDesc().Format, region.width, region.height, 1, pitch}};
    t.pResource = texture;
    t.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    t.SubresourceIndex = region.subresource;
    if (upload)
    {
        commands->list->CopyTextureRegion(&t, region.x, region.y, 0, &b, nullptr);
    }
    else
    {
        D3D12_BOX box{region.x, region.y, 0, region.x + region.width, region.y + region.height, 1};
        commands->list->CopyTextureRegion(&b, 0, 0, 0, &t, &box);
    }
}
#include "rendering.inl"
#include "binding.inl"
#include "presentation.inl"
#include "barriers.inl"
