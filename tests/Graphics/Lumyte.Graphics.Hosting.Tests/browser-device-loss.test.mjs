import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../../../src/Graphics/Lumyte.Graphics.Browser/wwwroot/lumyte-graphics.js', import.meta.url), 'utf8');
const graphics = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

function fakeDevice() {
    let lose;
    let rejectLoss;
    const pendingWork = [];
    let destroys = 0;
    const device = {
        limits: {},
        lost: new Promise((resolve, reject) => { lose = resolve; rejectLoss = reject; }),
        queue: {
            submit() {},
            onSubmittedWorkDone: () => new Promise((resolve, reject) => { pendingWork.push({ resolve, reject }); }),
        },
        destroy() { destroys++; lose({ reason: 'destroyed', message: 'Explicit destruction' }); },
    };
    Object.defineProperty(globalThis, 'navigator', {
        configurable: true,
        value: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device }) } },
    });
    return {
        lose,
        rejectLoss,
        device,
        complete: () => pendingWork.shift().resolve(),
        fail: error => pendingWork.shift().reject(error),
        destroys: () => destroys,
    };
}

test('device loss terminates pending queue waits and preserves diagnosis', async () => {
    const native = fakeDevice();
    const handle = await graphics.createDevice();
    const submission = graphics.submitCommands(handle, []);
    const otherSubmission = graphics.submitCommands(handle, []);
    assert.equal(graphics.getSubmissionStatus(submission), 0);
    assert.equal(handle.pendingSubmissions.size, 2);
    native.lose({ reason: 'unknown', message: 'GPU reset' });
    await graphics.waitDeviceLost(handle);
    await graphics.waitSubmission(submission);
    await graphics.waitSubmission(otherSubmission);
    assert.equal(graphics.getSubmissionStatus(submission), 2);
    assert.equal(graphics.getSubmissionStatus(otherSubmission), 2);
    assert.equal(handle.pendingSubmissions.size, 0);
    assert.equal(graphics.getDeviceLossReason(handle), 'unknown');
    assert.equal(graphics.getDeviceLossMessage(handle), 'GPU reset');
    assert.match(graphics.getSubmissionError(submission), /GPU reset/);
    native.complete();
    native.complete();
    await Promise.resolve();
    assert.equal(graphics.getSubmissionStatus(submission), 2);
    assert.equal(graphics.getSubmissionStatus(otherSubmission), 2);
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

test('completed submissions release their loss wait registrations', async () => {
    const native = fakeDevice();
    const handle = await graphics.createDevice();
    let lossReactions = 0;
    const then = handle.lostPromise.then.bind(handle.lostPromise);
    handle.lostPromise.then = (...args) => { lossReactions++; return then(...args); };
    for (let index = 0; index < 1000; index++) {
        const submission = graphics.submitCommands(handle, []);
        assert.equal(handle.pendingSubmissions.size, 1);
        native.complete();
        await graphics.waitSubmission(submission);
        assert.equal(graphics.getSubmissionStatus(submission), 1);
        assert.equal(handle.pendingSubmissions.size, 0);
    }
    assert.equal(lossReactions, 0, 'submissions must not add permanent reactions to the device loss promise');
});

test('failed queue work releases its loss wait registration', async () => {
    const native = fakeDevice();
    const handle = await graphics.createDevice();
    const submission = graphics.submitCommands(handle, []);
    native.fail(new Error('Queue failed'));
    await graphics.waitSubmission(submission);
    assert.equal(handle.pendingSubmissions.size, 0);
    assert.equal(graphics.getSubmissionStatus(submission), 2);
    assert.match(graphics.getSubmissionError(submission), /Queue failed/);
    native.lose({ reason: 'unknown', message: 'Later loss' });
    await graphics.waitDeviceLost(handle);
    assert.match(graphics.getSubmissionError(submission), /Queue failed/);
});

test('loss observed before submission still terminates its wait', async () => {
    const native = fakeDevice();
    const handle = await graphics.createDevice();
    native.lose({ reason: 'unknown', message: 'Already lost' });
    await graphics.waitDeviceLost(handle);
    const submission = graphics.submitCommands(handle, []);
    await graphics.waitSubmission(submission);
    assert.equal(handle.pendingSubmissions.size, 0);
    assert.equal(graphics.getSubmissionStatus(submission), 2);
    assert.match(graphics.getSubmissionError(submission), /Already lost/);
});

test('failed loss observation terminates pending queue waits', async () => {
    const native = fakeDevice();
    const handle = await graphics.createDevice();
    const submission = graphics.submitCommands(handle, []);
    const loss = graphics.waitDeviceLost(handle);
    native.rejectLoss(new Error('Loss observation failed'));
    await assert.rejects(loss, /Loss observation failed/);
    await graphics.waitSubmission(submission);
    assert.equal(handle.pendingSubmissions.size, 0);
    assert.equal(graphics.getSubmissionStatus(submission), 2);
    assert.match(graphics.getSubmissionError(submission), /Loss observation failed/);
});
