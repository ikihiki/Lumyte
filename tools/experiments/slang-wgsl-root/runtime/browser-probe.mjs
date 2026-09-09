import { spawn } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

const root = path.dirname(fileURLToPath(import.meta.url));
const workspace = path.resolve(root, '..', '..', '..', '..');
const artifactRoot = path.join(workspace, 'artifacts', 'experiments', 'slang-wgsl-root', 'runtime');
const runName = process.argv[2] ?? 'gpu-final';
if (!/^[A-Za-z0-9_-]+$/.test(runName)) throw new Error('Run name must contain letters, digits, underscores, or hyphens.');
const profile = path.join(artifactRoot, `profile-${runName}-${Date.now()}`);
await mkdir(profile, { recursive: true });
const browserPath = process.argv[3] ?? process.env.LUMYTE_WEBGPU_BROWSER ?? 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const browser = spawn(browserPath, [
  '--headless=new', '--no-first-run', '--no-default-browser-check',
  '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank',
], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
let browserLog = '';
let browserError;
browser.on('error', error => { browserError = error; browserLog += `${error.stack ?? error}\n`; });
browser.stdout.on('data', data => browserLog += data);
browser.stderr.on('data', data => browserLog += data);
let ws;
let pending = new Map();
let nextId = 0;
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
function command(method, params = {}, sessionId) {
  const id = ++nextId;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 45000);
    pending.set(id, { resolve, reject, timer });
    ws.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
  });
}
try {
  let port;
  for (let i = 0; i < 100; i++) {
    if (browserError) throw browserError;
    try { port = Number((await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]); break; }
    catch { await pause(200); }
  }
  if (!port) throw new Error('Edge did not expose its DevTools port.');
  const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
  ws = new WebSocket(version.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
  ws.onmessage = event => {
    const response = JSON.parse(event.data);
    const item = pending.get(response.id);
    if (!item) return;
    clearTimeout(item.timer);
    pending.delete(response.id);
    if (response.error) item.reject(new Error(JSON.stringify(response.error)));
    else item.resolve(response.result);
  };
  const { targetId } = await command('Target.createTarget', { url: 'file:///' + path.join(root, 'probe.html').replaceAll('\\', '/') });
  const { sessionId } = await command('Target.attachToTarget', { targetId, flatten: true });
  const systemInfo = await command('SystemInfo.getInfo');
  await command('Runtime.enable', {}, sessionId);
  await pause(300);
  const manifest = JSON.parse(await readFile(path.join(root, 'shader-manifest.json'), 'utf8'));
  const compilerOutput = process.env.LUMYTE_SLANG_PROBE_OUTPUT;
  const shaderPaths = Object.fromEntries(Object.entries(manifest.shaders).map(([key, entry]) => [key,
    compilerOutput && (key === 'slangInterop' || key === 'slangMixed')
      ? path.resolve(compilerOutput, path.basename(entry.path))
      : path.resolve(root, entry.path),
  ]));
  const shaders = {};
  for (const [key, shaderPath] of Object.entries(shaderPaths)) {
    shaders[key] = await readFile(shaderPath, 'utf8');
    const expectedHash = manifest.shaders[key].sha256;
    const actualHash = createHash('sha256').update(shaders[key]).digest('hex');
    if (expectedHash && expectedHash !== actualHash) throw new Error(`Shader hash mismatch: ${key}`);
  }
  const probe = await readFile(path.join(root, 'probe.js'), 'utf8');
  const result = await command('Runtime.evaluate', { expression: `globalThis.probeShaders=${JSON.stringify(shaders)};\n${probe}`, awaitPromise: true, returnByValue: true }, sessionId);
  const output = { timestamp: new Date().toISOString(), node: process.version, browser: version.Browser, compiler: manifest.compiler,
    gpuDevices: systemInfo.gpu.devices, gpuRenderer: systemInfo.gpu.auxAttributes?.glRenderer,
    shaders: Object.entries(shaderPaths).map(([key, shaderPath]) => ({ key, path: shaderPath, sha256: createHash('sha256').update(shaders[key]).digest('hex') })), result };
  await writeFile(path.join(artifactRoot, `${runName}.json`), JSON.stringify(output, null, 2) + '\n');
  console.log(JSON.stringify(output, null, 2));
  if (result.result?.value?.passed !== true || result.exceptionDetails) process.exitCode = 1;
} finally {
  if (ws?.readyState === 1) {
    try { await command('Browser.close'); } catch {}
    ws.close();
  }
  await writeFile(path.join(artifactRoot, `${runName}.browser.log`), browserLog);
  if (browser.pid && browser.exitCode === null) browser.kill();
}
