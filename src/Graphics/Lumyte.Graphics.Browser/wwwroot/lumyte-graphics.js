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
            shaderTarget: 0, // WGSL.
            features: 3, // WebGPU core: indirect draw and anisotropic filtering.
            maxBufferSize: limits.maxBufferSize,
            maxStorageBufferBindingSize: limits.maxStorageBufferBindingSize,
            maxTextureDimension2D: limits.maxTextureDimension2D,
            maxTextureArrayLayers: limits.maxTextureArrayLayers,
            maxColorAttachments: limits.maxColorAttachments,
            maxSampledTexturesPerStage: limits.maxSampledTexturesPerShaderStage,
            maxSamplersPerStage: limits.maxSamplersPerShaderStage,
            maxSamplerAnisotropy: 16,
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

export function createTexture(handle, width, height, layers, mips, format, flags) {
    const formats = ["rgba8unorm", "rgba8unorm-srgb", "bgra8unorm", "bgra8unorm-srgb"];
    let usage = 0;
    if (flags & 1) usage |= GPUTextureUsage.COPY_SRC;
    if (flags & 2) usage |= GPUTextureUsage.COPY_DST;
    if (flags & 4) usage |= GPUTextureUsage.TEXTURE_BINDING;
    if (flags & 8) usage |= GPUTextureUsage.RENDER_ATTACHMENT;
    return handle.device.createTexture({
        size: [width, height, layers],
        mipLevelCount: mips,
        sampleCount: 1,
        dimension: "2d",
        format: formats[format],
        usage,
    });
}

export function createTextureView(texture, dimension, baseMip, mipCount, baseLayer, layerCount) {
    return texture.createView({
        dimension: ["2d", "2d-array", "cube", "cube-array"][dimension],
        aspect: "all",
        baseMipLevel: baseMip,
        mipLevelCount: mipCount,
        baseArrayLayer: baseLayer,
        arrayLayerCount: layerCount,
    });
}

export function destroyTexture(texture) {
    texture.destroy();
}

export function createSampler(handle, descJson) {
    const desc = JSON.parse(descJson);
    const filters = ["nearest", "linear"];
    const addresses = ["clamp-to-edge", "repeat", "mirror-repeat"];
    const compares = ["never", "less", "equal", "less-equal", "greater", "not-equal", "greater-equal", "always"];
    const native = {
        minFilter: filters[desc.minFilter],
        magFilter: filters[desc.magFilter],
        mipmapFilter: filters[desc.mipmapFilter],
        addressModeU: addresses[desc.addressU],
        addressModeV: addresses[desc.addressV],
        addressModeW: addresses[desc.addressW],
        lodMinClamp: desc.lodMinClamp,
        lodMaxClamp: desc.lodMaxClamp,
        maxAnisotropy: desc.maxAnisotropy,
    };
    if (desc.compare !== null) native.compare = compares[desc.compare];
    return handle.device.createSampler(native);
}

export function createShader(handle, code) {
    return handle.device.createShaderModule({ code });
}

export function createCommandEncoder(handle) { return handle.device.createCommandEncoder(); }
export function recordBufferCopy(encoder, src, srcOffset, dst, dstOffset, size) {
    encoder.copyBufferToBuffer(src.buffer, srcOffset, dst.buffer, dstOffset, size);
}
function textureInfo(texture, r) {
    return { texture, mipLevel: r.mip, origin: { x: r.x, y: r.y, z: r.layer } };
}
function copyExtent(r) { return { width: r.width, height: r.height, depthOrArrayLayers: r.layers }; }
export function recordTextureCopy(encoder, src, sourceRegion, dst, destinationRegion) {
    const s = JSON.parse(sourceRegion), d = JSON.parse(destinationRegion);
    encoder.copyTextureToTexture(textureInfo(src, s), textureInfo(dst, d), copyExtent(s));
}
export function recordBufferTextureCopy(encoder, buffer, offset, bytesPerRow, rowsPerImage, texture, region, upload) {
    const r = JSON.parse(region), b = { buffer: buffer.buffer, offset, bytesPerRow, rowsPerImage };
    if (upload) encoder.copyBufferToTexture(b, textureInfo(texture, r), copyExtent(r));
    else encoder.copyTextureToBuffer(textureInfo(texture, r), b, copyExtent(r));
}
export function createRenderDescriptor() { return { colorAttachments: [] }; }
export function addColorAttachment(desc, view, load, store, r, g, b, a) {
    desc.colorAttachments.push({ view, loadOp: load === 0 ? "load" : "clear", storeOp: store === 0 ? "store" : "discard", clearValue: { r, g, b, a } });
}
export function beginRenderPass(encoder, desc) { return encoder.beginRenderPass(desc); }
export function beginComputePass(encoder) { return encoder.beginComputePass(); }
export function endRenderPass(pass) { pass.end(); }
export function endComputePass(pass) { pass.end(); }
export function finishCommands(encoder) { return encoder.finish(); }
export function createCommandList() { return []; }
export function addCommand(list, commands) { list.push(commands); }
export function submitCommands(handle, list) {
    handle.device.queue.submit(list);
    const result = { status: 0, error: "", promise: null };
    result.promise = handle.device.queue.onSubmittedWorkDone().then(
        () => { result.status = 1; },
        error => { result.status = 2; result.error = String(error); });
    return result;
}
export function getSubmissionStatus(result) { return result.status; }
export function getSubmissionError(result) { return result.error; }
export async function waitSubmission(result) { await result.promise; }
