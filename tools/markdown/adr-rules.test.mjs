import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import fs from "node:fs";
import { createRequire } from "node:module";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { after, test } from "node:test";
import checks from "./adr-checks.cjs";

// Use the same markdownlint dependency as the repository's pinned CLI.
const cli = process.env.MARKDOWNLINT_CLI2 || execFileSync("mise", ["which", "markdownlint-cli2"], { encoding: "utf8" }).trim();
const require = createRequire(fs.realpathSync(cli));
const { lint } = await import(pathToFileURL(require.resolve("markdownlint/sync")));
const { default: MarkdownIt } = await import(pathToFileURL(require.resolve("markdown-it")));
const root = fs.mkdtempSync(path.join(os.tmpdir(), "lumyte-adr-lint-"));
after(() => fs.rmSync(root, { recursive: true, force: true }));
fs.mkdirSync(path.join(root, "docs/adr/sample"), { recursive: true });
fs.writeFileSync(path.join(root, "docs/adr/sample/SAMPLE-0001-previous.md"), "# Prior decision\n");
const name = path.join(root, "docs/adr/sample/SAMPLE-0002-change.md");
const sha = "fbc81051cbf0f4aa5d996237543765e02b8cfa5c";
const valid = `# ADR-SAMPLE-0002: 呼び出しを更新境界で反映する

- 状態: 提案
- 日付: 2026-11-02
- 基準コミット: \`${sha}\`

## 背景

利用者は次の更新で反映する操作を区別したい。

## 決定

予約を明示する名前を使う。

### 利用者の変更前・変更後

変更前は Apply、変更後は Queue を呼び出す。

\`\`\`csharp
// PR #123 and https://example.com are example strings, not prose references.
service.Queue(value);
\`\`\`

### API 差分

\`\`\`diff
-public void Apply(int value);
+public void Queue(int value);
\`\`\`

## 検討した代替案

即時更新は更新境界を変えるため採用しない。

## 結果と影響

呼び出し名を更新する必要がある。
`;

function errors(content, { file = name, legacy = { files: {} } } = {}) {
  return lint({
    strings: { [file]: content },
    config: { default: false, LMADR001: true, LMADR002: true, LMADR003: true },
    markdownItFactory: () => new MarkdownIt({ html: true }),
    customRules: checks.createRules({ repositoryRoot: root, legacy }),
    noInlineConfig: true,
  })[file];
}
const codes = (content, options) => errors(content, options).map(({ ruleNames }) => ruleNames[0]);
const expect = (content, code) => assert(codes(content).includes(code), JSON.stringify(errors(content)));

test("valid ADR is checked by the actual Markdown parser", () => assert.deepEqual(errors(valid), []));
test("non-ADR Markdown is outside custom rule scope", () => {
  assert.deepEqual(errors("# Notes\nPR #108\n", { file: path.join(root, "docs/notes.md") }), []);
});
test("filename/category and title must agree", () => {
  expect(valid.replace("ADR-SAMPLE-0002:", "ADR-0002:"), "LMADR001");
  assert(codes(valid, { file: path.join(root, "docs/adr/sample/OTHER-0002-change.md") }).includes("LMADR001"));
});
test("duplicate IDs are detected even if the other file is archived", () => {
  const duplicate = path.join(root, "docs/adr/sample/SAMPLE-0002-duplicate.md");
  fs.writeFileSync(name, valid);
  fs.writeFileSync(duplicate, "# Existing\n");
  try { expect(valid, "LMADR001"); }
  finally { fs.unlinkSync(duplicate); fs.unlinkSync(name); }
});
test("status cannot mix adoption and implementation", () => expect(valid.replace("状態: 提案", "状態: 採用・実装済み"), "LMADR001"));
test("dates must be real and baseline must be a fixed SHA", () => {
  expect(valid.replace("2026-11-02", "2026-02-30"), "LMADR001");
  expect(valid.replace(sha, "origin/main"), "LMADR001");
  expect(valid.replace(sha, "abcd"), "LMADR001");
  assert.deepEqual(errors(valid.replace(sha, "fbc8105")), []);
});
test("metadata belongs before the first section", () => {
  const line = "- 状態: 提案\n";
  expect(valid.replace(line, "").replace("## 背景", `## 背景\n\n${line}`), "LMADR001");
});
test("fenced templates cannot supply missing headings", () => {
  const content = valid.replace("## 結果と影響", "## 影響") + "\n```markdown\n## 結果と影響\n```\n";
  expect(content, "LMADR001");
});
test("required headings cannot be duplicated or reversed", () => {
  expect(valid.replace("## 背景", "## 決定"), "LMADR001");
  expect(valid + "\n## 背景\n\n追記\n", "LMADR001");
});
test("before/after must be in Decision and precede API diff", () => {
  expect(valid.replace("### 利用者の変更前・変更後", "### 使用例"), "LMADR002");
  expect(valid.replace("### 利用者の変更前・変更後", "```diff\n+public void Run();\n```\n\n### 利用者の変更前・変更後"), "LMADR002");
  expect(valid.replace("### 利用者の変更前・変更後", "## 利用者の変更前・変更後"), "LMADR002");
});
test("internal changes can explain unchanged calling code without fake comparisons", () => {
  const content = valid.replace(/変更前は Apply[^\n]+/, "利用者の記述は変わらない。呼び出し後の割り当てを減らす。");
  assert.deepEqual(errors(content), []);
});
test("concrete PR and issue references are rejected; generic policy terms are allowed", () => {
  for (const reference of ["PR #108", "Issue 108", "main/#105", "#106 の変更"]) {
    expect(valid.replace("予約を明示する名前を使う。", reference), "LMADR003");
  }
  assert.deepEqual(errors(valid + "\nPR や Issue の経緯を本文に持ち込まない。\n"), []);
});
test("relative ADR references must exist", () => {
  assert.deepEqual(errors(valid + "\n[以前の判断](SAMPLE-0001-previous.md)\n"), []);
  expect(valid + "\n[将来の判断](SAMPLE-0003-missing.md)\n", "LMADR003");
});
test("external references, reports, and mutable source links are rejected", () => {
  for (const href of ["https://example.com/design", "../../performance/report.md", "../../../src/Thing.cs", "https://github.com/ikihiki/Lumyte/blob/main/src/Thing.cs"]) {
    expect(valid + `\n[参考](${href})\n`, "LMADR003");
  }
});
test("baseline-pinned source links are allowed, including autolinks", () => {
  const url = `https://github.com/ikihiki/Lumyte/blob/${sha}/src/Thing.cs#L12`;
  assert.deepEqual(errors(valid + `\n[コード](${url})\n<${url}>\n`), []);
  expect(valid + `\n[別時点](${url.replace(sha, "1234567")})\n`, "LMADR003");
});
test("HTML links and images cannot bypass reference checks", () => {
  expect(valid + '\n<a href="https://example.com">参考</a>\n', "LMADR003");
  expect(valid + "\n![図](image.png)\n", "LMADR003");
});
test("frozen legacy bodies are exempt only while unchanged, across line endings", () => {
  const old = "# Old ADR\n\n- 状態: 採用・実装済み\n\nPR #108\n";
  const legacy = { files: { "docs/adr/sample/SAMPLE-0002-change.md": checks.hashContent(old) } };
  assert.deepEqual(errors(old, { legacy }), []);
  assert.deepEqual(errors(old.replaceAll("\n", "\r\n"), { legacy }), []);
  assert(codes(old + "\nNew text\n", { legacy }).includes("LMADR001"));
  assert(codes("---\nlegacy: true\n---\n" + old, { legacy }).includes("LMADR001"));
});
test("comment edits do not preserve the legacy exemption", () => {
  const old = "# Old ADR\n\nPR #108\n";
  const legacy = { files: { "docs/adr/sample/SAMPLE-0002-change.md": checks.hashContent(old) } };
  assert(codes(old + "\n<!-- added -->\n", { legacy }).includes("LMADR001"));
});

test("pinned CLI loads the rules and fails an invalid ADR", () => {
  const checksPath = fs.realpathSync(new URL("./adr-checks.cjs", import.meta.url));
  fs.writeFileSync(path.join(root, "custom.cjs"),
    `module.exports = require(${JSON.stringify(checksPath)}).createRules({ repositoryRoot: ${JSON.stringify(root)} });\n`);
  fs.writeFileSync(path.join(root, ".markdownlint-cli2.jsonc"), JSON.stringify({
    config: { default: false, LMADR001: true, LMADR002: true, LMADR003: true },
    customRules: ["./custom.cjs"],
    globs: ["docs/adr/sample/SAMPLE-0002-change.md"],
  }));
  fs.writeFileSync(name, valid);
  const run = () => spawnSync(process.execPath, [fs.realpathSync(cli)], { cwd: root, encoding: "utf8" });
  const success = run();
  assert.equal(success.status, 0, success.stdout + success.stderr);
  fs.writeFileSync(name, valid + "\nPR #108 を参照する。\n");
  const failure = run();
  assert.equal(failure.status, 1, failure.stdout + failure.stderr);
});
