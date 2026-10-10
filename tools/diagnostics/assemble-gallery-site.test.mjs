import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { cp, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { promisify } from 'node:util';
import test from 'node:test';
import { generateGallery } from './generate-gallery.mjs';

const run = promisify(execFile);
const assembler = new URL('./assemble-gallery-site.mjs', import.meta.url).pathname;
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64');

test('updates one PR without losing other previews and bounds retained PR count', async t => {
    const directory = await mkdtemp(join(tmpdir(), 'lumyte-pages-'));
    t.after(() => rm(directory, { recursive: true, force: true }));
    const input = join(directory, 'input');
    const gallery = join(directory, 'gallery');
    const site = join(directory, 'site');
    await mkdir(join(input, 'screenshots'), { recursive: true });
    await writeFile(join(input, 'screenshots/test.png'), png);
    await writeFile(join(input, 'gallery.json'), JSON.stringify({ version: 1, capturedAt: '2026-10-10T00:00:00.000Z', screenshots: [{ id: 'test', title: 'Test', description: 'Actual capture', page: 'settings', viewport: 'desktop', file: 'screenshots/test.png' }] }));
    await generateGallery(input, gallery, { repository: 'example/engine', commit: 'a'.repeat(40), label: '<PR>' });
    await writeFile(join(gallery, 'private.log'), 'Do not publish');
    await run(process.execPath, [assembler, gallery, site, 'main']);
    await run(process.execPath, [assembler, gallery, site, 'pr-26']);
    await writeFile(join(site, 'previews/pr-26/screenshots/old.png'), png);
    await run(process.execPath, [assembler, gallery, site, 'pr-26']);
    assert.deepEqual(await readdir(join(site, 'previews')), ['main', 'pr-26']);
    assert.deepEqual(await readdir(join(site, 'previews/pr-26/screenshots')), ['test.png']);
    assert.ok(!(await readdir(join(site, 'previews/pr-26'))).includes('private.log'));
    const html = await readFile(join(site, 'index.html'), 'utf8');
    assert.ok(html.includes('previews/pr-26/'));
    assert.ok(html.includes('&lt;PR&gt;') && !html.includes('<PR>'));
    for (let number = 1; number <= 22; number++) {
        await cp(gallery, join(site, 'previews', `pr-${number}`), { recursive: true });
        const metadataPath = join(site, 'previews', `pr-${number}`, 'metadata.json');
        const metadata = JSON.parse(await readFile(metadataPath, 'utf8'));
        metadata.capturedAt = `2026-09-${String(number).padStart(2, '0')}T00:00:00.000Z`;
        await writeFile(metadataPath, JSON.stringify(metadata));
    }
    await run(process.execPath, [assembler, gallery, site, 'pr-26']);
    const retained = await readdir(join(site, 'previews'));
    assert.equal(retained.length, 21);
    assert.ok(retained.includes('main') && retained.includes('pr-26') && !retained.includes('pr-1'));
    await assert.rejects(run(process.execPath, [assembler, gallery, site, '../escape']));
    assert.equal((await readdir(join(site, 'previews'))).length, 21);
});
