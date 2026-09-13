async function runIndirectImmediateProbe() {
    if (!navigator.gpu?.wgslLanguageFeatures?.has('immediate_address_space')) {
        throw new Error('This browser does not support immediate_address_space.');
    }
    const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
    if (!adapter) throw new Error('No WebGPU adapter is available.');
    const device = await adapter.requestDevice({ requiredLimits: { maxImmediateSize: adapter.limits.maxImmediateSize } });
    const result = {
        userAgent: navigator.userAgent,
        adapter: { vendor: adapter.info.vendor, architecture: adapter.info.architecture, description: adapter.info.description },
        maxImmediateSize: device.limits.maxImmediateSize,
        maxComputeWorkgroupsPerDimension: device.limits.maxComputeWorkgroupsPerDimension,
        tests: [], uncapturedErrors: [], deviceLossBeforeDestroy: null
    };
    let destroying = false;
    device.lost.then(info => { if (!destroying) result.deviceLossBeforeDestroy = { reason: info.reason, message: info.message }; });
    device.addEventListener('uncapturederror', event => result.uncapturedErrors.push(event.error.message));
    try {
        for (const indirect of [false, true]) {
            device.pushErrorScope('validation');
            device.pushErrorScope('out-of-memory');
            device.pushErrorScope('internal');
            const output = device.createBuffer({ size: 8, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
            const readback = device.createBuffer({ size: 8, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST });
            const indirectBuffer = device.createBuffer({ size: 32, usage: GPUBufferUsage.INDIRECT | GPUBufferUsage.COPY_DST });
            const test = { indirect, expected: [37, 38] };
            try {
                device.queue.writeBuffer(indirectBuffer, 0, new Uint32Array([0, 0, 0, 0, 2, 1, 1, 0]));
                const module = device.createShaderModule({ code: `requires immediate_address_space;
                    var<immediate> value: u32;
                    @group(0) @binding(0) var<storage, read_write> output: array<u32>;
                    @compute @workgroup_size(1) fn main(@builtin(global_invocation_id) id: vec3u) {
                        output[id.x] = value + id.x;
                    }` });
                test.compilationMessages = [...(await module.getCompilationInfo()).messages].map(item => ({ type: item.type, message: item.message }));
                const layout = device.createBindGroupLayout({ entries: [{ binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } }] });
                const pipeline = device.createComputePipeline({
                    layout: device.createPipelineLayout({ bindGroupLayouts: [layout], immediateSize: 4 }),
                    compute: { module, entryPoint: 'main' }
                });
                const bindings = device.createBindGroup({ layout, entries: [{ binding: 0, resource: { buffer: output } }] });
                const encoder = device.createCommandEncoder();
                const pass = encoder.beginComputePass();
                pass.setPipeline(pipeline);
                pass.setBindGroup(0, bindings);
                pass.setImmediates(0, new Uint32Array([37]));
                if (indirect) pass.dispatchWorkgroupsIndirect(indirectBuffer, 16);
                else pass.dispatchWorkgroups(2);
                pass.end();
                encoder.copyBufferToBuffer(output, 0, readback, 0, 8);
                device.queue.submit([encoder.finish()]);
                await device.queue.onSubmittedWorkDone();
                await readback.mapAsync(GPUMapMode.READ);
                test.actual = [...new Uint32Array(readback.getMappedRange())];
                readback.unmap();
            } catch (error) {
                test.error = { name: error.name, message: error.message, stack: error.stack };
            } finally {
                test.errorScopes = await Promise.all([device.popErrorScope(), device.popErrorScope(), device.popErrorScope()])
                    .then(errors => errors.map(error => error ? { name: error.constructor.name, message: error.message } : null));
                output.destroy(); readback.destroy(); indirectBuffer.destroy();
            }
            test.passed = !test.error && !test.errorScopes.some(Boolean)
                && !test.compilationMessages?.some(item => item.type === 'error')
                && test.actual?.length === test.expected.length && test.actual.every((value, index) => value === test.expected[index]);
            result.tests.push(test);
        }
        result.passed = result.tests.every(test => test.passed) && !result.uncapturedErrors.length && !result.deviceLossBeforeDestroy;
        return result;
    } finally {
        destroying = true;
        device.destroy();
    }
}
