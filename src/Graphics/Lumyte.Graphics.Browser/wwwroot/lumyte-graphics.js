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

export function createBuffer(device, size, flags, memory) {
    let usage = 0;
    if (flags & 1) usage |= GPUBufferUsage.COPY_SRC;
    if (flags & 2) usage |= GPUBufferUsage.COPY_DST;
    if (flags & 12) usage |= GPUBufferUsage.STORAGE;
    if (flags & 16) usage |= GPUBufferUsage.INDEX;
    if (memory === 2) usage |= GPUBufferUsage.MAP_WRITE;
    if (memory === 1) usage |= GPUBufferUsage.MAP_READ;
    return { buffer: device.device.createBuffer({ size, usage }), mapped: null, disposed: false };
}

export async function mapBuffer(handle, memory) {
    await handle.buffer.mapAsync(memory === 2 ? GPUMapMode.WRITE : GPUMapMode.READ);
    try {
        handle.mapped = handle.buffer.getMappedRange();
    } catch (error) {
        handle.buffer.unmap();
        throw error;
    }
}

export function unmapBuffer(handle) {
    handle.buffer.unmap();
    handle.mapped = null;
}

export function copyBufferFrom(handle, source, offset) {
    source.copyTo(new Uint8Array(handle.mapped, offset, source.byteLength));
}

export function copyBufferTo(handle, destination, offset) {
    destination.set(new Uint8Array(handle.mapped, offset, destination.byteLength));
}

export function destroyBuffer(handle) {
    if (!handle.disposed) {
        if (handle.mapped) unmapBuffer(handle);
        handle.buffer.destroy();
        handle.disposed = true;
    }
}
