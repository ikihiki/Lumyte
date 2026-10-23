import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const source = await readFile(new URL('../../src/Audio/Lumyte.Audio.Browser/wwwroot/lumyte-audio.js', import.meta.url), 'utf8');
const audio = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

test('suspension and interruption can resume without replacing the context', async () => {
    const context = { state: 'suspended', async resume() { this.state = 'running'; } };
    const device = { context, closed: false };
    assert.equal(audio.outputState(device), 0);
    await audio.resume(device);
    assert.equal(audio.outputState(device), 1);
    context.state = 'interrupted';
    assert.equal(audio.outputState(device), 0);
    await audio.resume(device);
    assert.equal(audio.outputState(device), 1);
    assert.equal(device.context, context);
});

test('already closed contexts release without requesting a second close', () => {
    const device = {
        context: { state: 'closed', close() { throw new Error('A closed context cannot close again'); } },
        closed: false,
    };
    assert.equal(audio.outputState(device), 2);
    audio.destroyDevice(device);
    audio.destroyDevice(device);
    assert.equal(device.closed, true);
});

test('normal device disposal begins context shutdown once', async () => {
    let closes = 0;
    const device = {
        context: { state: 'running', close() { closes++; return Promise.resolve(); } },
        closed: false,
    };
    audio.destroyDevice(device);
    audio.destroyDevice(device);
    await Promise.resolve();
    assert.equal(closes, 1);
    assert.equal(audio.outputState(device), 2);
});

test('voice cleanup disconnects retained nodes after the context is lost', () => {
    let disconnectedSource = 0;
    let disconnectedGain = 0;
    const source = {
        onended() {},
        stop() { throw new Error('Context has closed'); },
        disconnect() { disconnectedSource++; },
    };
    const voice = {
        device: { context: { state: 'closed' }, closed: false },
        source,
        gain: { disconnect() { disconnectedGain++; } },
    };
    audio.destroyVoice(voice);
    assert.equal(source.onended, null);
    assert.equal(voice.source, null);
    assert.equal(disconnectedSource, 1);
    assert.equal(disconnectedGain, 1);
});

test('unexpected stop failure retains ownership for a later cleanup retry', () => {
    const failure = new Error('Unexpected stop failure');
    let reject = true;
    let disconnectedSource = 0;
    let disconnectedGain = 0;
    const source = {
        onended() {},
        stop() { if (reject) throw failure; },
        disconnect() { disconnectedSource++; },
    };
    const voice = {
        device: { context: { state: 'running' }, closed: false },
        source,
        gain: { disconnect() { disconnectedGain++; } },
    };
    assert.throws(() => audio.destroyVoice(voice), error => error === failure);
    assert.equal(voice.source, source);
    assert.equal(disconnectedSource, 0);
    assert.equal(disconnectedGain, 0);
    reject = false;
    audio.destroyVoice(voice);
    assert.equal(voice.source, null);
    assert.equal(disconnectedSource, 1);
    assert.equal(disconnectedGain, 1);
});
