import { readFile, readdir, readlink, mkdtemp, mkdir, writeFile, stat, copyFile, rm, chmod, truncate } from "node:fs/promises";
import { spawn, spawnSync } from "node:child_process";
import { tmpdir } from "node:os";
import { join } from "node:path";

export function parseProcessStat(text) {
    const end = text.lastIndexOf(")");
    if (end < 0) throw new Error("invalid-stat");
    const fields = text.slice(end + 2).trim().split(/\s+/);
    return { state: fields[0], ppid: Number(fields[1]), userTicks: Number(fields[11]), systemTicks: Number(fields[12]), threads: Number(fields[17]), startTicks: fields[19] };
}
export function createBrowserProcessRecorder({ browserPid, environment = process.env, platform = process.platform,
    read = readFile, list = readdir, link = readlink, launch = spawn, now = () => performance.now(),
    intervalMs = 1000, maxSamples = 120, maxProcesses = 8, maxThreads = 16,
    signalGroup = (pid, signal) => process.kill(-pid, signal), stopGraceMs = 1500 } = {}) {
    let timer, pending, ended = false, rootIdentity, executable, spool, perf, perfDone, perfStopped = false, nativeAttempted = false;
    let unavailable, dropped = 0, nativeStatus = "not-started", nativeError = "", nativeBytes = 0;
    const samples = [], identities = new Map();
    const clocks = platform === "linux" ? spawnSync("getconf", ["CLK_TCK"], { encoding: "utf8", timeout: 500 }) : {};
    const clockTicks = Number(clocks.stdout?.trim()) || null;
    const boundedRead = async (path, limit = 65536) => { const value = await read(path, "utf8"); if (value.length > limit) throw new Error("oversized-proc"); return value; };
    const processStat = async pid => parseProcessStat(await boundedRead('/proc/' + pid + '/stat', 4096));
    async function ownedProcesses() {
        const root = await processStat(browserPid);
        if (root.startTicks !== rootIdentity) throw new Error("browser-exited-or-pid-reused");
        const queue = [browserPid], seen = new Set(queue), result = [];
        while (queue.length && seen.size <= 64) {
            const parent = queue.shift();
            const tids = (await list('/proc/' + parent + '/task').catch(() => [])).slice(0, 128);
            for (const tid of tids) {
                const children = await boundedRead('/proc/' + parent + '/task/' + tid + '/children', 4096).catch(() => "");
                for (const raw of children.trim().split(/\s+/)) {
                    const pid = Number(raw);
                    if (!Number.isSafeInteger(pid) || pid <= 0 || seen.has(pid) || seen.size >= 64) continue;
                    seen.add(pid);
                    const info = await processStat(pid).catch(() => null);
                    if (!info || info.ppid !== parent) continue;
                    queue.push(pid);
                    const path = await link('/proc/' + pid + '/exe').catch(() => "");
                    if (path !== executable) continue;
                    const args = await boundedRead('/proc/' + pid + '/cmdline', 16384).catch(() => "");
                    // Chrome may rewrite argv into one space-separated process title.
                    const type = /(?:^|[\0\s])--type=(gpu-process|renderer)(?=[\0\s]|$)/.exec(args)?.[1];
                    if (type && result.length < maxProcesses) result.push({ pid, type, ...info });
                }
            }
        }
        return result;
    }
    async function stopPerf() {
        if (!perf || perfStopped) return;
        perfStopped = true;
        const signal = value => {
            try { if (Number.isSafeInteger(perf.pid) && perf.pid > 0) signalGroup(perf.pid, value); else perf.kill(value); } catch {}
        };
        signal("SIGINT");
        let timeout;
        await Promise.race([perfDone, new Promise(resolve => { timeout = setTimeout(resolve, stopGraceMs); })]);
        clearTimeout(timeout);
        // Kill the whole owned group even if timeout exited first: perf may remain.
        signal("SIGKILL");
        await Promise.race([perfDone, new Promise(resolve => { const t = setTimeout(resolve, 250); t.unref?.(); })]);
    }
    async function beginPerf(item) {
        if (nativeAttempted || ended) return;
        nativeAttempted = true;
        const binary = environment.LUMYTE_BROWSER_PERF;
        if (!binary) { nativeStatus = "unavailable-no-configured-tool"; return; }
        const current = await processStat(item.pid);
        if (current.startTicks !== item.startTicks) { nativeStatus = "unavailable-pid-reused"; return; }
        spool = await mkdtemp(join(tmpdir(), "lumyte-owned-gpu-perf-"));
        await chmod(spool, 0o700);
        nativeStatus = "recording";
        perf = launch("timeout", ["--signal=INT", "--kill-after=2s", "300s", binary, "record", "-e", "cpu-clock:u", "-F", "49", "--call-graph", "dwarf,8192", "-m", "256", "--timestamp", "--clockid", "mono", "--no-buildid-cache", "--switch-output=8M", "--switch-max-files=2", "-o", join(spool, "gpu.perf.data"), "-p", String(item.pid)], { detached: true, stdio: ["ignore", "ignore", "pipe"] });
        perf.stderr?.on("data", data => { if (nativeError.length < 4096) nativeError = (nativeError + data.toString()).slice(0, 4096); });
        perfDone = new Promise(resolve => {
            perf.once("error", () => { nativeStatus = "unavailable-launch-error"; resolve(); });
            perf.once("exit", (code, signal) => {
                const stoppedByOwner = perfStopped && (signal === "SIGINT" || signal === "SIGKILL" || code === 130);
                nativeStatus = code === 0 || stoppedByOwner ? "stopped" : "unavailable-permission-or-recording-error";
                resolve();
            });
        });
    }
    async function sample() {
        if (ended) return;
        try {
            const processes = await ownedProcesses();
            const rows = [];
            for (const item of processes) {
                const identity = item.pid + ":" + item.startTicks;
                identities.set(identity, { pid: item.pid, startTicks: item.startTicks, type: item.type });
                while (identities.size > 32) identities.delete(identities.keys().next().value);
                const sched = (await boundedRead('/proc/' + item.pid + '/schedstat', 256).catch(() => "")).trim().split(/\s+/);
                const threads = [];
                for (const tid of (await list('/proc/' + item.pid + '/task').catch(() => [])).slice(0, maxThreads)) {
                    const t = await boundedRead('/proc/' + item.pid + '/task/' + tid + '/schedstat', 256).catch(() => "");
                    const s = t.trim().split(/\s+/);
                    if (s.length >= 2) threads.push({ tid: Number(tid), runtimeNs: s[0], runqueueWaitNs: s[1] });
                }
                rows.push({ ...item, runtimeNs: sched[0] || null, runqueueWaitNs: sched[1] || null, threadSamples: threads });
                if (item.type === "gpu-process") await beginPerf(item);
            }
            samples.push({ atMs: now(), processes: rows });
            if (samples.length > maxSamples) { samples.shift(); dropped++; }
            if (perf && !perfStopped) {
                const files = (await readdir(spool)).filter(x => /^gpu\.perf\.data(?:[.-][0-9]+)?$/.test(x));
                const sizes = await Promise.all(files.map(x => stat(join(spool, x))));
                if (sizes.reduce((sum, x) => sum + x.size, 0) > 64 * 1024 * 1024) { nativeStatus = "size-watchdog"; await stopPerf(); }
            }
        } catch (error) { unavailable = error.message === "browser-exited-or-pid-reused" ? error.message : "proc-unavailable"; }
    }
    async function start() {
        if (timer || rootIdentity || ended) return;
        if (platform !== "linux" || !Number.isSafeInteger(browserPid) || browserPid <= 0) { unavailable = "unsupported-platform-or-pid"; return; }
        try { rootIdentity = (await processStat(browserPid)).startTicks; executable = await link('/proc/' + browserPid + '/exe'); }
        catch { unavailable = "browser-proc-unavailable"; return; }
        await sample();
        timer = setInterval(() => { if (!pending) pending = sample().finally(() => { pending = undefined; }); }, intervalMs);
        timer.unref?.();
    }
    async function finish({ retain = false, directory } = {}) {
        ended = true; clearInterval(timer); timer = undefined;
        await pending; await stopPerf();
        const metadata = { version: 1, browserPid, rootIdentity, clockTicks, unavailable, dropped, samples, identities: [...identities.values()],
            nativeStatus, nativeError, nativeBytes: 0,
            limitations: "Process CPU ticks and scheduler time are not hardware GPU timestamps. Optional perf has no privilege escalation; denied attachment is unavailable, not a captured profile. Stripped/JIT addresses may not distinguish shader compilation from execution. Native segments rotate; byte watchdog limits recording and export. Truncated native files are incomplete, not successful captures." };
        try {
            if (retain && directory) {
                await mkdir(directory, { recursive: true });
                if (spool) {
                    const files = (await readdir(spool)).filter(x => /^gpu\.perf\.data(?:[.-][0-9]+)?$/.test(x)).sort().reverse();
                    metadata.nativeFiles = [];
                    for (const name of files) {
                        const path = join(spool, name); const file = await stat(path);
                        if (!file.size) continue;
                        const remaining = 64 * 1024 * 1024 - nativeBytes;
                        if (remaining <= 0) { metadata.nativeTruncated = true; break; }
                        if (file.size > remaining) { await truncate(path, remaining); metadata.nativeTruncated = true; }
                        await copyFile(path, join(directory, name));
                        nativeBytes += Math.min(file.size, remaining); metadata.nativeFiles.push(name);
                    }
                    metadata.nativeBytes = nativeBytes;
                }
                await writeFile(join(directory, "processes.json"), JSON.stringify(metadata) + "\n");
            }
        } finally { if (spool) await rm(spool, { recursive: true, force: true }).catch(() => {}); }
        return metadata;
    }
    return { start, finish, sample };
}
