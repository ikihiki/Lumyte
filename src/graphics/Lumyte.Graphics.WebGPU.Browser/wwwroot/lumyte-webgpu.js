// Host bridge for Lumyte.Graphics.WebGPU.Browser. GPU validation belongs to WebGPU.
const deviceFailures = new WeakMap();

function observeDevice(device) {
    let report;
    const failure = new Promise(resolve => { report = resolve; });
    const fail = message => {
        device.removeEventListener("uncapturederror", uncaptured);
        report(message);
    };
    const uncaptured = event => {
        event.preventDefault();
        fail(`Uncaptured WebGPU error: ${event.error.message}`);
    };
    device.addEventListener("uncapturederror", uncaptured);
    device.lost.then(info => fail(`WebGPU device lost (${info.reason}): ${info.message}`),
        error => fail(`WebGPU device loss observation failed: ${error.message}`));
    deviceFailures.set(device, failure);
}

export async function requestDevice(optionsJson) {
    const gpu = globalThis.navigator?.gpu;
    if (!gpu) { throw new Error("LUMYTE_NOT_SUPPORTED: Browser WebGPU is unavailable in this context."); }
    if (!gpu.wgslLanguageFeatures?.has("immediate_address_space")) {
        throw new Error("LUMYTE_NOT_SUPPORTED: Browser WebGPU requires WGSL immediate_address_space. Buffer fallback is disabled.");
    }
    const adapter = await gpu.requestAdapter({ powerPreference: "high-performance" });
    if (!adapter) { throw new Error("LUMYTE_NOT_SUPPORTED: No Browser WebGPU adapter is available."); }
    const options = JSON.parse(optionsJson);
    const requiredLimits = { ...options.requiredLimits };
    requiredLimits.maxImmediateSize ??= adapter.limits.maxImmediateSize;
    if (!requiredLimits.maxImmediateSize) {
        throw new Error("LUMYTE_NOT_SUPPORTED: Browser WebGPU requires a nonzero immediate input limit.");
    }
    const requiredFeatures = [];
    if (options.requireDualSourceBlend) { requiredFeatures.push("dual-source-blending"); }
    if (options.requireIndirectFirstInstance) { requiredFeatures.push("indirect-first-instance"); }
    const device = await adapter.requestDevice({ requiredFeatures, requiredLimits });
    observeDevice(device);
    if (!device.limits.maxImmediateSize ||
        typeof globalThis.GPUComputePassEncoder?.prototype.setImmediates !== "function" ||
        typeof globalThis.GPURenderPassEncoder?.prototype.setImmediates !== "function") {
        device.destroy();
        throw new Error("LUMYTE_NOT_SUPPORTED: Browser WebGPU did not enable direct immediate inputs.");
    }
    return device;
}

const limitNames = [
    "maxTextureDimension1D", "maxTextureDimension2D", "maxTextureDimension3D", "maxTextureArrayLayers",
    "maxBindGroups", "maxBindingsPerBindGroup", "maxDynamicUniformBuffersPerPipelineLayout",
    "maxDynamicStorageBuffersPerPipelineLayout", "maxSampledTexturesPerShaderStage", "maxSamplersPerShaderStage",
    "maxStorageBuffersPerShaderStage", "maxStorageTexturesPerShaderStage", "maxUniformBuffersPerShaderStage",
    "minUniformBufferOffsetAlignment", "minStorageBufferOffsetAlignment", "maxColorAttachments",
    "maxColorAttachmentBytesPerSample", "maxComputeWorkgroupStorageSize", "maxComputeInvocationsPerWorkgroup",
    "maxComputeWorkgroupSizeX", "maxComputeWorkgroupSizeY", "maxComputeWorkgroupSizeZ",
    "maxComputeWorkgroupsPerDimension", "maxImmediateSize", "maxBufferSize",
    "maxUniformBufferBindingSize", "maxStorageBufferBindingSize"
];

export function getConfiguration(device) {
    return JSON.stringify({
        capabilities: {
            directRootData: true,
            dualSourceBlend: device.features.has("dual-source-blending"),
            indirectFirstInstance: device.features.has("indirect-first-instance")
        },
        limits: Object.fromEntries(limitNames.map(name => [name, device.limits[name]]))
    });
}

export function observeFailure(device) { return deviceFailures.get(device); }

export function pushErrorScopes(device) {
    device.pushErrorScope("validation");
    device.pushErrorScope("out-of-memory");
    device.pushErrorScope("internal");
}

export function popErrorScopes(device) {
    // Pop synchronously before yielding: other resource calls must not enter these scopes.
    const internal = device.popErrorScope();
    const outOfMemory = device.popErrorScope();
    const validation = device.popErrorScope();
    return Promise.all([internal, outOfMemory, validation]).then(errors => JSON.stringify(
        errors.flatMap((error, index) => error ? [{ kind: [2, 1, 0][index], message: error.message }] : [])));
}

function hydrate(value, references) {
    if (value === null || typeof value !== "object") { return value; }
    if (Array.isArray(value)) { return value.map(item => hydrate(item, references)); }
    if (Object.hasOwn(value, "$ref")) { return references[value.$ref]; }
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, hydrate(item, references)]));
}

export function create(owner, kind, descriptorJson, references) {
    const descriptor = hydrate(JSON.parse(descriptorJson), references);
    switch (kind) {
        case "buffer": return owner.createBuffer(descriptor);
        case "texture": return owner.createTexture(descriptor);
        case "bindingLayout": return owner.createBindGroupLayout(descriptor);
        case "bindings": return owner.createBindGroup(descriptor);
        case "shaderModule": return owner.createShaderModule(descriptor);
        case "pipelineLayout": return owner.createPipelineLayout(descriptor);
        case "computePipeline": return owner.createComputePipeline(descriptor);
        case "rasterPipeline": return owner.createRenderPipeline(descriptor);
        case "textureView": return owner.createView(descriptor);
        case "sampler": return owner.createSampler(descriptor);
        default: throw new Error(`Unknown Browser WebGPU object kind: ${kind}`);
    }
}

function bytesFromBase64(base64) {
    const text = atob(base64);
    return Uint8Array.from(text, character => character.charCodeAt(0));
}

export function encode(device, commandsJson, references) {
    const commands = hydrate(JSON.parse(commandsJson), references);
    const encoder = device.createCommandEncoder();
    let pass;
    for (const command of commands) {
        switch (command.op) {
            case "beginRendering": pass = encoder.beginRenderPass(command.descriptor); break;
            case "endRendering": pass.end(); pass = undefined; break;
            case "beginCompute": pass = encoder.beginComputePass(); break;
            case "endCompute": pass.end(); pass = undefined; break;
            case "setPipeline": pass.setPipeline(command.pipeline); break;
            case "setBindings": pass.setBindGroup(command.group, command.bindings, command.offsets); break;
            case "setRoot": pass.setImmediates(0, bytesFromBase64(command.data)); break;
            case "viewport": pass.setViewport(command.x, command.y, command.width, command.height, command.minDepth, command.maxDepth); break;
            case "scissor": pass.setScissorRect(command.x, command.y, command.width, command.height); break;
            case "stencil": pass.setStencilReference(command.value); break;
            case "blend": pass.setBlendConstant(command.color); break;
            case "draw": pass.draw(command.vertexCount, command.instanceCount, command.firstVertex, command.firstInstance); break;
            case "drawIndexed":
                pass.setIndexBuffer(command.indices, command.format, command.offset, command.size);
                pass.drawIndexed(command.indexCount, command.instanceCount, command.firstIndex, command.baseVertex, command.firstInstance);
                break;
            case "drawIndirect": pass.drawIndirect(command.arguments, command.offset); break;
            case "drawIndexedIndirect":
                pass.setIndexBuffer(command.indices, command.format, command.offset, command.size);
                pass.drawIndexedIndirect(command.arguments, command.argumentsOffset);
                break;
            case "dispatch": pass.dispatchWorkgroups(command.x, command.y, command.z); break;
            case "dispatchIndirect": pass.dispatchWorkgroupsIndirect(command.arguments, command.offset); break;
            case "copyBuffer": encoder.copyBufferToBuffer(command.source, command.sourceOffset, command.destination, command.destinationOffset, command.size); break;
            case "copyBufferToTexture": encoder.copyBufferToTexture(command.source, command.destination, command.extent); break;
            case "copyTextureToBuffer": encoder.copyTextureToBuffer(command.source, command.destination, command.extent); break;
            case "copyTexture": encoder.copyTextureToTexture(command.source, command.destination, command.extent); break;
            default: throw new Error(`Unknown Browser WebGPU command: ${command.op}`);
        }
    }
    return encoder.finish();
}

export function submit(device, buffers) { device.queue.submit(buffers); }
export function onSubmittedWorkDone(device) { return device.queue.onSubmittedWorkDone(); }
export function map(buffer, mode, offset, length) { return buffer.mapAsync(mode, offset, length); }
export function getMappedRange(buffer, offset, length) { return buffer.getMappedRange(offset, length); }
export function readMapped(mappedRange, destination) { destination.set(new Uint8Array(mappedRange)); }
export function writeMapped(mappedRange, source) { source.copyTo(new Uint8Array(mappedRange)); }
export function unmap(buffer) { buffer.unmap(); }
export function destroy(resource) { resource.destroy(); }
