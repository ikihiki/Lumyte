export async function createDevice() {
    const gpu = globalThis.navigator?.gpu;
    if (!gpu) {
        throw new Error("WebGPU is unavailable. Use a supported browser in a secure context.");
    }
    const adapter = await gpu.requestAdapter();
    if (!adapter) {
        throw new Error("No WebGPU adapter is available.");
    }
    const device = await adapter.requestDevice();
    try {
        const limits = device.limits;
        const caps = Object.freeze({
            features: 3, // WebGPU core: indirect draw and anisotropic filtering.
            maxBufferSize: limits.maxBufferSize,
            maxStorageBufferBindingSize: limits.maxStorageBufferBindingSize,
            maxTextureDimension2D: limits.maxTextureDimension2D,
            maxColorAttachments: limits.maxColorAttachments,
            maxSampledTexturesPerStage: limits.maxSampledTexturesPerShaderStage,
            maxSamplersPerStage: limits.maxSamplersPerShaderStage,
            maxUniformBuffersPerStage: limits.maxUniformBuffersPerShaderStage,
            maxStorageBuffersPerStage: limits.maxStorageBuffersPerShaderStage,
            maxComputeInvocationsPerWorkgroup: limits.maxComputeInvocationsPerWorkgroup,
            copyBufferOffsetAlignment: 4,
            copyBufferSizeAlignment: 4,
            copyBytesPerRowAlignment: 256,
            storageBufferOffsetAlignment: limits.minStorageBufferOffsetAlignment,
        });
        return { device, caps, disposed: false };
    } catch (error) {
        device.destroy();
        throw error;
    }
}

export function getCapsJson(handle) {
    return JSON.stringify(handle.caps);
}

export function destroyDevice(handle) {
    if (!handle.disposed) {
        handle.device.destroy();
        handle.disposed = true;
    }
}
