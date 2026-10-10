import assert from 'node:assert/strict';
import { copyFile, mkdir, readdir, readFile, rm, writeFile, lstat } from 'node:fs/promises';
import { resolve } from 'node:path';

// Build one Pages site while retaining the latest capture for each reviewed PR.
const [incomingArgument, siteArgument, key] = process.argv.slice(2);
assert.ok(incomingArgument && siteArgument && /^(main|pr-[1-9][0-9]*)$/.test(key ?? ''), 'Usage: assemble-gallery-site.mjs <gallery> <site> <main|pr-N>');
const incoming = resolve(incomingArgument);
const site = resolve(siteArgument);
assert.ok(incoming !== site && !incoming.startsWith(site + '/') && !site.startsWith(incoming + '/'), 'Input and site directories must be separate');
const metadata = JSON.parse(await readFile(resolve(incoming, 'metadata.json'), 'utf8'));
assert.match(metadata.repository, /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/);
assert.match(metadata.commit, /^[a-f0-9]{40}$/);
assert.ok(Number.isFinite(Date.parse(metadata.capturedAt)), 'Invalid capture date');
const destination = resolve(site, 'previews', key);
await rm(destination, { recursive: true, force: true });
await mkdir(resolve(destination, 'screenshots'), { recursive: true });
for (const name of ['index.html', 'gallery.css', 'gallery.js', 'metadata.json']) {
    const path = resolve(incoming, name);
    assert.ok((await lstat(path)).isFile(), 'Gallery assets must be regular files');
    await copyFile(path, resolve(destination, name));
}
for (const item of await readdir(resolve(incoming, 'screenshots'), { withFileTypes: true })) {
    assert.ok(item.isFile() && /^[a-z0-9-]+\.png$/.test(item.name), 'Unexpected screenshot asset');
    await copyFile(resolve(incoming, 'screenshots', item.name), resolve(destination, 'screenshots', item.name));
}
const previews = [];
for (const item of await readdir(resolve(site, 'previews'), { withFileTypes: true })) {
    if (!item.isDirectory() || !/^(main|pr-[1-9][0-9]*)$/.test(item.name)) continue;
    const data = JSON.parse(await readFile(resolve(site, 'previews', item.name, 'metadata.json'), 'utf8'));
    assert.equal(data.repository, metadata.repository, 'Unexpected repository in preview archive');
    previews.push({ ...data, key: item.name });
}
previews.sort((a, b) => Date.parse(b.capturedAt) - Date.parse(a.capturedAt));
async function bytes(path) {
    let total = 0;
    for (const entry of await readdir(path, { withFileTypes: true })) {
        const child = resolve(path, entry.name);
        assert.ok(!entry.isSymbolicLink(), 'Archive cannot contain symbolic links');
        total += entry.isDirectory() ? await bytes(child) : (await lstat(child)).size;
    }
    return total;
}
// Bound both the number of previews and the published site size (below Pages' 1 GiB limit).
const maximumBytes = 800 * 1024 * 1024;
const main = previews.find(preview => preview.key === 'main');
let retainedBytes = main ? await bytes(resolve(site, 'previews', 'main')) : 0;
assert.ok(retainedBytes < maximumBytes, 'Main preview exceeds the site limit');
let retainedPullRequests = 0;
const retained = [];
for (const preview of previews) {
    const size = await bytes(resolve(site, 'previews', preview.key));
    if (preview.key === 'main' || (retainedPullRequests < 20 && retainedBytes + size < maximumBytes)) {
        retained.push(preview);
        if (preview.key !== 'main') { retainedPullRequests++; retainedBytes += size; }
    } else await rm(resolve(site, 'previews', preview.key), { recursive: true, force: true });
}
const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');
const cards = retained.map((preview, index) => `<a class="card" href="previews/${preview.key}/"><span class="eyebrow">${index === 0 ? 'LATEST PREVIEW' : 'PREVIEW'}</span><h2>${escape(preview.label || preview.key)}</h2><p>${escape(preview.screenshotCount)}枚の実画面</p><p class="meta">${escape(preview.commit.slice(0, 7))} · ${escape(new Date(preview.capturedAt).toISOString())}</p><strong>画面を見る →</strong></a>`).join('\n');
await writeFile(resolve(site, 'index.html'), `<!doctype html><html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Lumyte — 画面プレビュー</title><meta name="description" content="Lumyte診断画面の実画面ギャラリー"><style>*{box-sizing:border-box}body{margin:0;background:#f4f6f1;color:#23372e;font:16px/1.7 system-ui,sans-serif}main{max-width:1080px;margin:auto;padding:64px 24px}.eyebrow{font-size:12px;letter-spacing:.18em;color:#60756a}h1{font-size:clamp(30px,5vw,48px);margin:12px 0}header p{max-width:720px;color:#52665b}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,320px),1fr));gap:20px;margin-top:40px}.card{display:block;padding:28px;border:1px solid #d6dfd6;border-radius:16px;background:white;color:inherit;text-decoration:none}.card:hover,.card:focus-visible{border-color:#376d50;box-shadow:0 5px 24px #183c2410}.card h2{margin:12px 0;font-size:24px}.meta{font-size:13px;overflow-wrap:anywhere;color:#637369}footer{margin-top:48px;font-size:14px}footer a{color:#286144}</style></head><body><main><header><span class="eyebrow">LUMYTE / DIAGNOSTICS</span><h1>画面プレビュー</h1><p>診断サーバーをブラウザーで操作して撮影した、実画面のギャラリーです。設定の読み込み・保存、ログ、Traceなどをページ別に確認できます。</p></header><section class="grid" aria-label="公開中のプレビュー">${cards}</section><footer><a href="https://github.com/${escape(metadata.repository)}">GitHubでプロジェクトを見る</a><p>画像内の値は撮影時点のサンプルデータです。</p></footer></main></body></html>\n`);
await writeFile(resolve(site, '.nojekyll'), '');
console.log(`Prepared ${retained.length} previews; updated ${key}`);
