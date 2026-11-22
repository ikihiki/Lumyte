import test from "node:test";
import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { createBrowserProcessRecorder, parseProcessStat } from "./browser-process-recorder.mjs";
function fixture({ perf = false, rewritten = false, hanging = false } = {}) {
    let start = "100";
    const row = (parent, id) => "1 (name with ) spaces) " + ["S", parent, 0, 0, 0, 0, 0, 0, 0, 0, 0, 12, 3, 0, 0, 0, 0, 4, 0, id].join(" ");
    const paths = [];
    const read = async path => {
        paths.push(path);
        if (path === "/proc/100/stat") return row(1, start);
        if (path === "/proc/101/stat") return row(100, "200");
        if (path === "/proc/102/stat") return row(999, "300");
        if (path.endsWith("/children")) return path.includes("/100/") ? "101 102" : "";
        if (path.endsWith("/cmdline")) return rewritten ? "chrome --type=gpu-process --private-argument=hidden\0" : "chrome\0--type=gpu-process\0do-not-record-private-argument";
        if (path.endsWith("/schedstat")) return "1000 2000 3";
        throw new Error("missing");
    };
    const list = async path => path.endsWith("/task") ? [path.split("/")[2]] : [];
    const children = [], signals = [];
    let groupAlive = hanging;
    const launch = (binary, args, options) => {
        assert.equal(options.detached, true);
        const child = new EventEmitter(); child.stderr = new EventEmitter(); child.exitCode = null; child.signalCode = null;
        child.kill = signal => { child.signalCode = signal; child.emit("exit", null); };
        children.push({ binary, args, child });
        if (hanging) { child.pid = 5678; return child; }
        queueMicrotask(() => { child.stderr.emit("data", Buffer.from("permission denied")); child.exitCode = 1; child.emit("exit", 1); });
        return child;
    };
    const recorder = createBrowserProcessRecorder({ browserPid: 100, platform: "linux", read, list, link: async () => "/chrome", launch,
        environment: perf ? { LUMYTE_BROWSER_PERF: "/perf" } : {}, intervalMs: 1000000, maxSamples: 2, stopGraceMs: 5,
        signalGroup: (pid, signal) => {
            assert.equal(pid, 5678); signals.push(signal);
            if (signal === "SIGINT") { const child = children[0].child; child.exitCode = 0; child.emit("exit", 0); }
            if (signal === "SIGKILL") groupAlive = false;
        } });
    return { recorder, paths, children, signals, groupAlive: () => groupAlive, reuse() { start = "101"; } };
}
test("stat parser handles parentheses and identifies kernel fields", () => {
    const f = parseProcessStat("1 (a ) b) " + ["S", 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 12, 3, 0, 0, 0, 0, 4, 0, "123"].join(" "));
    assert.equal(f.ppid, 9); assert.equal(f.userTicks, 12); assert.equal(f.startTicks, "123");
});
test("only verified descendants are sampled, history is bounded, raw arguments are not retained", async () => {
    const f = fixture(); await f.recorder.start(); await f.recorder.sample(); await f.recorder.sample();
    const result = await f.recorder.finish();
    assert.equal(result.samples.length, 2); assert.equal(result.dropped, 1);
    assert.deepEqual(result.samples[0].processes.map(x => x.pid), [101]);
    assert.equal(result.samples[0].processes[0].runqueueWaitNs, "2000");
    assert.equal(JSON.stringify(result).includes("private-argument"), false);
    assert.equal(f.paths.includes("/proc/102/cmdline"), false);
    assert.equal(result.nativeStatus, "unavailable-no-configured-tool");
});
test("browser PID reuse stops ownership discovery without following the new process", async () => {
    const f = fixture(); await f.recorder.start(); f.reuse(); await f.recorder.sample();
    const result = await f.recorder.finish(); assert.equal(result.unavailable, "browser-exited-or-pid-reused");
});
test("native sampling is PID-scoped, bounded and reports permission denial", async () => {
    const f = fixture({ perf: true }); await f.recorder.start();
    const result = await f.recorder.finish();
    assert.equal(f.children[0].binary, "timeout");
    assert.deepEqual(f.children[0].args.slice(-2), ["-p", "101"]);
    assert.equal(f.children[0].args.includes("-a"), false);
    assert.equal(result.nativeStatus, "unavailable-permission-or-recording-error");
    assert.equal(result.nativeBytes, 0);
});
test("unsupported platforms leave a truthful unavailable result", async () => {
    const r = createBrowserProcessRecorder({ browserPid: 100, platform: "win32" });
    await r.start(); const result = await r.finish(); assert.equal(result.unavailable, "unsupported-platform-or-pid");
});

test("Chrome rewritten process titles still identify owned GPU processes", async () => {
 const f = fixture({ rewritten: true }); await f.recorder.start(); const result = await f.recorder.finish();
 assert.equal(result.samples[0].processes[0].type, "gpu-process");
 assert.equal(JSON.stringify(result).includes("hidden"), false);
});

test("cleanup kills the entire owned group even when timeout exits before perf", async () => {
 const f = fixture({ perf: true, hanging: true }); await f.recorder.start();
 await f.recorder.finish();
 assert.deepEqual(f.signals, ["SIGINT", "SIGKILL"]);
 assert.equal(f.groupAlive(), false);
});

test("CI perf discovery follows version-directory symlinks", { skip: process.platform !== "linux" }, async () => {
 const { mkdtemp, mkdir, writeFile, symlink, rm, readFile } = await import("node:fs/promises");
 const { tmpdir } = await import("node:os");
 const { join } = await import("node:path");
 const { execFileSync } = await import("node:child_process");
 const root = await mkdtemp(join(tmpdir(), "lumyte-perf-layout-"));
 try {
  const actual = join(root, "linux-tools-version"), links = join(root, "linux-tools");
  await mkdir(actual); await mkdir(links);
  await writeFile(join(actual, "perf"), "#!/bin/sh\nexit 0\n", { mode: 0o700 });
  await symlink(actual, join(links, "version-generic"));
  assert.equal(execFileSync("find", [links, "-type", "f", "-name", "perf", "-executable"], { encoding: "utf8" }), "");
  assert.match(execFileSync("find", ["-L", links, "-type", "f", "-name", "perf", "-executable"], { encoding: "utf8" }), /version-generic\/perf/);
  const workflow = await readFile(new URL("../../.github/workflows/composition.yml", import.meta.url), "utf8");
  assert.match(workflow, /find -L \/usr\/lib\/linux-tools -type f/);
 } finally { await rm(root, { recursive: true, force: true }); }
});
