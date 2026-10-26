"""Verify the published .NET sample and real Web Audio rendering in Chromium."""
import argparse
import functools
import http.server
import json
import threading
from pathlib import Path

from playwright.sync_api import sync_playwright

parser = argparse.ArgumentParser()
parser.add_argument('wwwroot', type=Path)
parser.add_argument('--chromium', default=None)
args = parser.parse_args()
handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=str(args.wwwroot))
server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), handler)
thread = threading.Thread(target=server.serve_forever, daemon=True)
thread.start()
try:
    with sync_playwright() as playwright:
        options = {'headless': True}
        if args.chromium:
            options['executable_path'] = args.chromium
        browser = playwright.chromium.launch(**options)
        page = browser.new_page()
        errors = []
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.goto(f'http://127.0.0.1:{server.server_port}/')
        page.locator('#start').click(timeout=60000)
        page.wait_for_function("document.querySelector('#result').dataset.result", timeout=60000)
        result = page.locator('#result')
        assert result.get_attribute('data-result') == 'passed', result.inner_text()
        print(result.inner_text())
        rendering = page.evaluate('''async () => {
            const audio = await import('./lumyte-audio.js');
            const context = new OfflineAudioContext(2, 4800, 48000);
            const device = { context, closed: false };
            const samples = new Float32Array(9600);
            for (let i = 0; i < 4800; i++) {
                samples[2 * i] = 0.5;
                samples[2 * i + 1] = -0.25;
            }
            const bytes = new Uint8Array(samples.buffer);
            const clip = audio.createClip(device, 48000, 2,
                { byteLength: bytes.length, copyTo: target => target.set(bytes) });
            const voice = audio.createVoice(device, clip, 1);
            audio.play(voice);
            audio.setGain(voice, 0.3);
            const rendered = await context.startRendering();
            const left = rendered.getChannelData(0)[1000];
            const right = rendered.getChannelData(1)[1000];
            if (Math.abs(left - 0.15) > 0.00001 || Math.abs(right + 0.075) > 0.00001)
                throw new Error(`Unexpected PCM output ${left}, ${right}`);
            // Muted buses and gains set before Play must be silent from the first frame.
            async function renderMuted(initialGain, changeBeforePlay) {
                const context = new OfflineAudioContext(1, 480, 48000);
                const clip = context.createBuffer(1, 480, 48000);
                clip.getChannelData(0).fill(0.5);
                const voice = audio.createVoice({ context, closed: false }, clip, initialGain);
                if (changeBeforePlay) audio.setGain(voice, 0);
                audio.play(voice);
                const rendered = await context.startRendering();
                const peak = rendered.getChannelData(0).reduce((peak, value) => Math.max(peak, Math.abs(value)), 0);
                if (peak !== 0) throw new Error(`Muted voice leaked PCM: ${peak}`);
                audio.destroyVoice(voice);
                return peak;
            }
            const mutedAtCreation = await renderMuted(0, false);
            const mutedBeforePlay = await renderMuted(1, true);

            // Verify actual PCM after seeking, including clips with a different sample rate.
            async function renderSeek(mode, sampleRate) {
                const context = new OfflineAudioContext(1, 8192, 48000);
                const clip = context.createBuffer(1, 20000, sampleRate);
                const samples = clip.getChannelData(0);
                for (let i = 0; i < samples.length; i++) samples[i] = (i + 1) / 24000;
                const voice = audio.createVoice({ context, closed: false }, clip, 1);
                const targetFrame = 6000;
                let resumeFrame = 0;
                let rendering;
                if (mode === 'stopped') {
                    audio.seek(voice, targetFrame);
                    if (voice.source || audio.framePosition(voice) !== targetFrame)
                        throw new Error('Seeking a stopped voice started playback or lost its position');
                    audio.play(voice);
                    rendering = context.startRendering();
                } else {
                    audio.play(voice);
                    const suspended = context.suspend(1024 / 48000);
                    rendering = context.startRendering();
                    await suspended;
                    resumeFrame = Math.round(context.currentTime * 48000);
                    const oldHandler = voice.source.onended;
                    if (mode === 'paused') audio.pause(voice);
                    audio.seek(voice, targetFrame);
                    if (audio.framePosition(voice) !== targetFrame)
                        throw new Error(`Seek position is not measured in clip frames: ${mode}, ${sampleRate}`);
                    if (mode === 'paused') {
                        if (voice.source) throw new Error('Seeking a paused voice resumed playback');
                        audio.play(voice);
                    }
                    oldHandler();
                    if (audio.completed(voice)) throw new Error('Pre-seek completion changed new playback');
                    await context.resume();
                }

                const output = (await rendering).getChannelData(0);
                for (const delta of [256, 512, 2048]) {
                    const expected = samples[targetFrame + delta * sampleRate / 48000];
                    const actual = output[resumeFrame + delta];
                    if (Math.abs(actual - expected) > 0.00001)
                        throw new Error(`Seek rendered wrong PCM (${mode}, ${sampleRate} Hz, ${delta}): ${actual}, expected ${expected}`);
                }
                const expectedPosition = targetFrame + (output.length - resumeFrame) * sampleRate / 48000;
                if (Math.abs(audio.framePosition(voice) - expectedPosition) > 1)
                    throw new Error(`Seek cursor advanced at the wrong sample rate: ${mode}, ${sampleRate}`);
                audio.destroyVoice(voice);
                return { mode, sampleRate, targetFrame, resumeFrame };
            }
            const seeks = [];
            for (const rate of [48000, 24000]) {
                for (const mode of ['stopped', 'paused', 'playing']) seeks.push(await renderSeek(mode, rate));
            }

            // Disabling looping after multiple iterations must retain the current clip position.
            const loopContext = new OfflineAudioContext(1, 48000, 48000);
            const loopClip = loopContext.createBuffer(1, 4800, 48000);
            const loopSamples = loopClip.getChannelData(0);
            for (let i = 0; i < loopSamples.length; i++) loopSamples[i] = (i + 1) / 9600;
            const looping = audio.createVoice({ context: loopContext, closed: false }, loopClip, 1);
            audio.setLoop(looping, true);
            audio.play(looping);
            const suspended = loopContext.suspend(0.25);
            const loopRendering = loopContext.startRendering();
            await suspended;
            const resumeFrame = Math.round(loopContext.currentTime * 48000);
            const clipFrame = resumeFrame % loopSamples.length;
            audio.setLoop(looping, false);
            audio.pause(looping);
            audio.play(looping);
            await loopContext.resume();
            const loopResult = (await loopRendering).getChannelData(0);
            for (const delta of [0, 128, 512, 2000]) {
                const expected = loopSamples[clipFrame + delta];
                const actual = loopResult[resumeFrame + delta];
                if (Math.abs(actual - expected) > 0.00001)
                    throw new Error(`Loop resume skipped PCM at ${delta}: ${actual}, expected ${expected}`);
            }
            if (loopResult[resumeFrame + loopSamples.length - clipFrame + 128] !== 0 || !audio.completed(looping))
                throw new Error('Loop-disabled voice did not complete at the clip boundary');
            audio.destroyVoice(looping);
            // A stopped node's delayed callback must not complete a restarted voice.
            const live = { context: new AudioContext(), closed: false };
            await live.context.resume();
            const liveClip = audio.createClip(live, 48000, 2,
                { byteLength: bytes.length, copyTo: target => target.set(bytes) });
            const active = audio.createVoice(live, liveClip, 1);
            audio.play(active);
            const oldHandler = active.source.onended;
            audio.stop(active);
            audio.play(active);
            oldHandler();
            if (audio.completed(active)) throw new Error('Stale completion changed new playback');
            audio.destroyVoice(active);
            await live.context.close();
            return { left, right, mutedAtCreation, mutedBeforePlay, seeks, loopResumeFrame: clipFrame, staleCompletionRejected: true };
        }''')
        assert not errors, errors
        print(json.dumps(rendering))
        browser.close()
finally:
    server.shutdown()
    server.server_close()
