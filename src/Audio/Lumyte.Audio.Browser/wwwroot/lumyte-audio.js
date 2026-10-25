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
    // close() initiates shutdown synchronously; completion is owned by the context.
    const closing = device.context.close();
    device.closed = true;
    closing.catch(error => console.error("AudioContext shutdown failed", error));
}
export function createClip(device, rate, channels, memory) {
    const bytes = new Uint8Array(memory.byteLength);
    memory.copyTo(bytes);
    const samples = new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4);
    const buffer = device.context.createBuffer(channels, samples.length / channels, rate);
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
        voice.source.onended = null;
        voice.source.stop();
        voice.source.disconnect();
        voice.source = null;
    }
}
export function play(voice) {
    if (voice.source) return;
    if (voice.ended) voice.offset = 0;
    const source = voice.device.context.createBufferSource();
    source.buffer = voice.clip;
    source.loop = voice.loop;
    source.connect(voice.gain);
    source.onended = () => {
        if (voice.source !== source) return;
        source.disconnect();
        voice.source = null;
        voice.offset = 0;
        voice.ended = true;
    };
    source.start(0, voice.offset);
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
    const position = voice.offset + time - voice.start;
    voice.offset = voice.loop ? position % voice.clip.duration : Math.min(position, voice.clip.duration);
    voice.start = time;
}
export function stop(voice) {
    detach(voice);
    voice.offset = 0;
    voice.ended = false;
}
export function completed(voice) { return voice.ended; }
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
