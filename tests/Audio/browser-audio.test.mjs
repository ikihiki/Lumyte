import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const source = await readFile(new URL('../../src/Audio/Lumyte.Audio.Browser/wwwroot/lumyte-audio.js', import.meta.url), 'utf8');
const audio = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

for (const channels of [1, 2]) {
    test(`clip copies ${channels}-channel PCM synchronously without retaining or modifying its input`, () => {
        const input = new Float32Array([0.25, -0.5, 0.75, -1, 0.125, 0.5]);
        const original = input.slice();
        let borrowed = true;
        let copiedInto;
        const memory = {
            byteLength: input.byteLength,
            copyTo(destination) {
                assert.ok(borrowed);
                copiedInto = destination;
                destination.set(new Uint8Array(input.buffer));
            },
        };
        const output = [];
        const device = {
            context: {
                createBuffer(channelCount, frames, rate) {
                    assert.equal(channelCount, channels);
                    assert.equal(frames, input.length / channels);
                    assert.equal(rate, 48000);
                    // Exercise nonzero byte offsets, as the copy must respect each channel's view.
                    for (let c = 0; c < channels; c++) output.push(new Float32Array(frames + 2).subarray(1, frames + 1));
                    return { getChannelData(channel) { return output[channel]; } };
                },
            },
        };
        const clip = audio.createClip(device, 48000, channels, memory);
        borrowed = false;
        assert.deepEqual(input, original);
        if (channels === 1) assert.equal(copiedInto.buffer, output[0].buffer);
        input.fill(0);
        for (let c = 0; c < channels; c++) {
            assert.equal(clip.getChannelData(c), output[c]);
            assert.deepEqual(Array.from(output[c]), Array.from(original).filter((_, i) => i % channels === c));
        }
    });
}

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

function playback() {
    const sources = [];
    const gain = {
        gain: {
            value: 0,
            cancelScheduledValues() {},
            setValueAtTime(value) { this.value = value; },
            cancelAndHoldAtTime() {},
            linearRampToValueAtTime(value) { this.value = value; },
        },
        connect() {},
        disconnect() {},
    };
    const context = {
        state: 'running',
        currentTime: 0,
        createGain() { return gain; },
        createBufferSource() {
            const source = {
                onended: null,
                stopped: 0,
                disconnected: 0,
                connect(destination) { this.destination = destination; },
                start(when, offset) { this.offset = offset; },
                stop() { this.stopped++; },
                disconnect() { this.disconnected++; },
            };
            sources.push(source);
            return source;
        },
    };
    const clip = { length: 480000, sampleRate: 48000, duration: 10 };
    const voice = audio.createVoice({ context, closed: false }, clip, 0.5);
    return { context, sources, gain, clip, voice };
}

test('stopped and paused seeking stays idle and the next play starts from the selected frame', () => {
    const { context, sources, voice } = playback();
    audio.seek(voice, 120000);
    assert.equal(audio.framePosition(voice), 120000);
    assert.equal(sources.length, 0);
    audio.play(voice);
    assert.equal(sources[0].offset, 2.5);
    context.currentTime = 1.25;
    assert.equal(audio.framePosition(voice), 180000);
    audio.pause(voice);
    context.currentTime = 20;
    assert.equal(audio.framePosition(voice), 180000);
    audio.seek(voice, 192000);
    assert.equal(audio.framePosition(voice), 192000);
    assert.equal(sources.length, 1);
    assert.equal(voice.source, null);
    audio.play(voice);
    assert.equal(sources[1].offset, 4);
    audio.stop(voice);
    assert.equal(audio.framePosition(voice), 0);
    audio.play(voice);
    assert.equal(sources[2].offset, 0);
});

test('playing seek replaces the source, preserves gain and loop, and ignores old completion callbacks', () => {
    const { context, sources, gain, voice } = playback();
    audio.setLoop(voice, true);
    audio.setGain(voice, 0.25);
    audio.play(voice);
    const old = voice.source;
    const queuedCompletion = old.onended;
    context.currentTime = 2;
    audio.seek(voice, 432000);
    assert.equal(old.stopped, 1);
    assert.equal(old.disconnected, 1);
    assert.equal(old.onended, null);
    assert.equal(sources.length, 2);
    assert.equal(voice.source.offset, 9);
    assert.equal(voice.source.loop, true);
    assert.equal(voice.source.destination, gain);
    assert.equal(gain.gain.value, 0.25);
    assert.equal(audio.framePosition(voice), 432000);
    queuedCompletion();
    assert.equal(voice.source, sources[1]);
    assert.equal(audio.completed(voice), false);
    context.currentTime = 4;
    assert.equal(audio.framePosition(voice), 48000);
    audio.seek(voice, 480000);
    assert.equal(audio.framePosition(voice), 0);
    assert.equal(voice.source.offset, 0);
    assert.equal(voice.source.loop, true);
});

test('completion keeps the end position and explicit seeking selects the next start', () => {
    const { sources, voice, clip } = playback();
    audio.play(voice);
    sources[0].onended();
    assert.equal(audio.framePosition(voice), clip.length);
    assert.equal(audio.completed(voice), true);
    audio.seek(voice, clip.length);
    assert.equal(audio.completed(voice), true);
    audio.play(voice);
    assert.equal(voice.source.offset, 0);
    voice.source.onended();
    audio.seek(voice, 96000);
    assert.equal(audio.completed(voice), false);
    assert.equal(audio.framePosition(voice), 96000);
    assert.equal(voice.source, null);
    audio.play(voice);
    assert.equal(voice.source.offset, 2);
});

test('seeking a nonlooping playing voice to the end completes without allocating another source', () => {
    const { sources, voice, clip } = playback();
    audio.play(voice);
    audio.seek(voice, clip.length);
    assert.equal(audio.completed(voice), true);
    assert.equal(audio.framePosition(voice), clip.length);
    assert.equal(voice.source, null);
    assert.equal(sources.length, 1);
    audio.play(voice);
    assert.equal(voice.source.offset, 0);
});

test('an idle seek to the end completes on play, while enabling loop starts at zero', () => {
    const { sources, voice, clip } = playback();
    audio.seek(voice, clip.length);
    assert.equal(audio.completed(voice), false);
    audio.play(voice);
    assert.equal(audio.completed(voice), true);
    assert.equal(sources.length, 0);
    audio.play(voice);
    assert.equal(voice.source.offset, 0);
    audio.pause(voice);
    audio.seek(voice, clip.length);
    assert.equal(audio.completed(voice), false);
    audio.setLoop(voice, true);
    audio.play(voice);
    assert.equal(voice.source.offset, 0);
    assert.equal(voice.source.loop, true);
});

test('invalid seek positions leave playback, position and the live source unchanged', () => {
    const { context, sources, voice, clip } = playback();
    audio.play(voice);
    context.currentTime = 1;
    const playing = voice.source;
    for (const invalid of [-1, clip.length + 1, NaN, Infinity, 1.5]) {
        assert.throws(() => audio.seek(voice, invalid), RangeError);
        assert.equal(audio.framePosition(voice), 48000);
        assert.equal(voice.source, playing);
        assert.equal(playing.stopped, 0);
        assert.equal(sources.length, 1);
        assert.equal(audio.completed(voice), false);
    }
});

test('position reads do not move the playback origin and loop changes preserve elapsed position', () => {
    const { context, voice } = playback();
    audio.play(voice);
    audio.setLoop(voice, true);
    context.currentTime = 12.5;
    for (let count = 0; count < 10; count++) assert.equal(audio.framePosition(voice), 120000);
    audio.setLoop(voice, false);
    assert.equal(audio.framePosition(voice), 120000);
    context.currentTime = 13;
    assert.equal(audio.framePosition(voice), 144000);
    audio.pause(voice);
    context.currentTime = 30;
    assert.equal(audio.framePosition(voice), 144000);
});

test('a failed source start disconnects the candidate so later playback can retry', () => {
    const { context, sources, voice } = playback();
    const create = context.createBufferSource;
    const failure = new Error('Injected source start failure');
    context.createBufferSource = () => {
        const source = create();
        source.start = () => { throw failure; };
        return source;
    };
    audio.seek(voice, 96000);
    assert.throws(() => audio.play(voice), error => error === failure);
    assert.equal(sources[0].disconnected, 1);
    assert.equal(sources[0].onended, null);
    assert.equal(voice.source, null);
    assert.equal(audio.framePosition(voice), 96000);
    context.createBufferSource = create;
    audio.play(voice);
    assert.equal(voice.source.offset, 2);
});

test('replacement failure after a playing seek stops cleanly and can retry from the selected frame', () => {
    const { context, sources, voice } = playback();
    audio.play(voice);
    const original = voice.source;
    const create = context.createBufferSource;
    const failure = new Error('Injected replacement start failure');
    context.createBufferSource = () => {
        const candidate = create();
        candidate.start = () => { throw failure; };
        return candidate;
    };
    assert.throws(() => audio.seek(voice, 96000), error => error === failure);
    assert.equal(original.stopped, 1);
    assert.equal(original.disconnected, 1);
    assert.equal(sources[1].disconnected, 1);
    assert.equal(audio.playing(voice), false);
    assert.equal(audio.framePosition(voice), 96000);
    context.createBufferSource = create;
    audio.play(voice);
    assert.equal(audio.playing(voice), true);
    assert.equal(voice.source.offset, 2);
});

test('a failed old-source stop during seek retains live playback and its completion callback', () => {
    const { voice } = playback();
    audio.play(voice);
    const playing = voice.source;
    const onended = playing.onended;
    const failure = new Error('Injected source stop failure');
    playing.stop = () => { throw failure; };
    assert.throws(() => audio.seek(voice, 96000), error => error === failure);
    assert.equal(audio.playing(voice), true);
    assert.equal(voice.source, playing);
    assert.equal(playing.onended, onended);
    assert.equal(audio.framePosition(voice), 0);
    playing.stop = () => {};
    audio.seek(voice, 96000);
    assert.equal(voice.source.offset, 2);
});
