import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const helper = fileURLToPath(new URL('./ci-apt.sh', import.meta.url));
function run(overrides, arch = 'x86_64') {
  const root = mkdtempSync(join(tmpdir(), 'lumyte-ci-apt-'));
  try {
    const result = spawnSync('bash', ['-eu', '-c',
      'uname() { echo "$TEST_ARCH"; }; source "$TEST_HELPER"; declare -p lumyte_apt_options'], {
      encoding: 'utf8',
      env: { ...process.env, LUMYTE_ENV_ROOT: root, TEST_HELPER: helper,
        TEST_ARCH: arch, LUMYTE_CI_APT: '1', GITHUB_ACTIONS: 'true',
        RUNNER_ENVIRONMENT: 'github-hosted', ID: 'ubuntu', VERSION_CODENAME: 'noble', ...overrides },
    });
    return { ...result, files: readdirSync(root), sources: result.status === 0 &&
      readdirSync(root).length ? readFileSync(join(root, 'ci-apt/sources.list'), 'utf8') : '' };
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

test('local setup retains host sources and does not create private configuration', () => {
  const result = run({ LUMYTE_CI_APT: '0', GITHUB_ACTIONS: '' });
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(result.files, []);
  assert.ok(result.stdout.includes('lumyte_apt_options=()'));
});
for (const [arch, mirror] of [['x86_64', 'https://archive.ubuntu.com/ubuntu'],
  ['aarch64', 'https://ports.ubuntu.com/ubuntu-ports']]) {
  test('CI ' + arch + ' uses signed official HTTPS indexes and verified archive reuse', () => {
    const result = run({}, arch);
    assert.equal(result.status, 0, result.stderr);
    assert.ok(result.sources.includes(mirror));
    assert.equal(result.sources.trim().split(String.fromCharCode(10)).length, 3);
    assert.ok(result.sources.includes('signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg'));
    assert.ok(result.sources.includes('noble-security'));
    assert.ok(result.stdout.includes('Dir::Etc::sourceparts=-'));
    assert.ok(result.stdout.includes('Dir::Cache::archives='));
    assert.ok(result.stdout.includes('Keep-Downloaded-Packages=true'));
    assert.ok(!result.sources.includes('trusted=yes'));
    assert.deepEqual(result.files, ['ci-apt']);
  });
}
for (const overrides of [{ GITHUB_ACTIONS: '' }, { RUNNER_ENVIRONMENT: 'self-hosted' }, { ID: 'debian' },
  { VERSION_CODENAME: 'other' }]) {
  test('CI routing fails closed outside its declared runner: ' + JSON.stringify(overrides), () => {
    const result = run(overrides);
    assert.notEqual(result.status, 0);
    assert.deepEqual(result.files, []);
  });
}
test('CI routing rejects unsupported architectures before writing files', () => {
  const result = run({}, 'unknown');
  assert.notEqual(result.status, 0);
  assert.deepEqual(result.files, []);
});
