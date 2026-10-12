import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../../../src/Graphics/Lumyte.Graphics.Browser/wwwroot/lumyte-graphics.js', import.meta.url), 'utf8');
const graphics = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

function fakeDevice() {
    let lose;
    let complete;
    let destroys = 0;
    const device = {
        limits: {},
        lost: new Promise(resolve => { lose = resolve; }),
        queue: {
            submit() {},
            onSubmittedWorkDone: () => new Promise(resolve => { complete = resolve; }),
        },
        destroy() { destroys++; lose({ reason: 'destroyed', message: 'Explicit destruction' }); },
    };
    Object.defineProperty(globalThis, 'navigator', {
        configurable: true,
        value: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device }) } },
    });
    return { lose, device, complete: () => complete(), destroys: () => destroys };
}

test('device loss terminates pending queue waits and preserves diagnosis', async () => {
    const native = fakeDevice();
    const handle = await graphics.createDevice();
    const submission = graphics.submitCommands(handle, []);
    assert.equal(graphics.getSubmissionStatus(submission), 0);
    native.lose({ reason: 'unknown', message: 'GPU reset' });
    await graphics.waitDeviceLost(handle);
    await graphics.waitSubmission(submission);
    assert.equal(graphics.getSubmissionStatus(submission), 2);
    assert.equal(graphics.getDeviceLossReason(handle), 'unknown');
    assert.equal(graphics.getDeviceLossMessage(handle), 'GPU reset');
    assert.match(graphics.getSubmissionError(submission), /GPU reset/);
});

test('completed work remains completed and normal destruction is idempotent', async () => {
    const native = fakeDevice();
    const handle = await graphics.createDevice();
    const submission = graphics.submitCommands(handle, []);
    native.complete();
    await graphics.waitSubmission(submission);
    assert.equal(graphics.getSubmissionStatus(submission), 1);
    graphics.destroyDevice(handle);
    graphics.destroyDevice(handle);
    await graphics.waitDeviceLost(handle);
    assert.equal(native.destroys(), 1);
    assert.equal(graphics.getDeviceLossReason(handle), 'destroyed');
    assert.equal(graphics.getSubmissionStatus(submission), 1);
});
