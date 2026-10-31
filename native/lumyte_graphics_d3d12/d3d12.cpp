#include <initguid.h>
#include <d3d12.h>
#include <d3d12sdklayers.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <memory>
#include <new>

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
};

API HRESULT device_create(UINT adapter_index, int warp, int debug, const char* sdk_path, Device** output) noexcept
{
    *output = nullptr;
    ComPtr<ID3D12SDKConfiguration1> sdk;
    CHECK(D3D12GetInterface(CLSID_D3D12SDKConfiguration, IID_PPV_ARGS(&sdk)));
    ComPtr<ID3D12DeviceFactory> device_factory;
    CHECK(sdk->CreateDeviceFactory(LUMYTE_AGILITY_VERSION, sdk_path, IID_PPV_ARGS(&device_factory)));
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

API HRESULT buffer_create(Device* device, UINT64 size, int memory, ID3D12Resource** output) noexcept
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
API void commands_barrier(Commands* commands, ID3D12Resource* buffer, UINT before, UINT after) noexcept
{
    if (before == after)
    {
        return;
    }
    D3D12_RESOURCE_BARRIER barrier{};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = buffer;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    barrier.Transition.StateBefore = static_cast<D3D12_RESOURCE_STATES>(before);
    barrier.Transition.StateAfter = static_cast<D3D12_RESOURCE_STATES>(after);
    commands->list->ResourceBarrier(1, &barrier);
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
