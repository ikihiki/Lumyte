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
            const voice = audio.createVoice(device, clip);
            audio.setGain(voice, 0.3);
            audio.play(voice);
            const rendered = await context.startRendering();
            const left = rendered.getChannelData(0)[1000];
            const right = rendered.getChannelData(1)[1000];
            if (Math.abs(left - 0.15) > 0.00001 || Math.abs(right + 0.075) > 0.00001)
                throw new Error(`Unexpected PCM output ${left}, ${right}`);
            // A stopped node's delayed callback must not complete a restarted voice.
            const live = { context: new AudioContext(), closed: false };
            await live.context.resume();
            const liveClip = audio.createClip(live, 48000, 2,
                { byteLength: bytes.length, copyTo: target => target.set(bytes) });
            const active = audio.createVoice(live, liveClip);
            audio.play(active);
            const oldHandler = active.source.onended;
            audio.stop(active);
            audio.play(active);
            oldHandler();
            if (audio.completed(active)) throw new Error('Stale completion changed new playback');
            audio.destroyVoice(active);
            await live.context.close();
            return { left, right, staleCompletionRejected: true };
        }''')
        assert not errors, errors
        print(json.dumps(rendering))
        browser.close()
finally:
    server.shutdown()
    server.server_close()
