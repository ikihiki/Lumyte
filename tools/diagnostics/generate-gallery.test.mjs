import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, readdir, rm, symlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { generateGallery } from './generate-gallery.mjs';

const tinyPng = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64');
const metadata = { repository: 'example/engine', commit: 'a'.repeat(40), label: 'PR #26' };

async function fixture(t, change = () => {}) {
    const directory = await mkdtemp(join(tmpdir(), 'lumyte-gallery-'));
    t.after(() => rm(directory, { recursive: true, force: true }));
    const input = join(directory, 'input');
    const output = join(directory, 'output');
    await mkdir(join(input, 'screenshots'), { recursive: true });
    await writeFile(join(input, 'screenshots/settings.png'), tinyPng);
    const manifest = {
        version: 1,
        capturedAt: '2026-10-10T01:02:03.000Z',
        screenshots: [{ id: 'settings', title: '設定を保存', description: '音量を変更して保存した画面です。', page: 'Settings', viewport: 'desktop', file: 'screenshots/settings.png' }],
    };
    change(manifest);
    await writeFile(join(input, 'gallery.json'), JSON.stringify(manifest));
    return { directory, input, output, manifest };
}

test('publishes only declared PNGs and gallery assets, with relative image links and precise metadata', async t => {
    const { input, output } = await fixture(t);
    await writeFile(join(input, 'failure.html'), 'private browser state');
    await writeFile(join(input, 'server.log'), 'private server output');
    await writeFile(join(input, 'screenshots/unlisted.png'), tinyPng);
    const result = await generateGallery(input, output, metadata);
    assert.equal(result.count, 1);
    assert.deepEqual((await readdir(output)).sort(), ['gallery.css', 'gallery.js', 'index.html', 'metadata.json', 'screenshots']);
    assert.deepEqual(await readdir(join(output, 'screenshots')), ['settings.png']);
    assert.deepEqual(await readFile(join(output, 'screenshots/settings.png')), tinyPng);
    const html = await readFile(join(output, 'index.html'), 'utf8');
    assert.match(html, /lang="ja"/u);
    assert.match(html, /src="screenshots\/settings.png"/u);
    assert.match(html, /https:\/\/github.com\/example\/engine\/commit\/a{40}/u);
    assert.match(html, /10:02 JST/u);
    assert.match(html, /Content-Security-Policy/u);
    assert.deepEqual(JSON.parse(await readFile(join(output, 'metadata.json'), 'utf8')), {
        version: 1, repository: metadata.repository, commit: metadata.commit, label: metadata.label,
        capturedAt: '2026-10-10T01:02:03.000Z', screenshotCount: 1,
    });
});

test('escapes captions, page names and the preview label without introducing executable markup', async t => {
    const payload = '<script>alert("caption")</script> & \'text\'';
    const { input, output } = await fixture(t, manifest => {
        manifest.screenshots[0].title = payload;
        manifest.screenshots[0].description = payload;
        manifest.screenshots[0].page = payload;
    });
    await generateGallery(input, output, { ...metadata, label: payload });
    const html = await readFile(join(output, 'index.html'), 'utf8');
    assert.ok(!html.includes(payload));
    assert.ok(html.includes('&lt;script&gt;alert(&quot;caption&quot;)&lt;/script&gt; &amp; &#39;text&#39;'));
    assert.equal([...html.matchAll(/<script\b/gu)].length, 1);
    assert.ok(!html.includes('onerror='));
});

test('rejects traversal, absolute paths, encoded separators, non-PNG paths and duplicate entries', async t => {
    for (const file of ['../secret.png', '/tmp/private.png', 'screenshots/../secret.png', 'screenshots/%2e%2e%2fsecret.png', 'screenshots\\private.png', 'screenshots/private.html']) {
        await t.test(file, async child => {
            const { input, output } = await fixture(child, manifest => { manifest.screenshots[0].file = file; });
            await assert.rejects(generateGallery(input, output, metadata), /screenshots\/<slug>\.png/u);
            await assert.rejects(readdir(output), { code: 'ENOENT' });
        });
    }
    await t.test('duplicate id and path', async child => {
        const { input, output } = await fixture(child, manifest => { manifest.screenshots.push({ ...manifest.screenshots[0] }); });
        await assert.rejects(generateGallery(input, output, metadata), /unique lowercase slug/u);
    });
});

test('rejects symlinked images, screenshot directories and output directories', async t => {
    for (const kind of ['image', 'screenshots', 'output']) {
        await t.test(kind, async child => {
            const { directory, input, output } = await fixture(child);
            if (kind === 'image') {
                await writeFile(join(directory, 'private.png'), tinyPng);
                await rm(join(input, 'screenshots/settings.png'));
                await symlink(join(directory, 'private.png'), join(input, 'screenshots/settings.png'));
            } else if (kind === 'screenshots') {
                await mkdir(join(directory, 'private'));
                await writeFile(join(directory, 'private/settings.png'), tinyPng);
                await rm(join(input, 'screenshots'), { recursive: true });
                await symlink(join(directory, 'private'), join(input, 'screenshots'), 'dir');
            } else {
                await mkdir(join(directory, 'public'));
                await symlink(join(directory, 'public'), output, 'dir');
            }
            await assert.rejects(generateGallery(input, output, metadata), /Symbolic links/u);
        });
    }
});

test('rejects invalid PNG contents and excessive dimensions before publishing any files', async t => {
    for (const kind of ['contents', 'dimensions']) {
        await t.test(kind, async child => {
            const { input, output } = await fixture(child);
            const contents = kind === 'contents' ? Buffer.from('<html>not an image</html>') : Buffer.from(tinyPng);
            if (kind === 'dimensions') contents.writeUInt32BE(100000, 16);
            await writeFile(join(input, 'screenshots/settings.png'), contents);
            await assert.rejects(generateGallery(input, output, metadata), /PNG/u);
            await assert.rejects(readdir(output), { code: 'ENOENT' });
        });
    }
});

test('rejects invalid metadata, dates and occupied output without modifying existing files', async t => {
    const { input, output } = await fixture(t);
    await assert.rejects(generateGallery(input, output, { ...metadata, repository: 'https://evil.example/repo' }), /GITHUB_REPOSITORY/u);
    await assert.rejects(generateGallery(input, output, { ...metadata, commit: 'main' }), /PREVIEW_COMMIT/u);
    await assert.rejects(generateGallery(input, input, metadata), /must not overlap/u);
    await mkdir(output);
    await writeFile(join(output, 'server.log'), 'existing private output');
    await assert.rejects(generateGallery(input, output, metadata), /must be empty/u);
    assert.equal(await readFile(join(output, 'server.log'), 'utf8'), 'existing private output');
    const invalid = await fixture(t, manifest => { manifest.capturedAt = '2026-02-30T01:02:03.000Z'; });
    await assert.rejects(generateGallery(invalid.input, invalid.output, metadata), /ISO capturedAt/u);
});
