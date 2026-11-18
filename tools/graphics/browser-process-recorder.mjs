// Passive subset of the historical51b8 recorder: same proc-stat/owned-tree design,
// without perf, spawning, signals, argv/environment inspection or native attachments.
import { open, opendir, readlink } from "node:fs/promises";

async function readBounded(path, limit = 4096) {
    const file = await open(path, "r");
    try {
        const bytes = Buffer.alloc(limit + 1);
        const { bytesRead } = await file.read(bytes, 0, bytes.length, 0);
        if (bytesRead > limit) throw new Error("oversized-proc");
        return bytes.subarray(0, bytesRead).toString("utf8");
    } finally { await file.close(); }
}
async function listBounded(path, limit = 128) {
    const entries = [], directory = await opendir(path);
    try { for await (const entry of directory) { entries.push(entry.name); if (entries.length >= limit) break; } }
    finally { await directory.close().catch(() => {}); }
    return entries;
}
export function parseProcessStat(text) {
    const end = text.lastIndexOf(")"), fields = text.slice(end + 2).trim().split(/\s+/);
    if (end < 0 || fields.length < 22 || !/^\d+$/.test(fields[19])) throw new Error("invalid-stat");
    return { state: fields[0], ppid: Number(fields[1]), userTicks: fields[11], systemTicks: fields[12], threads: Number(fields[17]), startTicks: fields[19], rssPages: fields[21] };
}
const safely = action => { try { return action(); } catch {} };
export function createBrowserProcessRecorder({ browserPid, platform = process.platform, uid = process.getuid?.(),
    read = readBounded, list = listBounded, link = readlink, now = () => performance.now(),
    schedule = setInterval, unschedule = clearInterval } = {}) {
    let root, pending, starting, timer, ended = false, unavailable, cgroup, beforeMemory;
    let dropped = 0, unavailableReads = 0, sampleBytes = 0, overlapping = 0, processRowsDropped = 0, discoveryCapped = false;
    const samples = [], events = [];
    const at = () => safely(now) ?? 0;
    async function text(path, limit = 4096) {
        const value = await read(path, limit);
        if (typeof value !== "string" || Buffer.byteLength(value) > limit) throw new Error("oversized-proc");
        return value;
    }
    async function identity(pid) {
        const base = '/proc/' + pid, first = parseProcessStat(await text(base + '/stat'));
        const status = await text(base + '/status', 16384), ids = /^Uid:\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)/m.exec(status);
        if (!ids || ids.slice(1).some(value => Number(value) !== uid)) throw new Error("foreign-uid");
        const exe = await link(base + '/exe');
        if (typeof exe !== "string" || exe.length > 1024) throw new Error("invalid-executable");
        const second = parseProcessStat(await text(base + '/stat'));
        if (first.startTicks !== second.startTicks || first.ppid !== second.ppid) throw new Error("pid-reused");
        return { pid, uid, exe, ...second, vmHwmKiB: /^VmHWM:\s+(\d+)/m.exec(status)?.[1] ?? null };
    }
    async function still(item) {
        const current = await identity(item.pid);
        if (current.startTicks !== item.startTicks || current.exe !== item.exe || current.uid !== item.uid || current.ppid !== item.ppid) throw new Error("identity-changed");
        return current;
    }
    async function rows() {
        await still(root);
        const queue = [root], candidates = [], seen = new Set([root.pid]);
        for (let index = 0; index < queue.length && index < 64; index++) {
            const parent = queue[index];
            try {
                await still(parent);
                const tids = (await list('/proc/' + parent.pid + '/task', 128)).slice(0, 128);
                for (const tid of tids) {
                    if (!/^\d+$/.test(String(tid))) continue;
                    let children;
                    try { children = await text('/proc/' + parent.pid + '/task/' + tid + '/children'); } catch { unavailableReads++; continue; }
                    for (const value of children.trim().split(/\s+/)) {
                        const pid = Number(value);
                        if (!Number.isSafeInteger(pid) || pid <= 0 || seen.has(pid) || seen.size >= 64) continue;
                        seen.add(pid);
                        try {
                            const item = await identity(pid);
                            await still(parent);
                            if (item.ppid !== parent.pid || item.exe !== root.exe) continue;
                            queue.push(item);
                        } catch { unavailableReads++; }
                    }
                }
                const comm = (await text('/proc/' + parent.pid + '/comm', 128).catch(() => "")).trim();
                candidates.push({ ...parent, role: parent.pid === browserPid ? "browser-root" : /gpu/i.test(comm) ? "gpu-thread-observed" : "owned-chrome-role-unknown" });
            } catch { unavailableReads++; }
        }
        await still(root);
        processRowsDropped += Math.max(0, candidates.length - 8);
        discoveryCapped ||= seen.size >= 64;
        const selected = candidates.sort((a, b) => (a.pid === browserPid ? -2 : a.role === "gpu-thread-observed" ? -1 : 0) - (b.pid === browserPid ? -2 : b.role === "gpu-thread-observed" ? -1 : 0)).slice(0, 8);
        const result = [];
        for (const parent of selected) {
            try {
                await still(parent);
                const threads = [];
                for (const tid of (await list('/proc/' + parent.pid + '/task', 32)).slice(0, 32)) {
                    if (!/^\d+$/.test(String(tid))) continue;
                    try {
                        const base = '/proc/' + parent.pid + '/task/' + tid;
                        const stat = parseProcessStat(await text(base + '/stat'));
                        const comm = (await text(base + '/comm', 128)).trim().slice(0, 96);
                        const sched = (await text(base + '/schedstat', 256)).trim().split(/\s+/);
                        threads.push({ tid: Number(tid), startTicks: stat.startTicks, state: stat.state, name: comm, userTicks: stat.userTicks, systemTicks: stat.systemTicks,
                            runtimeNs: /^\d+$/.test(sched[0]) ? sched[0] : null, runqueueWaitNs: /^\d+$/.test(sched[1]) ? sched[1] : null });
                    } catch { unavailableReads++; }
                }
                result.push({ ...await still(parent), role: parent.role, threadsDropped: Math.max(0, parent.threads - threads.length), threadSamples: threads });
            } catch { unavailableReads++; }
        }
        await still(root);
        return result;
    }
    async function sampleInner() {
        try {
            const processes = await rows();
            const value = { atMs: at(), processes }, bytes = Buffer.byteLength(JSON.stringify(value));
            samples.push({ value, bytes }); sampleBytes += bytes;
            while (samples.length > 120 || sampleBytes > 160 * 1024) { sampleBytes -= samples.shift().bytes; dropped++; }
        } catch { unavailable = "owned-proc-unavailable-or-identity-changed"; }
    }
    function sample() {
        if (ended || !root) return Promise.resolve();
        if (pending) { overlapping++; return pending; }
        pending = sampleInner().finally(() => { pending = undefined; });
        return pending;
    }
    async function memory() {
        if (cgroup === undefined) return undefined;
        try {
            const entries = (await text('/sys/fs/cgroup' + cgroup + '/memory.events')).trim().split('\n');
            return Object.fromEntries(entries.map(row => row.trim().split(/\s+/)).filter(([key, value]) => /^(low|high|max|oom|oom_kill|oom_group_kill)$/.test(key) && /^\d+$/.test(value)));
        } catch { unavailableReads++; }
    }
    async function startInner() {
        if (root || ended) return;
        if (platform !== "linux" || !Number.isSafeInteger(browserPid) || browserPid <= 0 || !Number.isSafeInteger(uid)) { unavailable = "unsupported-platform-or-identity"; return; }
        try {
            root = await identity(browserPid);
            let match;
            try { match = /^0::([^\n]*)$/m.exec(await text('/proc/' + browserPid + '/cgroup')); } catch { unavailableReads++; }
            if (match && /^\/(?:[a-zA-Z0-9_.@-]+\/?)*$/.test(match[1]) && !match[1].split('/').includes('..')) cgroup = match[1] === '/' ? '' : match[1];
            beforeMemory = await memory();
            await sample();
            if (!ended) { timer = schedule(() => { void sample(); }, 1000); timer?.unref?.(); }
        } catch { unavailable = "browser-proc-unavailable"; }
    }
    function start() {
        if (!starting) starting = startInner();
        return starting;
    }
    function event(kind, data = {}) {
        const record = { kind, atMs: at() };
        for (const key of ["code", "signal", "errorCode"]) if (typeof data[key] === "number" || data[key] === null || typeof data[key] === "string") record[key] = typeof data[key] === "string" ? data[key].slice(0, 80) : data[key];
        events.push(record); if (events.length > 16) events.shift();
    }
    function snapshot() {
        return { version: 1, browserPid, root, unavailable, dropped, unavailableReads, overlapping, processRowsDropped, discoveryCapped, samples: samples.map(row => row.value), events: [...events],
            memory: { before: beforeMemory, sharedCgroup: true },
            limitations: "Passive owned Chrome CPU/RSS/thread observations, not GPU timestamps or shader stacks. Role inferred only from bounded thread names; no argv. GPU child exit/cause unknown unless separately reported. Shared-cgroup counters do not attribute OOM. Logging/sampling perturb scheduling." };
    }
    async function stop() { ended = true; if (timer !== undefined) unschedule(timer); timer = undefined; await starting; await pending; const value = snapshot(); value.memory.after = await memory(); return value; }
    return { start, sample, snapshot, stop, event };
}
export function createBoundedBrowserStderr() {
    let head = Buffer.alloc(0), tail = Buffer.alloc(0), total = 0;
    return {
        observe(data) { const bytes = Buffer.isBuffer(data) ? data : Buffer.from(String(data)); total += bytes.length;
            if (head.length < 16384) head = Buffer.concat([head, bytes.subarray(0, 16384 - head.length)]);
            tail = bytes.length >= 65536 ? Buffer.from(bytes.subarray(bytes.length - 65536)) : Buffer.concat([tail, bytes]).subarray(-65536);
        },
        snapshot() { return { head: head.toString('utf8'), tail: tail.toString('utf8'), totalBytes: total, droppedBytes: Math.max(0, total - head.length - tail.length) }; },
    };
}
export function emitBrowserNative(emit, value) {
    try {
        // Escaped stderr can expand JSON. Prefer losing old samples to exceeding the log cap.
        const report = structuredClone(value); let droppedForOutput = 0;
        while (Buffer.byteLength(JSON.stringify(report)) > 256 * 1024 && report.process?.samples?.length) { report.process.samples.shift(); droppedForOutput++; }
        report.droppedForOutput = droppedForOutput;
        if (Buffer.byteLength(JSON.stringify(report)) > 256 * 1024) { report.stderr = { totalBytes: report.stderr?.totalBytes, droppedBytes: report.stderr?.totalBytes, unavailable: "output-cap" }; }
        if (Buffer.byteLength(JSON.stringify(report)) <= 256 * 1024) emit(report);
    } catch { /* Diagnostics never replace the original application failure. */ }
}
