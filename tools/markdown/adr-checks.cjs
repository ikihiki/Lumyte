const { createHash } = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");

const requiredHeadings = ["背景", "決定", "検討した代替案", "結果と影響"];
const statuses = new Set(["提案", "採用", "却下", "廃止", "置換済み"]);
const shaPattern = /^[0-9a-f]{7,40}$/;

function hashContent(content) {
  return createHash("sha256").update(content.replace(/\r\n?/g, "\n")).digest("hex");
}

function isSourcePath(value) {
  return /^(?:src|tests|native|build|tools|samples|benchmarks|\.github|\.devcontainer|\.fallout)\//.test(value)
    ? !/\.(?:md|markdown|log|csv|gz|png|jpg|svg|pdf)$/i.test(value)
    : /^[^/]+\.(?:cs|csproj|slnx|props|targets|json|jsonc|toml|lock|config)$/.test(value);
}

function createRules({ repositoryRoot, legacy = { files: {} } }) {
  const root = path.resolve(repositoryRoot);

  function context(params) {
    const file = path.resolve(params.name);
    const relative = path.relative(root, file).split(path.sep).join("/");
    if (!relative.startsWith("docs/adr/") || !relative.endsWith(".md")) return null;
    // ADRs in the frozen manifest have no front matter or HTML comments.
    // Normalizing line endings lets the same archive work on Windows.
    if (!params.frontMatterLines.length &&
        legacy.files[relative] === hashContent(params.lines.join("\n"))) return null;
    const tokens = params.parsers.markdownit.tokens;
    const headings = [];
    for (let index = 0; index < tokens.length; index++) {
      const token = tokens[index];
      if (token.type === "heading_open" && token.level === 0) {
        headings.push({ tag: token.tag, text: tokens[index + 1].content, line: token.lineNumber });
      }
    }
    return { file, relative, tokens, headings };
  }

  const rule = (number, name, description, check) => ({
    names: [`LMADR${number}`, name],
    description,
    tags: ["adr"],
    parser: "markdownit",
    function(params, onError) {
      const doc = context(params);
      if (doc) check(doc, params, (line, detail) => onError({ lineNumber: Math.max(1, Math.min(params.lines.length, line || 1)), detail }));
    },
  });

  return [
    rule("001", "adr-structure", "ADR の識別子・メタデータ・必須見出しを揃える", (doc, params, error) => {
      const parts = doc.relative.slice("docs/adr/".length).split("/");
      const category = parts.length === 2 && /^[a-z]+(?:-[a-z]+)*$/.test(parts[0]) ? parts[0] : null;
      const prefix = category ? `${category.toUpperCase()}-` : "";
      const match = parts.at(-1).match(new RegExp(`^(${prefix}\\d{4})-[a-z0-9]+(?:-[a-z0-9]+)*\\.md$`));
      if (!match || (parts.length !== 1 && !category)) {
        error(1, "ファイル名を直下の NNNN-description.md または category/CATEGORY-NNNN-description.md に揃える。");
      } else {
        const title = doc.headings.filter(({ tag }) => tag === "h1");
        if (title.length !== 1 || !title[0].text.startsWith(`ADR-${match[1]}: `)) {
          error(1, `タイトルを ADR-${match[1]}: 題名 に揃える。`);
        }
        if (fs.existsSync(path.dirname(doc.file))) {
          const sameId = fs.readdirSync(path.dirname(doc.file))
            .filter((entry) => entry.startsWith(`${match[1]}-`) && entry.endsWith(".md"));
          if (sameId.length > 1) error(1, `識別子 ${match[1]} が重複している: ${sameId.join(", ")}`);
        }
      }
      if (params.frontMatterLines.length) error(1, "ADR のメタデータはタイトル直後に記載する。YAML front matter は使用しない。");
      const firstSection = doc.headings.find(({ tag }) => tag === "h2")?.line || params.lines.length + 1;
      const titleEnd = doc.headings.find(({ tag }) => tag === "h1")?.line || 0;
      // Parse actual list text only; fenced templates cannot satisfy metadata.
      const metadata = doc.tokens.filter((token) => token.type === "inline" &&
        token.lineNumber > titleEnd && token.lineNumber < firstSection);
      const field = (name) => metadata.filter((token) => token.content.startsWith(`${name}: `));
      const state = field("状態");
      if (state.length !== 1 || !statuses.has(state[0].content.slice("状態: ".length))) {
        error(state[0]?.lineNumber || titleEnd + 1, "状態は 提案・採用・却下・廃止・置換済み のいずれか一つにする。");
      }
      const date = field("日付");
      const value = date[0]?.content.slice("日付: ".length) || "";
      if (date.length !== 1 || !/^\d{4}-\d{2}-\d{2}$/.test(value) ||
          !Number.isFinite(Date.parse(value)) || new Date(value).toISOString().slice(0, 10) !== value) {
        error(date[0]?.lineNumber || titleEnd + 1, "日付に実在する YYYY-MM-DD を記載する。");
      }
      const baseline = field("基準コミット");
      const sha = baseline[0]?.content.match(/^基準コミット: `([0-9a-f]+)`(?:$|[\s（(])/);
      if (baseline.length !== 1 || !sha || !shaPattern.test(sha[1])) {
        error(baseline[0]?.lineNumber || titleEnd + 1, "基準コミットを 7〜40 桁の固定 SHA（バッククォートで囲む）で記載する。");
      }
      const sections = doc.headings.filter(({ tag }) => tag === "h2");
      const order = sections.filter(({ text }) => requiredHeadings.includes(text)).map(({ text }) => text);
      if (order.join("|") !== requiredHeadings.join("|")) {
        error(firstSection, `必須見出しを一度ずつ、この順に置く: ${requiredHeadings.join(" → ")}`);
      }
    }),
    rule("002", "adr-user-change", "利用者の変更前後を API 差分より先に説明する", (doc, params, error) => {
      const decision = doc.headings.find(({ tag, text }) => tag === "h2" && text === "決定");
      if (!decision) return;
      const end = doc.headings.find(({ tag, line }) => tag === "h2" && line > decision.line)?.line || Infinity;
      const usage = doc.headings.find(({ tag, text, line }) =>
        tag === "h3" && text === "利用者の変更前・変更後" && line > decision.line && line < end);
      if (!usage) {
        error(decision.line, "決定の中に「### 利用者の変更前・変更後」を置く。内部変更は利用側の記述が変わらない旨を説明する。");
        return;
      }
      const nextHeading = doc.headings.find(({ line }) => line > usage.line)?.line || end;
      if (!params.lines.slice(usage.line, nextHeading - 1).some((line) => line.trim())) {
        error(usage.line, "見出しだけでなく、変更前後または利用側の記述が変わらない旨を説明する。");
      }
      const firstDiff = doc.tokens.find((token) => token.type === "fence" && /^diff(?:\s|$)/.test(token.info));
      if (firstDiff && firstDiff.lineNumber < usage.line) error(firstDiff.lineNumber, "API 差分より先に利用者の変更前後を示す。");
    }),
    rule("003", "adr-references", "ADR の参照を過去 ADR と固定したソースに限定する", (doc, params, error) => {
      const baselineToken = doc.tokens.find((token) => token.type === "inline" && /^基準コミット: /.test(token.content));
      const baseline = baselineToken?.content.match(/^基準コミット: `([0-9a-f]{7,40})`/)?.[1];
      const checkLink = (href, line) => {
        if (href.startsWith("#")) return;
        if (/^[a-z][a-z0-9+.-]*:/i.test(href) || href.startsWith("//")) {
          const source = href.match(/^https:\/\/github\.com\/ikihiki\/Lumyte\/blob\/([0-9a-f]{7,40})\/([^?#]+)(?:#.*)?$/);
          if (!source || !isSourcePath(source[2])) {
            error(line, "外部資料・PR・測定文書へのリンクは使用せず、過去 ADR または SHA 固定のソースを示す。");
          } else if (baseline && !(source[1].startsWith(baseline) || baseline.startsWith(source[1]))) {
            error(line, "ソースのリンクは基準コミットの SHA に固定する。");
          }
          return;
        }
        let destination;
        try { destination = decodeURIComponent(href.split(/[?#]/, 1)[0]); }
        catch { error(line, "参照先の URL エンコードが不正。"); return; }
        const target = path.resolve(path.dirname(doc.file), destination);
        const relative = path.relative(root, target).split(path.sep).join("/");
        if (!relative.startsWith("docs/adr/") || !relative.endsWith(".md")) {
          error(line, "相対リンクは過去 ADR だけにする。ソースへのリンクは SHA で固定する。");
        } else if (!fs.existsSync(target)) {
          error(line, `参照先 ADR が存在しない: ${destination}`);
        }
      };
      for (const token of doc.tokens) {
        if (token.type === "html_block" && /(?:href|src)\s*=/i.test(token.content)) {
          error(token.lineNumber, "参照を検査できる Markdown のリンク形式を使用する。");
        }
        if (token.type !== "inline") continue;
        let linkDepth = 0;
        for (const child of token.children || []) {
          if (child.type === "link_open") {
            linkDepth++;
            checkLink(child.attrGet("href"), token.lineNumber);
          }
          if (child.type === "link_close") linkDepth--;
          if (child.type === "image") error(token.lineNumber, "画像ファイルではなく本文中の説明・コード・図で判断を示す。");
          if (child.type === "html_inline" && /(?:href|src)\s*=/i.test(child.content)) {
            error(token.lineNumber, "参照を検査できる Markdown のリンク形式を使用する。");
          }
          if (child.type === "text" && (/(?:\b(?:PR|Issue)\s*#?\s*\d+|(?:^|[\s/（(])#\d+\b)/i.test(child.content) ||
              (!linkDepth && /https?:\/\/\S+/i.test(child.content)))) {
            error(token.lineNumber, "PR・Issue 番号や裸の URL を前提にせず、基準時点の事実を説明する。");
          }
        }
      }
    }),
  ];
}

module.exports = { createRules, hashContent };
