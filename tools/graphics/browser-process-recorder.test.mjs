import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createBrowserProcessRecorder, createBoundedBrowserStderr, emitBrowserNative, parseProcessStat } from "./browser-process-recorder.mjs";

function stat(pid, ppid, start = String(pid), threads = 1) {
    const fields = Array(22).fill("0");
    Object.assign(fields, { 0: "S", 1: String(ppid), 11: "123", 12: "4", 17: String(threads), 19: start, 21: "25" });
    return pid + " (name with ) parentheses) " + fields.join(" ");
}
function fixture({ children = [200], foreign = [], platform = "linux" } = {}) {
    const paths = [], calls = [], state = { rootStart: "100", fail: false, pause: null, memory: 0 };
    let timer, tick = 0, canceled = 0;
    const metadata = pid => ({ ppid: pid === 100 ? 1 : pid === 999 ? 1 : 100, start: pid === 100 ? state.rootStart : String(pid) });
    async function read(path) {
        paths.push(path);
        if (state.pause) await state.pause;
        if (state.fail) throw new Error("permission denied");
        if (path === "/sys/fs/cgroup/test/memory.events") return "oom " + state.memory + "\noom_kill 0\nsecret not-a-counter\n";
        const pid = Number(path.split('/')[2]), item = metadata(pid);
        if (path.endsWith('/children')) return pid === 100 ? children.join(' ') : '';
        if (path.endsWith('/stat')) return stat(pid, item.ppid, item.start);
        if (path.endsWith('/status')) return "Uid:\t" + Array(4).fill(foreign.includes(pid) ? 43 : 42).join('\t') + "\nVmHWM:\t100 kB\n";
        if (path.endsWith('/cgroup')) return "0::/test\n";
        if (path.endsWith('/comm')) return pid === 200 ? "CrGpuMain\n" : "chrome\n";
        if (path.endsWith('/schedstat')) return "10 20 3\n";
        throw new Error("unexpected path " + path);
    }
    const recorder = createBrowserProcessRecorder({ browserPid: 100, uid: 42, platform, read,
        link: async path => { paths.push(path); return "/owned/chrome"; },
        list: async (path, limit) => { calls.push({ path, limit }); return [path.split('/')[2]]; }, now: () => ++tick,
        schedule: (callback, ms) => { assert.equal(ms, 1000); timer = callback; return { unref() {} }; }, unschedule: () => canceled++ });
    return { recorder, paths, calls, state, fire: () => timer?.(), canceled: () => canceled };
}

test("stat parser preserves64bit ticks and handles parenthesized names", () => {
    const parsed = parseProcessStat(stat(100, 1, "999999999999999999"));
    assert.equal(parsed.startTicks, "999999999999999999"); assert.equal(parsed.userTicks, "123");
    assert.throws(() => parseProcessStat("invalid"));
});
test("only same-UID/executable descendants; no argv, environment or broad system scan", async () => {
    const f = fixture({ children: [300, 999, 200], foreign: [300] }); await f.recorder.start();
    const result = await f.recorder.stop();
    assert.deepEqual(result.samples[0].processes.map(row => row.pid), [100, 200]);
    assert.equal(result.samples[0].processes[1].role, "gpu-thread-observed");
    assert.ok(f.paths.every(path => !/cmdline|environ|\/fd|maps|cookie|journal/.test(path)));
    assert.equal(result.root.uid, 42); assert.equal(f.canceled(), 1);
});
test("reused root PID halts sampling and does not follow its new descendants", async () => {
    const f = fixture(); await f.recorder.start(); const before = f.recorder.snapshot().samples.length;
    f.state.rootStart = "reused"; await f.recorder.sample();
    assert.equal(f.recorder.snapshot().samples.length, before);
    assert.equal(f.recorder.snapshot().unavailable, "owned-proc-unavailable-or-identity-changed"); await f.recorder.stop();
});
test("nonLinux and deniedproc stay explicitly unavailable without launching anything", async () => {
    const a = fixture({ platform: "darwin" }); await a.recorder.start(); assert.equal(a.paths.length, 0); assert.match((await a.recorder.stop()).unavailable, /unsupported/);
    const b = fixture(); b.state.fail = true; await b.recorder.start(); assert.match((await b.recorder.stop()).unavailable, /browser-proc-unavailable/);
});
test("sample serialization and stop do not overlap or leave a scheduled reader", async () => {
    const f = fixture(); await f.recorder.start(); let release;
    f.state.pause = new Promise(resolve => { release = resolve; });
    const a = f.recorder.sample(), b = f.recorder.sample(); assert.equal(a, b);
    const stopped = f.recorder.stop(); release(); await stopped;
    const count = f.paths.length; f.fire(); await f.recorder.sample(); assert.equal(f.paths.length, count); assert.ok(f.recorder.snapshot().overlapping > 0);
});
test("sample tail, event ring, process slots and GPU metadata priority are bounded", async () => {
    const f = fixture({ children: Array.from({ length: 15 }, (_, i) => 200 + i) }); await f.recorder.start();
    for (let i = 0; i < 125; i++) await f.recorder.sample();
    for (let i = 0; i < 30; i++) f.recorder.event("test", { code: i });
    const result = await f.recorder.stop(); assert.ok(result.samples.length <= 120); assert.ok(result.dropped > 0); assert.equal(result.events.length, 16);
    assert.ok(result.samples.every(row => row.processes.length <= 8 && row.processes.every(p => p.threadSamples.length <= 32)));
    assert.ok(result.samples.at(-1).processes.some(row => row.pid === 200)); assert.ok(result.processRowsDropped > 0);
});
test("shared cgroup before/after counters are labelled and not attributed to a browser kill", async () => {
    const f = fixture(); await f.recorder.start(); f.state.memory = 1; const result = await f.recorder.stop();
    assert.equal(result.memory.before.oom, "0"); assert.equal(result.memory.after.oom, "1"); assert.equal(result.memory.sharedCgroup, true);
    assert.deepEqual(Object.keys(result.memory.after).sort(), ["oom", "oom_kill"]);
});
test("root failure exit and later cleanup signals are separately timestamped, never inferred childexit", async () => {
    const f = fixture(); f.recorder.event("root-exit", { code: 9 }); f.recorder.event("failure-observed"); f.recorder.event("cleanup-signal", { signal: "SIGTERM" });
    const events = f.recorder.snapshot().events; assert.deepEqual(events.map(row => row.kind), ["root-exit", "failure-observed", "cleanup-signal"]);
    assert.ok(events[0].atMs < events[1].atMs && events[1].atMs < events[2].atMs);
});
test("native stderr has bounded head/tail/total and safe summary cap even for JSON escaping", () => {
    const ring = createBoundedBrowserStderr(); ring.observe(Buffer.alloc(1024 * 1024, 1)); const stderr = ring.snapshot();
    assert.equal(Buffer.byteLength(stderr.head), 16384); assert.equal(Buffer.byteLength(stderr.tail), 65536); assert.equal(stderr.totalBytes, 1024 * 1024); assert.ok(stderr.droppedBytes > 0);
    let value; emitBrowserNative(report => value = report, { stderr, process: { samples: Array.from({ length: 120 }, () => ({ text: 'x'.repeat(5000) })) } });
    assert.ok(Buffer.byteLength(JSON.stringify(value)) <= 256 * 1024); assert.ok(value.droppedForOutput > 0); assert.equal(value.stderr.unavailable, "output-cap");
    assert.doesNotThrow(() => emitBrowserNative(() => { throw new Error("reporter failure"); }, { stderr }));
});
test("verifier keeps launch/quality/deadline path and does not await proc reads before original cleanup", async () => {
    const source = await readFile(new URL('../../samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs', import.meta.url), 'utf8');
    assert.ok(source.indexOf('nativeReport("before-cleanup-failure"') < source.indexOf('browser.kill("SIGTERM")'));
    assert.ok(source.indexOf('browser.kill("SIGTERM")') < source.indexOf('await nativeStopping'));
    assert.match(source, /const timeout = setTimeout\([^\n]+2000\)/);
    assert.match(source, /Buffer.from\(await evaluate\(.*CapturePngAsync/);
    assert.ok(!source.includes('process.kill('));
});
