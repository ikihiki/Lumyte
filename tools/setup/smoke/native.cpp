#include <vulkan/vulkan.h>
#include <zlib.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <vector>

#ifdef _WIN32
#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#define LUMYTE_EXPORT __declspec(dllexport)
#else
#define LUMYTE_EXPORT
#endif

static void check(VkResult result)
{
    if (result != VK_SUCCESS)
    {
        throw std::runtime_error("Vulkan operation failed");
    }
}

struct Resources
{
    VkInstance instance{};
    VkDevice device{};
    VkBuffer buffer{};
    VkDeviceMemory memory{};
    VkCommandPool pool{};
    ~Resources()
    {
        if (device)
        {
            vkDeviceWaitIdle(device);
            if (pool)
            {
                vkDestroyCommandPool(device, pool, nullptr);
            }
            if (buffer)
            {
                vkDestroyBuffer(device, buffer, nullptr);
            }
            if (memory)
            {
                vkFreeMemory(device, memory, nullptr);
            }
            vkDestroyDevice(device, nullptr);
        }
        if (instance)
        {
            vkDestroyInstance(instance, nullptr);
        }
    }
};

extern "C" LUMYTE_EXPORT int lumyte_add(int a, int b)
{
    return a + b;
}

extern "C" LUMYTE_EXPORT int lumyte_verify_vulkan()
{
    try
    {
        const unsigned char input[] = "lumyte-vcpkg-zlib";
        unsigned char compressed[128]{}, output[128]{};
        uLongf compressed_size = sizeof(compressed), output_size = sizeof(output);
        if (compress(compressed, &compressed_size, input, sizeof(input)) != Z_OK ||
            uncompress(output, &output_size, compressed, compressed_size) != Z_OK || output_size != sizeof(input) ||
            std::memcmp(input, output, sizeof(input)))
        {
            throw std::runtime_error("zlib roundtrip failed");
        }

        Resources r;
        VkInstanceCreateInfo instance_info{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};
        check(vkCreateInstance(&instance_info, nullptr, &r.instance));
        uint32_t count = 0;
        check(vkEnumeratePhysicalDevices(r.instance, &count, nullptr));
        if (!count)
        {
            throw std::runtime_error("No Vulkan device");
        }
        std::vector<VkPhysicalDevice> devices(count);
        check(vkEnumeratePhysicalDevices(r.instance, &count, devices.data()));
        const auto physical = devices.front();
        VkPhysicalDeviceProperties properties{};
        vkGetPhysicalDeviceProperties(physical, &properties);
        if (properties.deviceType != VK_PHYSICAL_DEVICE_TYPE_CPU)
        {
            throw std::runtime_error("Expected lavapipe CPU device");
        }
        std::printf("Vulkan device: %s\n", properties.deviceName);

        vkGetPhysicalDeviceQueueFamilyProperties(physical, &count, nullptr);
        std::vector<VkQueueFamilyProperties> families(count);
        vkGetPhysicalDeviceQueueFamilyProperties(physical, &count, families.data());
        uint32_t family = 0;
        for (; family < count; ++family)
        {
            if (families[family].queueCount && (families[family].queueFlags & VK_QUEUE_TRANSFER_BIT))
            {
                break;
            }
        }
        if (family == count)
        {
            throw std::runtime_error("No transfer queue");
        }
        float priority = 1.0f;
        VkDeviceQueueCreateInfo queue_info{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
        queue_info.queueFamilyIndex = family;
        queue_info.queueCount = 1;
        queue_info.pQueuePriorities = &priority;
        VkDeviceCreateInfo device_info{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
        device_info.queueCreateInfoCount = 1;
        device_info.pQueueCreateInfos = &queue_info;
        check(vkCreateDevice(physical, &device_info, nullptr, &r.device));
        VkQueue queue{};
        vkGetDeviceQueue(r.device, family, 0, &queue);

        VkBufferCreateInfo buffer_info{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO};
        buffer_info.size = sizeof(uint32_t);
        buffer_info.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        buffer_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        check(vkCreateBuffer(r.device, &buffer_info, nullptr, &r.buffer));
        VkMemoryRequirements requirements{};
        vkGetBufferMemoryRequirements(r.device, r.buffer, &requirements);
        VkPhysicalDeviceMemoryProperties memory_properties{};
        vkGetPhysicalDeviceMemoryProperties(physical, &memory_properties);
        uint32_t memory_type = 0;
        constexpr auto flags = VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT;
        for (; memory_type < memory_properties.memoryTypeCount; ++memory_type)
        {
            if ((requirements.memoryTypeBits & (1u << memory_type)) &&
                (memory_properties.memoryTypes[memory_type].propertyFlags & flags) == flags)
            {
                break;
            }
        }
        if (memory_type == memory_properties.memoryTypeCount)
        {
            throw std::runtime_error("No coherent host-visible memory");
        }
        VkMemoryAllocateInfo allocation{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
        allocation.allocationSize = requirements.size;
        allocation.memoryTypeIndex = memory_type;
        check(vkAllocateMemory(r.device, &allocation, nullptr, &r.memory));
        check(vkBindBufferMemory(r.device, r.buffer, r.memory, 0));

        VkCommandPoolCreateInfo pool_info{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};
        pool_info.queueFamilyIndex = family;
        check(vkCreateCommandPool(r.device, &pool_info, nullptr, &r.pool));
        VkCommandBufferAllocateInfo command_info{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
        command_info.commandPool = r.pool;
        command_info.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        command_info.commandBufferCount = 1;
        VkCommandBuffer command{};
        check(vkAllocateCommandBuffers(r.device, &command_info, &command));
        VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};
        check(vkBeginCommandBuffer(command, &begin));
        constexpr uint32_t expected = 0x12345678;
        vkCmdFillBuffer(command, r.buffer, 0, sizeof(uint32_t), expected);
        VkMemoryBarrier barrier{VK_STRUCTURE_TYPE_MEMORY_BARRIER};
        barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        barrier.dstAccessMask = VK_ACCESS_HOST_READ_BIT;
        vkCmdPipelineBarrier(command, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_HOST_BIT, 0, 1, &barrier, 0,
                             nullptr, 0, nullptr);
        check(vkEndCommandBuffer(command));
        VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO};
        submit.commandBufferCount = 1;
        submit.pCommandBuffers = &command;
        check(vkQueueSubmit(queue, 1, &submit, VK_NULL_HANDLE));
        check(vkQueueWaitIdle(queue));
        void* mapped = nullptr;
        check(vkMapMemory(r.device, r.memory, 0, sizeof(uint32_t), 0, &mapped));
        const auto actual = *static_cast<uint32_t*>(mapped);
        vkUnmapMemory(r.device, r.memory);
        if (actual != expected)
        {
            throw std::runtime_error("Vulkan readback mismatch");
        }
        return 0;
    }
    catch (const std::exception& e)
    {
        std::fprintf(stderr, "Smoke test failed: %s\n", e.what());
        return 1;
    }
}

#ifdef _WIN32
extern "C" LUMYTE_EXPORT int lumyte_verify_directx()
{
    Microsoft::WRL::ComPtr<IDXGIFactory4> factory;
    Microsoft::WRL::ComPtr<IDXGIAdapter> adapter;
    Microsoft::WRL::ComPtr<ID3D12Device> device;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))) ||
        FAILED(factory->EnumWarpAdapter(IID_PPV_ARGS(&adapter))) ||
        FAILED(D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device))))
    {
        std::fprintf(stderr, "Direct3D 12 WARP device creation failed\n");
        return 1;
    }
    std::puts("PASS: Direct3D 12 WARP device created");
    return 0;
}
#endif
