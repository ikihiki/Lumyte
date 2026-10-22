# ADR-RESOURCES-0004: ファイルの直接読み込みとDLL埋め込み入力

- 状態: 提案
- 日付: 2026-10-23

## 背景

大きなファイル群をPipelineへ入力する際、取得やRevision確認のたびに全量コピー・hash計算を行いたくない。
実行時のファイル版管理は範囲外とし、直接読み込みと変更検出に限定する。

## 決定

FileSystem Sourceはroot内の元ファイルを直接開く。シンボリックリンクとroot外への参照を拒否する。
OpenSnapshotAsyncは名前を維持するが、入力の長さ・作成時刻・最終更新時刻を観測するdescriptorを返す。
Revisionはこのメタデータを組み合わせた不透明なtokenとし、GetRevisionAsyncもpayloadを読まない。
入力全体のコピー、hash、旧版保存、CAS、実行時の版管理は実装しない。Source自体のRAM取得も不要とする。

readerは独立したcursorと範囲を持ち、元ファイルを共有読み込みする。writerを妨げない。
reader開始時と各readの前後に、開いたhandleと現在のpathのメタデータを確認する。
変更・置換・削除を検出した場合や予期しない終端ではIOExceptionで失敗し、失敗したreadのbufferは破棄する。
再試行は新descriptorで操作全体をやり直す。構築失敗では既存の公開出力を保持する。
ストリーミング中の変更では旧sessionもfaultになり得るため、利用側がsessionを開き直す。
構築済みのCPU/GPU出力のlease保持と、入力ファイルの旧版保存は別の契約である。

検出はbest effortである。同じ長さ・作成時刻・更新時刻を保つ変更は見逃し得る。
時刻だけの変更で再構築される場合もある。ファイル全体の不変性・原子的な読み込みは保証しない。
アプリがrootの寿命を管理し、portableなリンク検査を攻撃者に対する原子的な境界とはしない。
descriptor破棄後は新readerを拒否する。既存readerのhandleはreader自身が所有する。

Embeddedは借用Assemblyと論理ID→manifest名の対応をDIで受け取る。
未登録IDはmanifest名として扱い、slashとdotの暗黙変換はしない。
RevisionはMVIDとmanifest名で固定し、payload全量をコピーせず範囲readerを開く。
snapshotとreaderがAssemblyを保持し、利用中のcollectible Assemblyの回収を防ぐ。
DLLのロード・交換・アンロードはアプリの責任とし、外部リンクresourceは扱わない。
デコード後のメモリはStageのscopeで管理する。

## APIと契約

比較元はこのPRの依存ブランチ。以下は判断対象の追加・変更APIの抜粋。

```diff
+namespace Lumyte.Resources.FileSystem
+{
+    public sealed record FileResourceSourceOptions
+    {
+        public required string RootPath { get; init; }
+    }
+    public sealed class FileResourceSource : IResourceSource
+    {
+        public FileResourceSource(FileResourceSourceOptions options);
+    }
+}
+namespace Lumyte.Resources.Embedded
+{
+    public sealed record EmbeddedResourceSourceOptions
+    {
+        public required Assembly Assembly { get; init; }
+        public IReadOnlyDictionary<ResourceId, string> ResourceNames { get; init; }
+    }
+    public sealed class EmbeddedResourceSource : IResourceSource
+    {
+        public EmbeddedResourceSource(EmbeddedResourceSourceOptions options);
+    }
+}
```

## 検討した代替案

全量コピーによる不変snapshotは取得ごとのI/Oとディスク容量を必要とする。
RAMへのコピーは大きな入力でメモリ予算を消費する。
CASや版管理は保存期間・回収・writerとの協調を別途設計する必要があり、今回は扱わない。
全量hashはRevision確認にもファイルサイズに比例したI/Oが必要なため採用しない。

## 結果と影響

取得・Revision確認はファイルサイズに比例するコピーを行わず、必要範囲だけ読める。
各read前後のメタデータ確認は増える。入力変更時の失敗と再試行を利用側が扱う必要がある。
埋め込みresourceはAssemblyの保持期間を延ばす。

## 検証方針

実ファイルの置換・削除・同サイズ上書き・切り詰めによるreader失敗、範囲、cancel、
大きな入力の末尾だけの読み込み、一時コピーを作らないこと、descriptorとreaderの独立した破棄、
root逸脱・リンク拒否、実DLLの欠落/空resource、aliasコピー、Assemblyの寿命を検証する。

## 関連ADR

- [ADR-RESOURCES-0001](RESOURCES-0001-typed-pipelines.md)
