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
            maxComputeWorkgroupsPerDimension: limits.maxComputeWorkgroupsPerDimension,
            maxComputeWorkgroupSizeX: limits.maxComputeWorkgroupSizeX,
            maxComputeWorkgroupSizeY: limits.maxComputeWorkgroupSizeY,
            maxComputeWorkgroupSizeZ: limits.maxComputeWorkgroupSizeZ,
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

export function createGraphicsPipeline(handle, vertex, vertexEntry, fragment, fragmentEntry, stateJson, formatsJson, bindingKey) {
    const state = JSON.parse(stateJson), formats = JSON.parse(formatsJson);
    const nativeFormats = ["rgba8unorm", "rgba8unorm-srgb", "bgra8unorm", "bgra8unorm-srgb"];
    const factors = ["zero", "one", "src", "one-minus-src", "src-alpha", "one-minus-src-alpha", "dst", "one-minus-dst", "dst-alpha", "one-minus-dst-alpha", "src-alpha-saturated", "constant", "one-minus-constant"];
    const operations = ["add", "subtract", "reverse-subtract", "min", "max"];
    const blend = b => ({ srcFactor: factors[b.Source], dstFactor: factors[b.Destination], operation: operations[b.Operation] });
    return handle.device.createRenderPipeline({
        layout: bindingKey ? handle.device.createPipelineLayout({ bindGroupLayouts: [shaderLayout(handle.device, bindingKey)] }) : "auto",
        vertex: { module: vertex, entryPoint: vertexEntry },
        fragment: { module: fragment, entryPoint: fragmentEntry, targets: formats.map((format, i) => {
            const c = state.ColorTargets[i];
            const result = { format: nativeFormats[format], writeMask: c.WriteMask };
            if (c.BlendEnable) result.blend = { color: blend(c.Color), alpha: blend(c.Alpha) };
            return result;
        }) },
        primitive: { topology: ["point-list", "line-list", "line-strip", "triangle-list", "triangle-strip"][state.Topology], frontFace: state.Rasterization.FrontFace === 0 ? "ccw" : "cw", cullMode: ["none", "front", "back"][state.Rasterization.Cull] },
        multisample: { count: 1, mask: state.SampleMask },
    });
}
export function createComputePipeline(handle, shader, entry, bindingKey) { return handle.device.createComputePipeline({ layout: bindingKey ? handle.device.createPipelineLayout({ bindGroupLayouts: [shaderLayout(handle.device, bindingKey, true)] }) : "auto", compute: { module: shader, entryPoint: entry } }); }
export function setRenderPipeline(pass, pipeline) { pass.setPipeline(pipeline); }
export function setComputePipeline(pass, pipeline) { pass.setPipeline(pipeline); }
export function setViewport(pass, x, y, width, height, min, max) { pass.setViewport(x, y, width, height, min, max); }
export function setScissor(pass, x, y, width, height) { pass.setScissorRect(x, y, width, height); }
export function setBlendConstant(pass, r, g, b, a) { pass.setBlendConstant({ r, g, b, a }); }
export function setStencilReference(pass, reference) { pass.setStencilReference(reference); }
export function draw(pass, vertices, instances, firstVertex, firstInstance) { pass.draw(vertices, instances, firstVertex, firstInstance); }
export function dispatch(pass, x, y, z) { pass.dispatchWorkgroups(x, y, z); }

function shaderLayout(device, key, compute = false) {
    const [textures, samplers, buffers, writable] = key.split(":").map(Number);
    const visibility = compute ? GPUShaderStage.COMPUTE : GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT;
    const entries = [{ binding: 8, visibility, buffer: { type: "uniform" } }];
    if (textures + samplers + buffers + writable > 0) entries.push({ binding: 9, visibility, buffer: { type: "read-only-storage" } });
    const extra = [10, 10 + Math.max(0, textures - 1), 10 + Math.max(0, textures - 1) + Math.max(0, samplers - 1), 10 + Math.max(0, textures - 1) + Math.max(0, samplers - 1) + Math.max(0, buffers - 1)];
    [textures, samplers, buffers, writable].forEach((count, kind) => {
        for (let i = 0; i < count; i++) {
            const binding = i === 0 ? kind : extra[kind] + i - 1;
            const entry = { binding, visibility: kind === 3 && !compute ? GPUShaderStage.FRAGMENT : visibility };
            if (kind === 0) entry.texture = { sampleType: "float", viewDimension: "2d" };
            else if (kind === 1) entry.sampler = { type: "filtering" };
            else entry.buffer = { type: kind === 2 ? "read-only-storage" : "storage" };
            entries.push(entry);
        }
    });
    if (entries.length > device.limits.maxBindingsPerBindGroup) throw new Error("Shader arguments exceed maxBindingsPerBindGroup.");
    return device.createBindGroupLayout({ entries });
}
function shaderBacking(device, view, usage) {
    const bytes = view.slice();
    const buffer = device.createBuffer({ size: (bytes.length + 3) & ~3, usage, mappedAtCreation: true });
    new Uint8Array(buffer.getMappedRange()).set(bytes);
    buffer.unmap();
    return buffer;
}
export function createShaderBinding(handle, key, compute, root, map) {
    if (root.length > handle.device.limits.maxUniformBufferBindingSize || map.length > handle.device.limits.maxStorageBufferBindingSize) throw new Error("Internal shader bindings exceed device size limits.");
    const uniform = shaderBacking(handle.device, root, GPUBufferUsage.UNIFORM);
    const binding = { layout: shaderLayout(handle.device, key, compute), buffers: [uniform], entries: [{ binding: 8, resource: { buffer: uniform, size: root.length } }] };
    if (map.length > 0) {
        const remap = shaderBacking(handle.device, map, GPUBufferUsage.STORAGE);
        binding.buffers.push(remap);
        binding.entries.push({ binding: 9, resource: { buffer: remap, size: map.length } });
    }
    return binding;
}
export function addShaderResource(binding, slot, kind, resource, size) {
    binding.entries.push({ binding: slot, resource: kind < 2 ? resource : { buffer: resource.buffer, size } });
}
export function addShaderData(handle, binding, slot, data) {
    const buffer = shaderBacking(handle.device, data, GPUBufferUsage.STORAGE);
    binding.buffers.push(buffer);
    binding.entries.push({ binding: slot, resource: { buffer, size: data.length } });
}
export function finishShaderBinding(handle, binding) { binding.group = handle.device.createBindGroup({ layout: binding.layout, entries: binding.entries }); }
export function setShaderBinding(pass, binding) { pass.setBindGroup(0, binding.group); }
export function destroyShaderBinding(binding) { for (const buffer of binding.buffers) buffer.destroy(); }
