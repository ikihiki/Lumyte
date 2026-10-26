export function createDevice() {
    return { context: new AudioContext(), closed: false };
}
export function outputState(device) {
    if (device.closed || device.context.state === "closed") return 2;
    return device.context.state === "running" ? 1 : 0;
}
export async function resume(device) {
    await device.context.resume();
    if (device.context.state !== "running") throw new Error("Audio output could not resume.");
}
export function destroyDevice(device) {
    if (device.closed || device.context.state === "closed") {
        device.closed = true;
        return;
    }
    // close() initiates shutdown synchronously; completion is owned by the context.
    const closing = device.context.close();
    device.closed = true;
    closing.catch(error => console.error("AudioContext shutdown failed", error));
}
export function createClip(device, rate, channels, memory) {
    // The .NET span is borrowed only for this call; never retain memory or write into it.
    const buffer = device.context.createBuffer(channels, memory.byteLength / 4 / channels, rate);
    if (channels === 1) {
        const channel = buffer.getChannelData(0);
        memory.copyTo(new Uint8Array(channel.buffer, channel.byteOffset, channel.byteLength));
        return buffer;
    }
    const bytes = new Uint8Array(memory.byteLength);
    memory.copyTo(bytes);
    const samples = new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4);
    for (let c = 0; c < channels; c++) {
        const channel = buffer.getChannelData(c);
        for (let i = 0; i < channel.length; i++) channel[i] = samples[i * channels + c];
    }
    return buffer;
}
export function createVoice(device, clip, initialGain) {
    const gain = device.context.createGain();
    gain.gain.value = initialGain;
    gain.connect(device.context.destination);
    return { device, clip, gain, source: null, offset: 0, start: 0, loop: false, ended: false };
}
function detach(voice) {
    if (voice.source) {
        const onended = voice.source.onended;
        voice.source.onended = null;
        try {
            voice.source.stop();
        } catch (error) {
            if (outputState(voice.device) !== 2) {
                voice.source.onended = onended;
                throw error;
            }
        }
        voice.source.disconnect();
        voice.source = null;
    }
}
export function play(voice) {
    if (voice.source) return;
    if (voice.ended) voice.offset = 0;
    if (voice.loop && voice.offset === voice.clip.length) voice.offset = 0;
    if (voice.offset === voice.clip.length) {
        voice.ended = true;
        return;
    }
    const source = voice.device.context.createBufferSource();
    source.buffer = voice.clip;
    source.loop = voice.loop;
    source.connect(voice.gain);
    source.onended = () => {
        if (voice.source !== source) return;
        source.disconnect();
        voice.source = null;
        voice.offset = voice.clip.length;
        voice.ended = true;
    };
    try {
        source.start(0, voice.offset / voice.clip.sampleRate);
    } catch (error) {
        source.onended = null;
        source.disconnect();
        throw error;
    }
    voice.source = source;
    voice.start = voice.device.context.currentTime;
    voice.ended = false;
}
export function pause(voice) {
    if (!voice.source) return;
    capturePosition(voice);
    detach(voice);
}
function capturePosition(voice) {
    const time = voice.device.context.currentTime;
    voice.offset = positionAt(voice, time);
    voice.start = time;
}
function positionAt(voice, time) {
    const position = voice.offset + (time - voice.start) * voice.clip.sampleRate;
    return voice.loop ? position % voice.clip.length : Math.min(position, voice.clip.length);
}
export function framePosition(voice) {
    return Math.floor(voice.source ? positionAt(voice, voice.device.context.currentTime) : voice.offset);
}
export function seek(voice, framePosition) {
    if (!Number.isSafeInteger(framePosition) || framePosition < 0 || framePosition > voice.clip.length) {
        throw new RangeError("The frame position must be within the clip.");
    }
    const playing = voice.source !== null;
    detach(voice);
    voice.offset = voice.loop && framePosition === voice.clip.length ? 0 : framePosition;
    voice.ended = voice.ended && voice.offset === voice.clip.length;
    if (playing) play(voice);
}
export function stop(voice) {
    detach(voice);
    voice.offset = 0;
    voice.ended = false;
}
export function completed(voice) { return voice.ended; }
export function playing(voice) { return voice.source !== null; }
export function setGain(voice, gain) {
    const param = voice.gain.gain;
    const time = voice.device.context.currentTime;
    if (!voice.source) {
        param.cancelScheduledValues(time);
        param.setValueAtTime(gain, time);
        return;
    }
    param.cancelAndHoldAtTime(time);
    param.linearRampToValueAtTime(gain, time + 0.005);
}
export function setLoop(voice, loop) {
    if (voice.loop === loop) return;
    if (voice.source) {
        capturePosition(voice);
        voice.source.loop = loop;
    }
    voice.loop = loop;
}
export function destroyVoice(voice) {
    stop(voice);
    voice.gain.disconnect();
}
