import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const helper = fileURLToPath(new URL('./install-ci-runtime.sh', import.meta.url));
function run(args = [], overrides = {}) {
  const root = mkdtempSync(join(tmpdir(), 'lumyte-runtime-'));
  try {
    const bin = join(root, 'bin');
    mkdirSync(bin);
    const log = join(root, 'apt.jsonl');
    const forbidden = join(root, 'product-build');
    const fake = (name, content) => writeFileSync(join(bin, name), content, { mode: 0o755 });
    fake('sudo', '#!/bin/bash\nwhile [[ "$1" == -* ]]; do shift; done\nexec "$@"\n');
    fake('uname', '#!/bin/bash\necho x86_64\n');
    fake('apt-get', '#!' + process.execPath + '\n' +
      'const fs = require("node:fs"); const args = process.argv.slice(2);\n' +
      'fs.appendFileSync(process.env.TEST_LOG, JSON.stringify({args, frontend: process.env.DEBIAN_FRONTEND}) + "\\n");\n' +
      'if (args.includes(process.env.TEST_FAIL)) process.exit(42);\n');
    for (const tool of ['dotnet', 'mise', 'cmake', 'ninja', 'make', 'cargo', 'gcc', 'g++']) {
      fake(tool, '#!/bin/bash\ntouch "$TEST_FORBIDDEN"\nexit 99\n');
    }
    const osRelease = join(root, 'os-release');
    writeFileSync(osRelease, 'ID=ubuntu\nVERSION_CODENAME=noble\n');
    // Only substitute the OS fixture; run the actual helper and libraries.
    const harness = 'source() { if [[ "$1" == /etc/os-release ]]; then builtin source "$TEST_OS_RELEASE"; else builtin source "$@"; fi; }; builtin source "$TEST_HELPER" "$@"';
    const result = spawnSync('bash', ['-c', harness, 'runtime-test', ...args], {
      encoding: 'utf8',
      env: { ...process.env, PATH: bin + ':' + process.env.PATH, TEST_HELPER: helper,
        TEST_OS_RELEASE: osRelease, TEST_LOG: log, TEST_FORBIDDEN: forbidden,
        TEST_FAIL: '', LUMYTE_ENV_ROOT: join(root, 'env with spaces'),
        LUMYTE_CI_APT: '1', GITHUB_ACTIONS: 'true', RUNNER_ENVIRONMENT: 'github-hosted', ...overrides },
    });
    const sources = join(root, 'env with spaces/ci-apt/sources.list');
    return { ...result, calls: existsSync(log) ? readFileSync(log, 'utf8').trim().split('\n').map(JSON.parse) : [],
      built: existsSync(forbidden), sources: existsSync(sources) ? readFileSync(sources, 'utf8') : '',
      envRoot: join(root, 'env with spaces') };
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

for (const [role, packages] of [
  ['native', ['libopenal1', 'xvfb', 'xauth']], ['browser', ['libopenal1']], ['aot', ['libopenal1']],
]) {
  test(role + ': signed update and install share identical private APT options', () => {
    const result = run([role]);
    assert.equal(result.status, 0, result.stderr);
    assert.equal(result.built, false);
    const options = ['-o', 'Dir::Etc::sourcelist=' + result.envRoot + '/ci-apt/sources.list',
      '-o', 'Dir::Etc::sourceparts=-', '-o', 'Dir::Cache::archives=' + result.envRoot + '/ci-apt/archives',
      '-o', 'Binary::apt-get::APT::Keep-Downloaded-Packages=true'];
    assert.deepEqual(result.calls, [
      { args: [...options, 'update', '-o', 'APT::Update::Error-Mode=any'] },
      { args: [...options, 'install', '-y', '--no-install-recommends', ...packages], frontend: 'noninteractive' },
    ]);
    assert.equal(result.sources.trim().split('\n').length, 3);
    assert.ok(result.sources.includes('signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg'));
    assert.ok(!/trusted=yes|allow-insecure|allow-unauthenticated/i.test(result.sources + JSON.stringify(result.calls)));
  });
}
test('optional role defaults to native fixtures', () => {
  const result = run();
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(result.calls[1].args.slice(-3), ['libopenal1', 'xvfb', 'xauth']);
});
for (const failure of ['update', 'install']) {
  test(failure + ' failure stops provisioning', () => {
    const result = run(['native'], { TEST_FAIL: failure });
    assert.equal(result.status, 42, result.stderr);
    assert.equal(result.calls.length, failure === 'update' ? 1 : 2);
    assert.equal(result.built, false);
  });
}
for (const args of [['unknown'], ['native', 'unexpected']]) {
  test('invalid arguments fail before APT: ' + args.join(' '), () => {
    const result = run(args);
    assert.equal(result.status, 2);
    assert.deepEqual(result.calls, []);
    assert.equal(result.sources, '');
    assert.equal(result.built, false);
  });
}
test('private routing fails closed on self-hosted runners before APT', () => {
  const result = run(['native'], { RUNNER_ENVIRONMENT: 'self-hosted' });
  assert.notEqual(result.status, 0);
  assert.deepEqual(result.calls, []);
});
test('local routing retains host configuration without private sources', () => {
  const result = run(['aot'], { LUMYTE_CI_APT: '0' });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.sources, '');
  assert.deepEqual(result.calls.map(call => call.args), [
    ['update', '-o', 'APT::Update::Error-Mode=any'],
    ['install', '-y', '--no-install-recommends', 'libopenal1'],
  ]);
});
