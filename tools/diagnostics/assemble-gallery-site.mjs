import { lstat, mkdir, mkdtemp, readFile, readdir, rename, rm, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { generateGallery } from './generate-gallery.mjs';

const maxJsonBytes = 1024 * 1024;
const maxSiteBytes = 500 * 1024 * 1024;
const statuses = new Set(['queued', 'in_progress', 'completed', 'waiting', 'requested', 'pending']);
const conclusions = new Set(['success', 'failure', 'neutral', 'cancelled', 'skipped', 'timed_out', 'action_required', 'stale', 'startup_failure']);

function text(value, name, limit = 300) {
    if (typeof value !== 'string' || !value.trim() || value.length > limit || /[\u0000-\u001f\u007f]/u.test(value)) {
        throw new Error(`${name} must be nonempty text of at most ${limit} characters.`);
    }
    return value;
}

function sha(value, name) {
    if (typeof value !== 'string' || !/^[a-fA-F0-9]{40}$/u.test(value)) throw new Error(`${name} must be a full commit SHA.`);
    return value.toLowerCase();
}

function positiveInteger(value, name) {
    if (!Number.isSafeInteger(value) || value < 1) throw new Error(`${name} must be a positive safe integer.`);
    return value;
}

function timestamp(value, name) {
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/u.test(value) ||
        !Number.isFinite(Date.parse(value)) || new Date(value).toISOString() !== value) {
        throw new Error(`${name} must be an ISO timestamp.`);
    }
    return value;
}

function html(value) {
    return value.replace(/[&<>"']/gu, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]);
}

function containsPath(parent, child) {
    const difference = relative(parent, child);
    return difference === '' || (difference !== '..' && !difference.startsWith(`..${sep}`) && !isAbsolute(difference));
}

async function rejectSymlinks(path, allowMissing = false) {
    const parts = resolve(path).split(sep);
    let current = parts.shift() || sep;
    for (const part of parts) {
        current = join(current, part);
        try {
            if ((await lstat(current)).isSymbolicLink()) throw new Error(`Symbolic links are not allowed: ${current}`);
        } catch (error) {
            if (allowMissing && error.code === 'ENOENT') return;
            throw error;
        }
    }
}

async function readJson(path) {
    await rejectSymlinks(path);
    const info = await lstat(path);
    if (!info.isFile() || info.size > maxJsonBytes) throw new Error(`Expected JSON file of at most 1 MiB: ${path}`);
    const contents = await readFile(path);
    if (contents.length > maxJsonBytes) throw new Error(`JSON file exceeds 1 MiB: ${path}`);
    return JSON.parse(contents.toString('utf8'));
}

async function requireEmptyOutput(output) {
    await rejectSymlinks(output, true);
    try {
        if ((await readdir(output)).length) throw new Error('Output directory must be empty to avoid publishing stale files.');
    } catch (error) {
        if (error.code !== 'ENOENT') throw error;
    }
}

function normalizeCatalog(raw) {
    if (!raw || raw.version !== 1 || typeof raw.repository !== 'string' || !/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/u.test(raw.repository) ||
        !Array.isArray(raw.entries) || raw.entries.length > 101) {
        throw new Error('Catalog requires version 1, an owner/repository name and at most 101 entries.');
    }
    const keys = new Set();
    const entries = raw.entries.map(entry => {
        if (!entry || typeof entry.key !== 'string' || !/^(?:main|pr\/[1-9][0-9]*)$/u.test(entry.key) || keys.has(entry.key)) {
            throw new Error('Catalog entry keys must be unique main or pr/<number> paths.');
        }
        keys.add(entry.key);
        if (entry.key === 'main' ? entry.number !== null : positiveInteger(entry.number, 'PR number') !== Number(entry.key.slice(3))) {
            throw new Error('PR number must match its entry key; main must have a null number.');
        }
        let latestRun = null;
        if (entry.latestRun !== null) {
            if (!entry.latestRun || !statuses.has(entry.latestRun.status) ||
                (entry.latestRun.conclusion !== null && !conclusions.has(entry.latestRun.conclusion))) {
                throw new Error('Invalid latest CI status or conclusion.');
            }
            latestRun = {
                id: positiveInteger(entry.latestRun.id, 'Latest run id'),
                status: entry.latestRun.status,
                conclusion: entry.latestRun.conclusion,
                headCommit: sha(entry.latestRun.headCommit, 'Latest run headCommit'),
            };
        }
        let preview = null;
        if (entry.preview !== null) {
            if (!entry.preview || typeof entry.preview.directory !== 'string' ||
                !/^galleries\/[1-9][0-9]*-[1-9][0-9]*$/u.test(entry.preview.directory)) {
                throw new Error('Preview directory must be galleries/<runId>-<attempt>.');
            }
            const runId = positiveInteger(entry.preview.runId, 'Preview runId');
            const [directoryRunId, attempt] = entry.preview.directory.slice('galleries/'.length).split('-').map(Number);
            if (runId !== directoryRunId || !Number.isSafeInteger(attempt)) throw new Error('Preview directory must match its runId and safe attempt.');
            preview = { directory: entry.preview.directory, runId, headCommit: sha(entry.preview.headCommit, 'Preview headCommit') };
        }
        return {
            key: entry.key, number: entry.number, title: text(entry.title, 'Entry title'),
            headCommit: sha(entry.headCommit, 'Entry headCommit'), latestRun, preview,
            ...(entry.unavailableReason === undefined ? {} : { unavailableReason: text(entry.unavailableReason, 'Unavailable reason', 1200) }),
        };
    }).sort((left, right) => left.key === 'main' ? -1 : right.key === 'main' ? 1 : right.number - left.number);
    return { version: 1, repository: raw.repository, generatedAt: timestamp(raw.generatedAt, 'generatedAt'), entries };
}

function formatDate(value) {
    return new Intl.DateTimeFormat('ja-JP', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Tokyo' }).format(new Date(value));
}

function runStatus(run) {
    if (!run) return { label: 'CI 未実行', kind: 'pending' };
    if (run.status !== 'completed') return {
        label: run.status === 'in_progress' ? 'CI 実行中' : 'CI 待機中', kind: 'pending',
    };
    const label = { success: 'CI 成功', failure: 'CI 失敗', cancelled: 'CI キャンセル', timed_out: 'CI タイムアウト',
        action_required: 'CI 承認待ち', skipped: 'CI スキップ', neutral: 'CI 完了', stale: 'CI 期限切れ', startup_failure: 'CI 起動失敗' }[run.conclusion] ?? 'CI 完了';
    return { label, kind: run.conclusion === 'success' ? 'success' : 'attention' };
}

function renderIndex(site) {
    const repositoryUrl = `https://github.com/${site.repository}`;
    const commitLink = commit => `<a class="commit" href="${repositoryUrl}/commit/${commit}">${commit.slice(0, 7)}</a>`;
    const cards = site.entries.map(entry => {
        const status = runStatus(entry.latestRun);
        const sourceUrl = entry.number === null ? `${repositoryUrl}/tree/main` : `${repositoryUrl}/pull/${entry.number}`;
        const label = entry.number === null ? 'main' : `PR #${entry.number}`;
        const preview = entry.preview;
        const stale = preview && preview.headCommit !== entry.headCommit;
        const previousRun = preview && entry.latestRun && preview.runId !== entry.latestRun.id;
        const notice = !preview ? entry.unavailableReason ?? '取得できる画面はまだありません。CI の完了後に更新されます。' :
            stale ? '以前のコミットの画面です。現在のコミットの撮影が完了するまで、取得済みの画面を表示します。' :
                previousRun ? '最新 CI の画面はまだ取得できません。同じコミットの以前の撮影を表示します。' : '現在のコミットで撮影した画面を確認できます。';
        return `<article class="preview-card${preview ? '' : ' unavailable'}">
                <div class="card-heading"><a class="ref-label" href="${sourceUrl}">${label} ↗</a><span class="badge ${status.kind}">${status.label}</span></div>
                <h2><a href="${sourceUrl}">${html(entry.title)}</a></h2>
                <dl><div><dt>現在のコミット</dt><dd>${commitLink(entry.headCommit)}</dd></div>${entry.latestRun ? `<div><dt>最新 CI</dt><dd><a href="${repositoryUrl}/actions/runs/${entry.latestRun.id}">#${entry.latestRun.id} ↗</a> · ${commitLink(entry.latestRun.headCommit)}</dd></div>` : ''}${preview ? `
                    <div><dt>撮影元のコミット</dt><dd>${commitLink(preview.headCommit)}</dd></div>
                    <div><dt>撮影対象</dt><dd>${commitLink(preview.commit)}${entry.number === null ? '' : ' <span class="detail">（PR マージ結果）</span>'}</dd></div>
                    <div><dt>撮影日時</dt><dd><time datetime="${preview.capturedAt}">${html(formatDate(preview.capturedAt))} JST</time></dd></div>
                    <div><dt>撮影した CI</dt><dd><a href="${repositoryUrl}/actions/runs/${preview.runId}">#${preview.runId} ↗</a></dd></div>` : ''}</dl>
                <p class="notice${stale || previousRun ? ' previous' : ''}">${html(notice)}</p>${preview && entry.unavailableReason ? `<p class="reason">${html(entry.unavailableReason)}</p>` : ''}
                ${preview ? `<a class="open-gallery" href="${entry.key}/">${preview.screenshotCount} 画面を見る <span aria-hidden="true">→</span></a>` : '<span class="no-gallery">画面の公開待ち</span>'}
            </article>`;
    }).join('\n');
    return `<!doctype html>
<html lang="ja">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'self'; base-uri 'none'; form-action 'none'; object-src 'none'">
    <meta name="color-scheme" content="light">
    <meta name="description" content="Lumyte 診断 UI の main と各 PR の画面プレビュー。撮影したコミットと CI の状態を確認できます。">
    <title>画面プレビュー一覧 · Lumyte 診断 UI</title>
    <link rel="stylesheet" href="site.css">
</head>
<body>
    <a class="skip-link" href="#previews">プレビュー一覧へ移動</a>
    <div class="shell">
        <header>
            <div class="masthead"><a class="brand" href="${repositoryUrl}"><span class="brand-mark" aria-hidden="true">L</span>Lumyte <span class="brand-divider">/</span> Diagnostics</a><span class="status-pill">画面プレビュー</span></div>
            <div class="hero"><p class="eyebrow">DIAGNOSTICS PREVIEWS</p><h1>PR ごとの画面を、見比べる。</h1><p class="intro">main と公開中の PR の診断画面を確認できます。<br>各画面は、実際に起動した診断サーバーのスクリーンショットです。</p></div>
            <div class="summary"><span>${site.entries.length} 件 · ${site.entries.filter(entry => entry.preview).length} 件の画面を公開中</span><span>一覧更新 <time datetime="${site.generatedAt}">${html(formatDate(site.generatedAt))} JST</time></span></div>
        </header>
        <main id="previews" tabindex="-1" aria-label="プレビュー一覧"><div class="preview-grid">${cards || '<p class="empty-state">公開対象のプレビューはまだありません。</p>'}</div></main>
        <footer><span>Lumyte · Game engine diagnostics</span><span>PR を閉じると、次の一覧更新で掲載を終了します。</span></footer>
    </div>
</body>
</html>
`;
}

const siteCss = `:root { color-scheme: light; --ink: #172b36; --muted: #5d6d76; --line: #dbe3e5; --accent: #006d68; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans JP", "Yu Gothic UI", sans-serif; }
* { box-sizing: border-box; }
body { margin: 0; background: #f5f7f6; color: var(--ink); line-height: 1.7; }
a { color: var(--accent); text-underline-offset: 4px; }
:focus-visible { outline: 3px solid #008a83; outline-offset: 4px; }
.shell { max-width: 1280px; margin: auto; padding: 0 48px; }
.skip-link { position: fixed; z-index: 2; top: 12px; left: 12px; padding: 8px 16px; background: white; transform: translateY(-150%); }
.skip-link:focus { transform: none; }
.masthead { display: flex; align-items: center; justify-content: space-between; min-height: 92px; border-bottom: 1px solid var(--line); gap: 16px; }
.brand { display: flex; gap: 10px; align-items: center; font-weight: 650; font-size: 15px; color: var(--ink); text-decoration: none; }
.brand-mark { display: grid; place-content: center; width: 30px; height: 30px; background: var(--accent); color: white; border-radius: 9px; font-weight: 800; }
.brand-divider { color: #a6b4b8; }
.status-pill, .badge { font-size: 12px; border-radius: 999px; padding: 4px 11px; white-space: nowrap; }
.status-pill, .success { color: var(--accent); background: #e2efeb; border: 1px solid #cde0da; }
.pending { color: #556271; background: #edf1f7; border: 1px solid #d7dfe9; }
.attention { color: #965221; background: #fff1e4; border: 1px solid #f1d5b9; }
.hero { padding: 52px 0 34px; }
.eyebrow { font-size: 11px; letter-spacing: 2px; font-weight: 750; color: var(--accent); margin: 0 0 12px; }
h1 { font-size: clamp(27px, 3vw, 42px); letter-spacing: -.04em; line-height: 1.35; margin: 0 0 20px; }
.intro { margin: 0; font-size: 15px; color: var(--muted); line-height: 1.95; }
.summary { display: flex; justify-content: space-between; flex-wrap: wrap; gap: 8px; margin-bottom: 22px; font-size: 12px; color: var(--muted); }
.preview-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 24px; }
.preview-card { min-width: 0; display: flex; flex-direction: column; padding: 25px; border: 1px solid var(--line); border-radius: 14px; background: white; box-shadow: 0 3px 10px #172b3604; }
.card-heading { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 12px; }
.ref-label { font-size: 13px; font-weight: 650; text-decoration: none; }
h2 { margin: 18px 0; font-size: 20px; line-height: 1.55; overflow-wrap: anywhere; }
h2 a { color: var(--ink); text-decoration: none; }
dl { margin: 0; font-size: 12px; }
dl > div { display: flex; gap: 12px; margin: 6px 0; }
dt { color: var(--muted); flex: 0 0 110px; }
dd { margin: 0; overflow-wrap: anywhere; }
.commit { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
.detail { color: var(--muted); font-size: 11px; }
.notice { color: var(--muted); font-size: 12px; background: #f5f7f6; border-radius: 8px; padding: 12px 14px; margin: 18px 0; overflow-wrap: anywhere; }
.notice.previous { background: #fff5e9; color: #805322; }
.reason { color: var(--muted); font-size: 12px; margin: -5px 0 18px; overflow-wrap: anywhere; }
.open-gallery { display: flex; justify-content: space-between; padding: 11px 15px; border-radius: 8px; background: var(--accent); color: white; font-size: 13px; font-weight: 650; text-decoration: none; margin-top: auto; }
.open-gallery:hover { background: #005c57; }
.no-gallery { color: var(--muted); padding: 11px 0; font-size: 13px; margin-top: auto; }
.unavailable { background: #fcfdfc; }
.empty-state { font-size: 14px; color: var(--muted); }
footer { display: flex; justify-content: space-between; flex-wrap: wrap; gap: 12px; padding: 30px 0; margin-top: 44px; border-top: 1px solid var(--line); color: var(--muted); font-size: 11px; }
@media (max-width: 760px) { .shell { padding: 0 20px; } .masthead { min-height: 74px; } .brand { font-size: 13px; gap: 7px; } .status-pill { font-size: 10px; } .hero { padding: 35px 0 26px; } .intro { font-size: 13px; } .summary { flex-direction: column; } .preview-grid { grid-template-columns: 1fr; gap: 20px; } .preview-card { padding: 20px; } h2 { font-size: 18px; } dl > div { gap: 8px; } dt { flex-basis: 100px; } footer { margin-top: 28px; } }
`;

async function directoryBytes(path) {
    let total = 0;
    for (const entry of await readdir(path, { withFileTypes: true })) {
        total += entry.isDirectory() ? await directoryBytes(join(path, entry.name)) : (await lstat(join(path, entry.name))).size;
    }
    return total;
}

export async function assembleGallerySite(catalogPath, outputDirectory) {
    const input = resolve(catalogPath);
    const parent = dirname(input);
    const output = resolve(outputDirectory);
    if (containsPath(parent, output) || containsPath(output, parent)) throw new Error('Catalog input and output directories must not overlap.');
    await requireEmptyOutput(output);
    const catalog = normalizeCatalog(await readJson(input));
    await mkdir(dirname(output), { recursive: true });
    const staging = await mkdtemp(join(dirname(output), '.diagnostics-site-'));
    try {
        const entries = [];
        let bytes = 0;
        for (const entry of catalog.entries) {
            let preview = null;
            if (entry.preview) {
                const directory = join(parent, entry.preview.directory);
                const metadata = await readJson(join(directory, 'metadata.json'));
                const manifest = await readJson(join(directory, 'gallery.json'));
                if (!metadata || metadata.version !== 1 || metadata.repository !== catalog.repository ||
                    sha(metadata.sourceCommit, 'Metadata sourceCommit') !== entry.preview.headCommit ||
                    metadata.capturedAt !== manifest?.capturedAt || metadata.screenshotCount !== manifest?.screenshots?.length) {
                    throw new Error(`Preview metadata does not match its catalog or gallery: ${entry.key}`);
                }
                const commit = sha(metadata.commit, 'Metadata commit');
                const capturedAt = timestamp(metadata.capturedAt, 'Metadata capturedAt');
                await generateGallery(directory, join(staging, entry.key), {
                    repository: catalog.repository, commit, sourceCommit: entry.preview.headCommit,
                    label: entry.number === null ? 'main' : `PR #${entry.number}`, homeHref: entry.number === null ? '../' : '../../',
                });
                bytes += await directoryBytes(join(staging, entry.key));
                if (bytes > maxSiteBytes) throw new Error('Combined site size exceeds 500 MiB.');
                preview = { runId: entry.preview.runId, headCommit: entry.preview.headCommit, commit, capturedAt, screenshotCount: metadata.screenshotCount };
            }
            entries.push({ ...entry, preview });
        }
        const site = { ...catalog, entries };
        const files = { 'index.html': renderIndex(site), 'site.css': siteCss, 'site.json': JSON.stringify(site, null, 2) + '\n' };
        bytes += Object.values(files).reduce((total, contents) => total + Buffer.byteLength(contents), 0);
        if (bytes > maxSiteBytes) throw new Error('Combined site size exceeds 500 MiB.');
        for (const [name, contents] of Object.entries(files)) await writeFile(join(staging, name), contents);
        await requireEmptyOutput(output);
        await rm(output, { recursive: true, force: true });
        await rename(staging, output);
        return { count: entries.length, previewCount: entries.filter(entry => entry.preview).length, bytes, output };
    } finally {
        await rm(staging, { recursive: true, force: true });
    }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    try {
        if (process.argv.length !== 4) throw new Error('Usage: node tools/diagnostics/assemble-gallery-site.mjs <catalog.json> <empty-site-directory>');
        const result = await assembleGallerySite(process.argv[2], process.argv[3]);
        console.log(`Generated ${result.previewCount} galleries for ${result.count} entries (${result.bytes} bytes) in ${result.output}`);
    } catch (error) {
        console.error(error.message);
        process.exitCode = 1;
    }
}
