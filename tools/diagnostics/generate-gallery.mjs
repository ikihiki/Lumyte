import { lstat, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import { isAbsolute, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const maxManifestBytes = 1024 * 1024;
const maxImageBytes = 32 * 1024 * 1024;
const maxTotalImageBytes = 200 * 1024 * 1024;
const pngSignature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);

function escapeHtml(value) {
    return value.replace(/[&<>"']/g, character => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
    })[character]);
}

function requireText(value, name, maxLength) {
    if (typeof value !== 'string' || value.trim().length === 0 || value.length > maxLength || /[\u0000-\u0008\u000b\u000c\u000e-\u001f]/u.test(value)) {
        throw new Error(`${name} must be nonempty text of at most ${maxLength} characters.`);
    }
    return value;
}

function containsPath(parent, child) {
    const difference = relative(parent, child);
    return difference === '' || (!difference.startsWith(`..${sep}`) && difference !== '..' && !isAbsolute(difference));
}

async function rejectSymlinks(path, allowMissing = false) {
    const absolute = resolve(path);
    const parts = absolute.split(sep);
    let current = parts.shift() || sep;
    for (const part of parts) {
        current = join(current, part);
        try {
            if ((await lstat(current)).isSymbolicLink()) {
                throw new Error(`Symbolic links are not allowed: ${current}`);
            }
        } catch (error) {
            if (allowMissing && error.code === 'ENOENT') {
                return;
            }
            throw error;
        }
    }
}

async function readRegularFile(path, maximumBytes) {
    await rejectSymlinks(path);
    const info = await lstat(path);
    if (!info.isFile() || info.size > maximumBytes) {
        throw new Error(`Expected a regular file of at most ${maximumBytes} bytes: ${path}`);
    }
    const contents = await readFile(path);
    if (contents.length > maximumBytes) {
        throw new Error(`File exceeds its size limit: ${path}`);
    }
    return contents;
}

function readPngDimensions(contents, name) {
    if (contents.length < 45 || !contents.subarray(0, 8).equals(pngSignature) ||
        contents.readUInt32BE(8) !== 13 || contents.toString('ascii', 12, 16) !== 'IHDR') {
        throw new Error(`Invalid PNG image: ${name}`);
    }
    const width = contents.readUInt32BE(16);
    const height = contents.readUInt32BE(20);
    if (width === 0 || height === 0 || width > 12000 || height > 20000 || width * height > 80000000) {
        throw new Error(`PNG dimensions exceed the gallery limits: ${name}`);
    }
    return { width, height };
}

function renderHtml(manifest, metadata, screenshots) {
    const pages = [...new Set(screenshots.map(screenshot => screenshot.page))];
    const capturedAt = new Intl.DateTimeFormat('ja-JP', {
        dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Tokyo',
    }).format(new Date(manifest.capturedAt));
    const commitUrl = `https://github.com/${metadata.repository}/commit/${metadata.commit}`;
    const cards = screenshots.map((screenshot, index) => `
            <article class="card" data-page="${escapeHtml(screenshot.page)}" data-viewport="${screenshot.viewport}" id="${screenshot.id}">
                <a class="screenshot" href="${screenshot.file}" data-zoom aria-label="「${escapeHtml(screenshot.title)}」を拡大表示">
                    <img src="${screenshot.file}" alt="${escapeHtml(screenshot.title)}（診断画面）" width="${screenshot.width}" height="${screenshot.height}" loading="${index < 2 ? 'eager' : 'lazy'}" decoding="async">
                    <span class="zoom-hint" aria-hidden="true">拡大表示 ↗</span>
                </a>
                <div class="card-body">
                    <div class="card-meta"><span>${escapeHtml(screenshot.page)}</span><span>${screenshot.viewport === 'mobile' ? '狭い画面' : 'PC'}</span></div>
                    <h2>${escapeHtml(screenshot.title)}</h2>
                    <p>${escapeHtml(screenshot.description)}</p>
                </div>
            </article>`).join('');
    return `<!doctype html>
<html lang="ja">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'self'; script-src 'self'; base-uri 'none'; form-action 'none'; object-src 'none'">
    <meta name="color-scheme" content="light">
    <meta name="description" content="Lumyte 診断 UI の実画面を、ページと画面幅ごとに確認できるスクリーンショットギャラリーです。">
    <title>${escapeHtml(metadata.label)} · Lumyte 診断 UI</title>
    <link rel="stylesheet" href="gallery.css">
    <script src="gallery.js" defer></script>
</head>
<body>
    <a class="skip-link" href="#gallery">画面一覧へ移動</a>
    <div class="shell">
        <header>
            <div class="masthead"><a class="brand" href="https://github.com/${metadata.repository}"><span class="brand-mark" aria-hidden="true">L</span>Lumyte <span class="brand-divider">/</span> Diagnostics</a><span class="status-pill">画面プレビュー</span></div>
            <div class="hero">
                <div><p class="eyebrow">DIAGNOSTICS GALLERY</p><h1>診断画面を、ひと目で。</h1><p class="intro">設定の編集から、ログ・トレースの調査まで。<br>実際に起動した診断サーバーの画面を確認できます。</p></div>
                <div class="preview-meta"><span class="preview-label">${escapeHtml(metadata.label)}</span><a href="${commitUrl}" class="commit-link">${metadata.commit.slice(0, 7)} <span aria-hidden="true">↗</span></a><span>撮影 <time datetime="${manifest.capturedAt}">${escapeHtml(capturedAt)} JST</time></span></div>
            </div>
            <p class="capture-note"><span aria-hidden="true">●</span> 実画面のスクリーンショットです。表示内容と操作結果は撮影時点のものです。画像を選ぶと拡大できます。</p>
        </header>
        <main>
            <div class="toolbar">
                <div class="section-title"><h2>画面一覧</h2><output id="visible-count" aria-live="polite">${screenshots.length} 画面</output></div>
                <div class="filters"><label>ページ<select id="page-filter"><option value="">すべてのページ</option>${pages.map(page => `<option value="${escapeHtml(page)}">${escapeHtml(page)}</option>`).join('')}</select></label><label>画面幅<select id="viewport-filter"><option value="">すべての画面幅</option><option value="desktop">PC</option><option value="mobile">狭い画面</option></select></label><button class="reset-button" id="reset-filters" type="button">リセット</button></div>
            </div>
            <div class="gallery" id="gallery" tabindex="-1">${cards}
            </div>
            <p class="empty-state" id="empty-state" hidden>この組み合わせの画面はありません。ページまたは画面幅を変更してください。</p>
        </main>
        <footer><span>Lumyte · Game engine diagnostics</span><a href="${commitUrl}">撮影対象のソースコードを見る ↗</a></footer>
    </div>
    <dialog id="image-dialog" aria-labelledby="dialog-title" aria-describedby="dialog-description">
        <div class="dialog-header"><h2 id="dialog-title"></h2><button id="close-dialog" class="close-button" type="button" aria-label="拡大表示を閉じる">閉じる ×</button></div>
        <div class="dialog-image"><img id="full-image" alt=""></div>
        <div class="dialog-footer"><p id="dialog-description"></p><a id="original-image" target="_blank" rel="noopener">画像を別タブで開く ↗</a></div>
    </dialog>
</body>
</html>
`;
}

const galleryCss = `:root { color-scheme: light; --ink: #172b36; --muted: #5d6d76; --line: #dbe3e5; --accent: #006d68; --paper: #f5f7f6; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans JP", "Yu Gothic UI", sans-serif; }
* { box-sizing: border-box; }
body { margin: 0; background: var(--paper); color: var(--ink); line-height: 1.7; }
a { color: var(--accent); text-underline-offset: 4px; }
button, select { font: inherit; }
button, a, select { -webkit-tap-highlight-color: transparent; }
:focus-visible { outline: 3px solid #008a83; outline-offset: 4px; }
[hidden] { display: none !important; }
.shell { max-width: 1440px; margin: auto; padding: 0 48px; }
.skip-link { position: fixed; z-index: 2; top: 12px; left: 12px; padding: 8px 16px; background: white; transform: translateY(-150%); }
.skip-link:focus { transform: none; }
.masthead { display: flex; align-items: center; justify-content: space-between; min-height: 92px; border-bottom: 1px solid var(--line); gap: 16px; }
.brand { display: flex; gap: 10px; align-items: center; font-weight: 650; font-size: 15px; color: var(--ink); text-decoration: none; letter-spacing: -.2px; }
.brand-mark { display: grid; place-content: center; width: 30px; height: 30px; background: var(--accent); color: white; border-radius: 9px; margin-right: 2px; font-weight: 800; }
.brand-divider { color: #a6b4b8; padding: 0 3px; }
.status-pill { color: var(--accent); font-size: 12px; background: #e2efeb; border: 1px solid #cde0da; padding: 4px 11px; border-radius: 999px; white-space: nowrap; }
.hero { display: flex; justify-content: space-between; gap: 36px; align-items: center; padding: 54px 0 35px; }
.eyebrow { font-size: 11px; letter-spacing: 2px; font-weight: 750; color: var(--accent); margin: 0 0 12px; }
h1 { font-size: clamp(27px, 3vw, 42px); letter-spacing: -.04em; line-height: 1.35; margin: 0 0 20px; font-weight: 750; }
.intro { margin: 0; font-size: 15px; color: var(--muted); line-height: 1.95; }
.preview-meta { display: flex; flex-direction: column; gap: 6px; align-items: flex-end; min-width: 220px; font-size: 12px; color: var(--muted); }
.preview-label { display: inline-block; font-size: 15px; font-weight: 650; color: var(--ink); overflow-wrap: anywhere; max-width: 360px; text-align: right; }
.commit-link { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; text-decoration: none; }
.capture-note { padding: 13px 16px; border: 1px solid var(--line); border-radius: 10px; background: #fff; font-size: 12px; color: var(--muted); margin: 0 0 34px; }
.capture-note span { color: var(--accent); margin-right: 9px; font-size: 9px; }
.toolbar { display: flex; justify-content: space-between; gap: 20px; align-items: end; margin: 0 0 20px; flex-wrap: wrap; }
.section-title { display: flex; align-items: baseline; gap: 12px; }
.section-title h2 { margin: 0; font-size: 18px; font-weight: 650; }
.section-title output { font-size: 12px; color: var(--muted); }
.filters { display: flex; align-items: end; gap: 10px; }
.filters label { display: flex; flex-direction: column; gap: 4px; color: var(--muted); font-size: 11px; }
select { background: white; color: var(--ink); border: 1px solid #cbd7d9; border-radius: 7px; padding: 8px 28px 8px 11px; font-size: 12px; min-width: 150px; height: 37px; }
.reset-button { cursor: pointer; color: var(--muted); background: transparent; border: 1px solid transparent; padding: 8px; font-size: 12px; height: 37px; }
.reset-button:hover { color: var(--accent); }
.gallery { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 24px; }
.card { border: 1px solid var(--line); border-radius: 14px; background: white; overflow: hidden; box-shadow: 0 3px 10px #172b3604; }
.screenshot { display: flex; justify-content: center; position: relative; aspect-ratio: 16 / 10; overflow: hidden; background: #e9eeef; padding: 13px; border-bottom: 1px solid var(--line); }
.screenshot img { display: block; width: 100%; height: 100%; object-fit: contain; object-position: top center; box-shadow: 0 4px 18px #172b3612; background: white; border-radius: 4px; transition: transform .18s ease; }
.card[data-viewport="mobile"] .screenshot img { width: auto; max-width: 100%; }
.screenshot:hover img { transform: scale(1.015); }
.zoom-hint { position: absolute; right: 21px; bottom: 20px; color: var(--ink); background: #fffffff2; padding: 4px 10px; border: 1px solid var(--line); border-radius: 6px; font-size: 11px; box-shadow: 0 2px 8px #172b360a; }
.card-body { padding: 20px 23px 24px; }
.card-meta { display: flex; justify-content: space-between; gap: 8px; color: var(--muted); font-size: 11px; margin-bottom: 7px; }
.card-meta span:first-child { color: var(--accent); font-weight: 650; }
.card h2 { font-size: 18px; line-height: 1.55; margin: 0 0 7px; letter-spacing: -.02em; }
.card p { font-size: 13px; color: var(--muted); margin: 0; overflow-wrap: anywhere; }
.empty-state { padding: 60px 20px; text-align: center; color: var(--muted); border: 1px dashed var(--line); border-radius: 14px; font-size: 14px; }
footer { display: flex; justify-content: space-between; gap: 20px; padding: 30px 0; margin-top: 44px; border-top: 1px solid var(--line); color: var(--muted); font-size: 11px; }
footer a { text-decoration: none; }
dialog { border: 1px solid var(--line); border-radius: 14px; padding: 0; max-width: 1500px; width: calc(100% - 48px); max-height: calc(100dvh - 48px); color: var(--ink); background: white; box-shadow: 0 16px 80px #001a3040; }
dialog::backdrop { background: #10252fbb; backdrop-filter: blur(4px); }
.dialog-header { display: flex; align-items: center; justify-content: space-between; gap: 15px; padding: 16px 22px; border-bottom: 1px solid var(--line); }
.dialog-header h2 { margin: 0; font-size: 17px; }
.close-button { white-space: nowrap; cursor: pointer; border: 1px solid var(--line); background: white; color: var(--ink); border-radius: 6px; padding: 5px 12px; font-size: 12px; }
.dialog-image { background: #e9eeef; padding: 12px; text-align: center; }
.dialog-image img { display: block; max-width: 100%; max-height: 70dvh; width: auto; height: auto; margin: auto; object-fit: contain; }
.dialog-footer { padding: 16px 22px; display: flex; justify-content: space-between; gap: 20px; align-items: baseline; }
.dialog-footer p { font-size: 12px; color: var(--muted); margin: 0; }
.dialog-footer a { font-size: 12px; flex-shrink: 0; }
@media (min-width: 1550px) { .shell { max-width: 1680px; } .gallery { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
@media (max-width: 760px) { .shell { padding: 0 20px; } .masthead { min-height: 74px; } .brand { font-size: 13px; gap: 7px; } .status-pill { font-size: 10px; } .hero { flex-direction: column; align-items: flex-start; gap: 24px; padding: 35px 0 24px; } .preview-meta { align-items: flex-start; gap: 4px; } .preview-label { text-align: left; } .intro { font-size: 13px; } .capture-note { font-size: 11px; margin-bottom: 28px; } .toolbar { align-items: flex-start; gap: 16px; } .filters { width: 100%; flex-wrap: wrap; gap: 8px; } .filters label { flex: 1; min-width: 0; } select { min-width: 0; width: 100%; padding-right: 20px; } .reset-button { padding-left: 2px; padding-right: 0; } .gallery { grid-template-columns: 1fr; gap: 20px; } .card-body { padding: 17px 19px 21px; } .card h2 { font-size: 17px; } footer { flex-direction: column; gap: 8px; margin-top: 28px; } dialog { width: calc(100% - 20px); max-height: calc(100dvh - 20px); } .dialog-header { padding: 12px; } .dialog-header h2 { font-size: 14px; } .dialog-footer { padding: 12px; flex-direction: column; gap: 8px; } }
@media (prefers-reduced-motion: reduce) { * { transition: none !important; } }
`;

const galleryJs = `const cards = [...document.querySelectorAll('.card')];
const pageFilter = document.getElementById('page-filter');
const viewportFilter = document.getElementById('viewport-filter');
function filterCards() {
    let count = 0;
    for (const card of cards) {
        card.hidden = Boolean((pageFilter.value && card.dataset.page !== pageFilter.value) || (viewportFilter.value && card.dataset.viewport !== viewportFilter.value));
        if (!card.hidden) count += 1;
    }
    document.getElementById('visible-count').textContent = count + ' 画面';
    document.getElementById('empty-state').hidden = count !== 0;
}
pageFilter.addEventListener('change', filterCards);
viewportFilter.addEventListener('change', filterCards);
document.getElementById('reset-filters').addEventListener('click', () => {
    pageFilter.value = '';
    viewportFilter.value = '';
    filterCards();
});
const dialog = document.getElementById('image-dialog');
for (const link of document.querySelectorAll('[data-zoom]')) {
    link.addEventListener('click', event => {
        if (!dialog.showModal || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        const card = link.closest('.card');
        document.getElementById('dialog-title').textContent = card.querySelector('h2').textContent;
        document.getElementById('dialog-description').textContent = card.querySelector('.card-body p').textContent;
        const image = document.getElementById('full-image');
        image.src = link.href;
        image.alt = link.querySelector('img').alt;
        document.getElementById('original-image').href = link.href;
        dialog.showModal();
    });
}
document.getElementById('close-dialog').addEventListener('click', () => dialog.close());
dialog.addEventListener('click', event => {
    const bounds = dialog.getBoundingClientRect();
    if (event.target === dialog && (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom)) dialog.close();
});
`;

export async function generateGallery(inputDirectory, outputDirectory, options = {}) {
    const input = resolve(inputDirectory);
    const output = resolve(outputDirectory);
    if (containsPath(input, output) || containsPath(output, input)) {
        throw new Error('Input and output directories must not overlap.');
    }
    const repository = options.repository ?? process.env.GITHUB_REPOSITORY;
    const commit = options.commit ?? process.env.PREVIEW_COMMIT;
    const label = requireText(options.label ?? process.env.PREVIEW_LABEL ?? 'UI プレビュー', 'PREVIEW_LABEL', 160);
    if (typeof repository !== 'string' || !/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/u.test(repository)) {
        throw new Error('GITHUB_REPOSITORY must be an owner/repository name.');
    }
    if (typeof commit !== 'string' || !/^[a-fA-F0-9]{40}$/u.test(commit)) {
        throw new Error('PREVIEW_COMMIT must be a full 40-character commit SHA.');
    }
    const manifest = JSON.parse((await readRegularFile(join(input, 'gallery.json'), maxManifestBytes)).toString('utf8'));
    if (!manifest || manifest.version !== 1 || typeof manifest.capturedAt !== 'string' ||
        !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/u.test(manifest.capturedAt) ||
        !Number.isFinite(Date.parse(manifest.capturedAt)) || new Date(manifest.capturedAt).toISOString() !== manifest.capturedAt ||
        !Array.isArray(manifest.screenshots) || manifest.screenshots.length < 1 || manifest.screenshots.length > 100) {
        throw new Error('gallery.json must have version 1, an ISO capturedAt timestamp, and 1–100 screenshots.');
    }
    const ids = new Set();
    const paths = new Set();
    const screenshots = [];
    let totalImageBytes = 0;
    for (const screenshot of manifest.screenshots) {
        if (!screenshot || typeof screenshot.id !== 'string' || !/^[a-z0-9][a-z0-9-]{0,119}$/u.test(screenshot.id) || ids.has(screenshot.id)) {
            throw new Error('Each screenshot must have a unique lowercase slug id.');
        }
        if (typeof screenshot.file !== 'string' || !/^screenshots\/[a-z0-9][a-z0-9-]{0,119}\.png$/u.test(screenshot.file) || paths.has(screenshot.file)) {
            throw new Error('Each screenshot must reference a unique screenshots/<slug>.png path.');
        }
        if (screenshot.viewport !== 'desktop' && screenshot.viewport !== 'mobile') {
            throw new Error(`Invalid viewport for ${screenshot.id}.`);
        }
        const title = requireText(screenshot.title, 'Screenshot title', 160);
        const description = requireText(screenshot.description, 'Screenshot description', 1200);
        const page = requireText(screenshot.page, 'Screenshot page', 80);
        const contents = await readRegularFile(join(input, screenshot.file), maxImageBytes);
        const dimensions = readPngDimensions(contents, screenshot.file);
        totalImageBytes += contents.length;
        if (totalImageBytes > maxTotalImageBytes) {
            throw new Error('Combined screenshot size exceeds 200 MiB.');
        }
        ids.add(screenshot.id);
        paths.add(screenshot.file);
        screenshots.push({ id: screenshot.id, title, description, page, viewport: screenshot.viewport, file: screenshot.file, ...dimensions, contents });
    }
    // Validate everything before writing, and never copy the input tree: browser logs
    // and failure artifacts may contain data that is not intended for publication.
    await rejectSymlinks(output, true);
    try {
        if ((await readdir(output)).length !== 0) {
            throw new Error('Output directory must be empty to avoid publishing stale files.');
        }
    } catch (error) {
        if (error.code !== 'ENOENT') {
            throw error;
        }
    }
    await mkdir(join(output, 'screenshots'), { recursive: true });
    for (const screenshot of screenshots) {
        await writeFile(join(output, screenshot.file), screenshot.contents);
    }
    await writeFile(join(output, 'gallery.css'), galleryCss);
    await writeFile(join(output, 'gallery.js'), galleryJs);
    await writeFile(join(output, 'index.html'), renderHtml(manifest, { repository, commit, label }, screenshots));
    await writeFile(join(output, 'metadata.json'), JSON.stringify({
        version: 1, repository, commit, label, capturedAt: manifest.capturedAt, screenshotCount: screenshots.length,
    }, null, 2) + '\n');
    return { count: screenshots.length, bytes: totalImageBytes, output };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    try {
        if (process.argv.length !== 4) {
            throw new Error('Usage: node tools/diagnostics/generate-gallery.mjs <browser-output-directory> <website-directory>');
        }
        const result = await generateGallery(process.argv[2], process.argv[3]);
        console.log(`Generated ${result.count} screenshot previews (${result.bytes} image bytes) in ${result.output}`);
    } catch (error) {
        console.error(error.message);
        process.exitCode = 1;
    }
}
