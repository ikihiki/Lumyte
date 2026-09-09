(async () => {
  const result = { secureContext: isSecureContext, webgpu: !!navigator.gpu, userAgent: navigator.userAgent };
  if (!navigator.gpu) return result;
  result.languageFeatures = [...navigator.gpu.wgslLanguageFeatures];
  result.adapters = [];
  for (const powerPreference of ['high-performance', 'low-power']) {
    const adapter = await navigator.gpu.requestAdapter({ powerPreference });
    if (!adapter) { result.adapters.push({ powerPreference, found: false }); continue; }
    const info = adapter.info;
    const data = { powerPreference, info: { vendor: info.vendor, architecture: info.architecture, device: info.device, description: info.description, isFallbackAdapter: info.isFallbackAdapter }, features: [...adapter.features], maxImmediateSize: adapter.limits.maxImmediateSize };
    const device = await adapter.requestDevice();
    let explicitDestroyRequested = false;
    data.deviceLossBeforeDestroy = null;
    device.lost.then(info => {
      if (!explicitDestroyRequested) data.deviceLossBeforeDestroy = { reason: info.reason, message: info.message };
    });
    data.deviceMaxImmediateSize = device.limits.maxImmediateSize;
    const encoder = device.createCommandEncoder();
    const pass = encoder.beginComputePass();
    data.setImmediates = typeof pass.setImmediates;
    pass.end();
    const uncaptured = [];
    device.addEventListener('uncapturederror', event => uncaptured.push({ name: event.error.constructor.name, message: event.error.message }));
    const errorObject = error => error ? { name: error.constructor.name, message: error.message } : null;
    const withErrors = async (name, body) => {
      const test = { name };
      device.pushErrorScope('validation');
      device.pushErrorScope('internal');
      device.pushErrorScope('out-of-memory');
      try { await body(test); }
      catch (error) { test.error = errorObject(error); }
      test.errorScopes = (await Promise.all([device.popErrorScope(), device.popErrorScope(), device.popErrorScope()])).map(errorObject);
      test.passed = !test.error && !test.errorScopes.some(Boolean) && test.matchesExpected;
      return test;
    };
    const compile = async (code, test) => {
      const module = device.createShaderModule({ code });
      const info = await module.getCompilationInfo();
      test.compilationMessages = [...info.messages].map(m => ({ type: m.type, message: m.message, lineNum: m.lineNum }));
      if (info.messages.some(m => m.type === 'error')) throw new Error('WGSL compilation failed');
      return module;
    };
    data.tests = [];
    function scalarBytes(words) { return new Uint32Array(words); }
    function mixedBytes(count, scale, vector, bias) {
      const bytes = new ArrayBuffer(32);
      const view = new DataView(bytes);
      view.setUint32(0, count, true);
      view.setFloat32(4, scale, true);
      vector.forEach((v, i) => view.setFloat32(16 + i * 4, v, true));
      view.setFloat32(28, bias, true);
      return new Uint8Array(bytes);
    }
    for (const [name, code, first, second, expected] of [
      ['handAuthoredTwoDispatches', probeShaders.handAuthored, scalarBytes([11, 7]), scalarBytes([23, 5]), [40, 74]],
      ['slangInteropTwoDispatches', probeShaders.slangInterop, scalarBytes([11, 7, 0, 0]), scalarBytes([23, 5, 0, 0]), [40, 74]],
      ['slangMixedLayoutTwoDispatches', probeShaders.slangMixed, mixedBytes(3, 2, [1, 2, 3], 4), mixedBytes(9, 3, [4, 5, 6], 7), [19, 61]],
    ]) {
      data.tests.push(await withErrors(name, async test => {
        test.immediateBytes = first.byteLength;
        test.expected = expected;
        if (name === 'slangMixedLayoutTwoDispatches') test.memberByteOffsets = { count: 0, scale: 4, vector: 16, bias: 28 };
        const module = await compile(code, test);
        const pipeline = await device.createComputePipelineAsync({ layout: 'auto', compute: { module, entryPoint: 'main' } });
        const output = device.createBuffer({ size: 4, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
        const readback = device.createBuffer({ size: 8, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
        try {
          const group = device.createBindGroup({ layout: pipeline.getBindGroupLayout(0), entries: [{ binding: 0, resource: { buffer: output } }] });
          const commands = device.createCommandEncoder();
          for (const [index, values] of [first, second].entries()) {
            const compute = commands.beginComputePass();
            compute.setPipeline(pipeline);
            compute.setBindGroup(0, group);
            compute.setImmediates(0, values);
            compute.dispatchWorkgroups(1);
            values.fill(99); // The recorded command must already own the root bytes.
            compute.end();
            commands.copyBufferToBuffer(output, 0, readback, index * 4, 4);
          }
          device.queue.submit([commands.finish()]);
          await device.queue.onSubmittedWorkDone();
          test.queueCompleted = true;
          await readback.mapAsync(GPUMapMode.READ);
          test.actual = [...new Uint32Array(readback.getMappedRange())];
          readback.unmap();
          test.matchesExpected = JSON.stringify(test.actual) === JSON.stringify(test.expected);
        } finally { output.destroy(); readback.destroy(); }
      }));
    }
    data.tests.push(await withErrors('mixedLayoutSamePassTwoDispatches', async test => {
      test.immediateBytes = 32;
      test.memberByteOffsets = { count: 0, gain: 4, offset: 16, index: 28 };
      test.expected = [8.5, 2, 3, 0, -6, 6, 8, 1];
      const module = await compile(probeShaders.mixed, test);
      const pipeline = await device.createComputePipelineAsync({ layout: 'auto', compute: { module, entryPoint: 'main' } });
      const output = device.createBuffer({ size: 32, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
      const readback = device.createBuffer({ size: 32, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
      try {
        const group = device.createBindGroup({ layout: pipeline.getBindGroupLayout(0), entries: [{ binding: 0, resource: { buffer: output } }] });
        const commands = device.createCommandEncoder();
        const compute = commands.beginComputePass();
        compute.setPipeline(pipeline);
        compute.setBindGroup(0, group);
        for (const [count, gain, offset, index] of [[3, 2.5, [1, 2, 3], 0], [5, -2, [4, 6, 8], 1]]) {
          const bytes = new ArrayBuffer(32);
          const view = new DataView(bytes);
          view.setUint32(0, count, true);
          view.setFloat32(4, gain, true);
          offset.forEach((v, i) => view.setFloat32(16 + i * 4, v, true));
          view.setUint32(28, index, true);
          compute.setImmediates(0, bytes);
          compute.dispatchWorkgroups(1);
          new Uint8Array(bytes).fill(99);
        }
        compute.end();
        commands.copyBufferToBuffer(output, 0, readback, 0, 32);
        device.queue.submit([commands.finish()]);
        await device.queue.onSubmittedWorkDone();
        test.queueCompleted = true;
        await readback.mapAsync(GPUMapMode.READ);
        test.actual = [...new Float32Array(readback.getMappedRange())];
        readback.unmap();
        test.matchesExpected = JSON.stringify(test.actual) === JSON.stringify(test.expected);
      } finally { output.destroy(); readback.destroy(); }
    }));
    data.tests.push(await withErrors('vertexAndFragmentImmediate', async test => {
      test.immediateBytes = 16;
      test.expected = [0.25, 0.5, 0.75, 1];
      const module = await compile(probeShaders.raster, test);
      const pipeline = await device.createRenderPipelineAsync({ layout: 'auto', vertex: { module, entryPoint: 'vertexMain' }, fragment: { module, entryPoint: 'fragmentMain', targets: [{ format: 'rgba32float' }] } });
      const texture = device.createTexture({ size: [1, 1], format: 'rgba32float', usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC });
      const readback = device.createBuffer({ size: 256, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
      try {
        const commands = device.createCommandEncoder();
        const render = commands.beginRenderPass({ colorAttachments: [{ view: texture.createView(), loadOp: 'clear', storeOp: 'store', clearValue: [0, 0, 0, 0] }] });
        render.setPipeline(pipeline);
        render.setImmediates(0, new Float32Array([0.25, 0.5, 0.75, 1]));
        render.draw(3);
        render.end();
        commands.copyTextureToBuffer({ texture }, { buffer: readback, bytesPerRow: 256 }, [1, 1]);
        device.queue.submit([commands.finish()]);
        await device.queue.onSubmittedWorkDone();
        test.queueCompleted = true;
        await readback.mapAsync(GPUMapMode.READ);
        test.actual = [...new Float32Array(readback.getMappedRange(), 0, 4)];
        readback.unmap();
        test.matchesExpected = JSON.stringify(test.actual) === JSON.stringify(test.expected);
      } finally { texture.destroy(); readback.destroy(); }
    }));
    data.uncapturedErrors = uncaptured;
    data.passed = data.tests.every(test => test.passed) && !uncaptured.length && !data.deviceLossBeforeDestroy;
    explicitDestroyRequested = true;
    device.destroy();
    result.adapters.push(data);
  }
  result.passed = result.adapters.every(adapter => adapter.passed);
  return result;
})()
