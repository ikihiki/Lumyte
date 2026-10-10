import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, readdir, rename, rm, symlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { assembleGallerySite } from './assemble-gallery-site.mjs';

const tinyPng = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64');
const repository = 'example/engine';
const capturedAt = '2026-10-10T01:02:03.000Z';

function entry(number, runId, character) {
    const headCommit = character.repeat(40);
    return {
        key: number === null ? 'main' : `pr/${number}`, number,
        title: number === null ? 'main の診断画面' : `診断機能を追加 #${number}`,
        headCommit,
        latestRun: { id: runId, status: 'completed', conclusion: 'success', headCommit },
        preview: { directory: `galleries/${runId}-1`, runId, headCommit },
    };
}

async function addGallery(input, item, options = {}) {
    const directory = join(input, item.preview.directory);
    await mkdir(join(directory, 'screenshots'), { recursive: true });
    await writeFile(join(directory, 'screenshots/settings.png'), tinyPng);
    await writeFile(join(directory, 'gallery.json'), JSON.stringify({
        version: 1, capturedAt,
        screenshots: [{ id: 'settings', title: '設定画面', description: '音量を保存しました。', page: 'Settings', viewport: 'desktop', file: 'screenshots/settings.png' }],
    }));
    await writeFile(join(directory, 'metadata.json'), JSON.stringify({
        version: 1, repository, commit: 'f'.repeat(40), sourceCommit: item.preview.headCommit,
        label: `PR #${item.number}`, capturedAt, screenshotCount: 1, ...options,
    }));
    return directory;
}

async function fixture(t, entries = [entry(null, 100, 'a'), entry(26, 126, 'b'), entry(27, 127, 'c')]) {
    const directory = await mkdtemp(join(tmpdir(), 'lumyte-multipr-'));
    t.after(() => rm(directory, { recursive: true, force: true }));
    const input = join(directory, 'input');
    const output = join(directory, 'site');
    await mkdir(input);
    const catalog = { version: 1, repository, generatedAt: '2026-10-10T02:03:04.000Z', entries };
    const catalogPath = join(input, 'catalog.json');
    const save = () => writeFile(catalogPath, JSON.stringify(catalog));
    await save();
    for (const item of entries) if (item.preview) await addGallery(input, item);
    return { directory, input, output, catalog, catalogPath, save };
}

test('publishes main and two PRs with links back to the shared index, source metadata and rebuilt assets', async t => {
    const { input, output, catalogPath } = await fixture(t);
    const artifact = join(input, 'galleries/126-1');
    await writeFile(join(artifact, 'index.html'), '<script>untrusted artifact HTML</script>');
    await writeFile(join(artifact, 'gallery.js'), 'untrusted artifact JavaScript');
    await writeFile(join(artifact, 'server.log'), 'private log');
    await writeFile(join(artifact, 'screenshots/unlisted.png'), tinyPng);
    const result = await assembleGallerySite(catalogPath, output);
    assert.equal(result.count, 3);
    assert.equal(result.previewCount, 3);
    assert.deepEqual((await readdir(output)).sort(), ['index.html', 'main', 'pr', 'site.css', 'site.json']);
    assert.deepEqual((await readdir(join(output, 'pr'))).sort(), ['26', '27']);
    assert.deepEqual(await readdir(join(output, 'pr/26/screenshots')), ['settings.png']);
    const index = await readFile(join(output, 'index.html'), 'utf8');
    for (const path of ['main/', 'pr/26/', 'pr/27/']) assert.ok(index.includes(`href="${path}"`));
    assert.match(index, /https:\/\/github.com\/example\/engine\/actions\/runs\/126/u);
    assert.match(index, /撮影元のコミット/u);
    assert.match(index, /PR マージ結果/u);
    assert.match(index, /10:02 JST/u);
    assert.match(index, /Content-Security-Policy/u);
    assert.match(await readFile(join(output, 'main/index.html'), 'utf8'), /href="\.\.\/">←/u);
    assert.match(await readFile(join(output, 'pr/26/index.html'), 'utf8'), /href="\.\.\/\.\.\/">←/u);
    assert.ok(!(await readFile(join(output, 'pr/26/gallery.js'), 'utf8')).includes('untrusted'));
    assert.ok(!(await readFile(join(output, 'pr/26/index.html'), 'utf8')).includes('untrusted'));
    const site = JSON.parse(await readFile(join(output, 'site.json'), 'utf8'));
    assert.deepEqual(site.entries.map(item => item.key), ['main', 'pr/27', 'pr/26']);
    assert.equal(site.entries[2].preview.directory, undefined);
    assert.equal(site.entries[2].preview.headCommit, 'b'.repeat(40));
    assert.equal(site.entries[2].preview.commit, 'f'.repeat(40));
    assert.equal(JSON.parse(await readFile(join(output, 'pr/26/metadata.json'), 'utf8')).sourceCommit, 'b'.repeat(40));
});

test('rebuilding after one PR updates retains other galleries and omits closed PRs', async t => {
    const { directory, input, output, catalog, catalogPath, save } = await fixture(t);
    await assembleGallerySite(catalogPath, output);
    const before = await readFile(join(output, 'pr/27/gallery.json'), 'utf8');
    const updated = entry(26, 226, 'd');
    catalog.entries[1] = updated;
    await addGallery(input, updated, { commit: 'e'.repeat(40) });
    await save();
    const updatedOutput = join(directory, 'updated');
    await assembleGallerySite(catalogPath, updatedOutput);
    assert.equal(await readFile(join(updatedOutput, 'pr/27/gallery.json'), 'utf8'), before);
    assert.equal(JSON.parse(await readFile(join(updatedOutput, 'pr/26/metadata.json'), 'utf8')).commit, 'e'.repeat(40));
    catalog.entries = catalog.entries.filter(item => item.number !== 27);
    await save();
    const closedOutput = join(directory, 'closed');
    await assembleGallerySite(catalogPath, closedOutput);
    assert.deepEqual(await readdir(join(closedOutput, 'pr')), ['26']);
    assert.ok(!(await readFile(join(closedOutput, 'index.html'), 'utf8')).includes('PR #27'));
});

test('retains an older successful capture when the latest CI fails or runs, and explains missing artifacts', async t => {
    const failed = entry(26, 126, 'b');
    failed.headCommit = 'd'.repeat(40);
    failed.latestRun = { id: 226, status: 'completed', conclusion: 'failure', headCommit: failed.headCommit };
    const pending = entry(27, 127, 'c');
    pending.latestRun = { id: 227, status: 'in_progress', conclusion: null, headCommit: pending.headCommit };
    const missing = entry(28, 128, 'e');
    missing.preview = null;
    missing.unavailableReason = 'CI 成果物の保存期間が終了しました。';
    const notRun = { ...entry(29, 129, 'f'), latestRun: null, preview: null };
    const { output, catalogPath } = await fixture(t, [failed, pending, missing, notRun]);
    await assembleGallerySite(catalogPath, output);
    const index = await readFile(join(output, 'index.html'), 'utf8');
    assert.match(index, /CI 失敗/u);
    assert.match(index, /CI 実行中/u);
    assert.match(index, /CI 未実行/u);
    assert.match(index, /以前のコミットの画面です/u);
    assert.match(index, /同じコミットの以前の撮影を表示/u);
    assert.match(index, /成果物の保存期間が終了/u);
    assert.match(index, /href="pr\/26\/"/u);
    assert.match(index, /href="pr\/27\/"/u);
    assert.ok(!index.includes('href="pr/28/"'));
    assert.ok(!index.includes('href="pr/29/"'));
});

test('escapes API titles and unavailable reasons and never embeds them as script or markup', async t => {
    const payload = '<img src=x onerror="alert(1)"> & \'title\'';
    const item = { ...entry(26, 126, 'a'), title: payload, preview: null, unavailableReason: payload };
    const { output, catalogPath } = await fixture(t, [item]);
    await assembleGallerySite(catalogPath, output);
    const index = await readFile(join(output, 'index.html'), 'utf8');
    assert.ok(!index.includes(payload));
    assert.ok(index.includes('&lt;img src=x onerror=&quot;alert(1)&quot;&gt; &amp; &#39;title&#39;'));
    assert.ok(!index.includes('<script'));
    assert.equal(JSON.parse(await readFile(join(output, 'site.json'), 'utf8')).entries[0].title, payload);
});

test('accepts an empty catalog and shows an empty state', async t => {
    const { output, catalogPath } = await fixture(t, []);
    const result = await assembleGallerySite(catalogPath, output);
    assert.equal(result.count, 0);
    assert.match(await readFile(join(output, 'index.html'), 'utf8'), /公開対象のプレビューはまだありません/u);
});

test('rejects unsafe artifact paths, invalid keys and mismatched directory run IDs', async t => {
    for (const path of ['../private', '/tmp/private', 'galleries/../secret', 'galleries/%2e%2e', 'galleries\\126-1', 'galleries/126-1/sub', 'galleries/226-1']) {
        await t.test(path, async child => {
            const { output, catalog, catalogPath, save } = await fixture(child);
            catalog.entries[1].preview.directory = path;
            await save();
            await assert.rejects(assembleGallerySite(catalogPath, output), /Preview directory/u);
            await assert.rejects(readdir(output), { code: 'ENOENT' });
        });
    }
    for (const key of ['pr/../secret', '/pr/26', 'pr/026', 'pr/27', 'main']) {
        await t.test(key, async child => {
            const { output, catalog, catalogPath, save } = await fixture(child);
            catalog.entries[1].key = key;
            await save();
            await assert.rejects(assembleGallerySite(catalogPath, output), /keys|number/u);
        });
    }
});

test('rejects symlinked catalogs, artifacts, metadata, PNGs and output directories', async t => {
    for (const kind of ['catalog', 'artifact', 'metadata', 'image', 'output']) {
        await t.test(kind, async child => {
            const { directory, input, output, catalogPath } = await fixture(child);
            const path = kind === 'catalog' ? catalogPath : kind === 'artifact' ? join(input, 'galleries/126-1') :
                kind === 'metadata' ? join(input, 'galleries/126-1/metadata.json') :
                    kind === 'image' ? join(input, 'galleries/126-1/screenshots/settings.png') : output;
            const target = join(directory, 'private');
            if (kind === 'output') await mkdir(target);
            else await rename(path, target);
            await symlink(target, path, kind === 'artifact' || kind === 'output' ? 'dir' : 'file');
            await assert.rejects(assembleGallerySite(catalogPath, output), /Symbolic links/u);
            assert.ok(!(await readdir(directory)).some(name => name.startsWith('.diagnostics-site-')));
        });
    }
});

test('rejects metadata that does not match the API catalog or capture manifest', async t => {
    for (const change of [{ repository: 'attacker/repo' }, { sourceCommit: 'd'.repeat(40) }, { sourceCommit: undefined },
        { commit: 'main' }, { capturedAt: '2026-10-10T09:00:00.000Z' }, { screenshotCount: 2 }, { version: 2 }]) {
        await t.test(JSON.stringify(change), async child => {
            const { directory, input, output, catalog, catalogPath } = await fixture(child);
            await addGallery(input, catalog.entries[1], change);
            await assert.rejects(assembleGallerySite(catalogPath, output), /metadata|Metadata/u);
            await assert.rejects(readdir(output), { code: 'ENOENT' });
            assert.ok(!(await readdir(directory)).some(name => name.startsWith('.diagnostics-site-')));
        });
    }
});

test('rejects invalid images and manifest traversal without partially publishing earlier valid galleries', async t => {
    for (const kind of ['png', 'path']) {
        await t.test(kind, async child => {
            const { directory, input, output, catalogPath } = await fixture(child);
            if (kind === 'png') await writeFile(join(input, 'galleries/126-1/screenshots/settings.png'), '<html>not a PNG</html>');
            else {
                const path = join(input, 'galleries/126-1/gallery.json');
                const manifest = JSON.parse(await readFile(path, 'utf8'));
                manifest.screenshots[0].file = '../secret.png';
                await writeFile(path, JSON.stringify(manifest));
            }
            await assert.rejects(assembleGallerySite(catalogPath, output), /PNG|screenshots\/<slug>/u);
            await assert.rejects(readdir(output), { code: 'ENOENT' });
            assert.ok(!(await readdir(directory)).some(name => name.startsWith('.diagnostics-site-')));
        });
    }
});

test('rejects invalid API metadata, duplicate entries, nonempty output and directory overlap', async t => {
    const { input, output, catalog, catalogPath, save } = await fixture(t);
    const original = structuredClone(catalog);
    for (const change of [value => { value.repository = 'https://attacker.invalid/repo'; },
        value => { value.generatedAt = '2026-02-30T01:00:00.000Z'; },
        value => { value.entries.push(value.entries[0]); },
        value => { value.entries[0].latestRun.id = -1; },
        value => { value.entries[0].latestRun.status = '<script>'; },
        value => { value.entries[0].latestRun.conclusion = '<script>'; },
        value => { value.entries[0].title = 'x'.repeat(301); },
        value => { value.entries[0].headCommit = 'main'; },
        value => { value.entries[0].number = 0; }]) {
        Object.assign(catalog, structuredClone(original));
        change(catalog);
        await save();
        await assert.rejects(assembleGallerySite(catalogPath, output));
    }
    Object.assign(catalog, structuredClone(original));
    await save();
    await assert.rejects(assembleGallerySite(catalogPath, input), /must not overlap/u);
    await mkdir(output);
    await writeFile(join(output, 'preserve.txt'), 'existing output');
    await assert.rejects(assembleGallerySite(catalogPath, output), /must be empty/u);
    assert.equal(await readFile(join(output, 'preserve.txt'), 'utf8'), 'existing output');
});
