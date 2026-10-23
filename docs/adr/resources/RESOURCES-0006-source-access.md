# ADR-RESOURCES-0006: ファイルの直接読み込みとDLL埋め込み入力

- 状態: 提案
- 日付: 2026-10-23

## 背景

大きなファイル群をPipelineへ入力する際、取得やRevision確認のたびに全量コピー・hash計算を行うと、I/Oと一時容量が入力サイズに比例して増える。一方、編集可能な元ファイルを直接読む場合は、読み込み中の変更を正常なデータとして扱わない境界が必要になる。

[Source契約](RESOURCES-0001-typed-pipelines.md) をファイルシステムとDLL埋め込みデータへ接続し、取得元ごとの整合性、範囲読み込み、所有権を定める。入力の歴史的な版を保存する責務はSourceへ持たせない。

## 決定

### 配置と構成

ファイルシステム固有の実装は `src/Platform/Lumyte.Resources.FileSystem/`、埋め込み実装は `src/Core/Lumyte.Resources.Embedded/` に配置する。どちらも `IResourceSource` を実装し、Stageは具象Sourceに依存せず `ResourceBuildContext.OpenSourceAsync` で入力を借りる。Sourceは入力を提供し、デコードと変換後のメモリ計上はStageが担当する。

Sourceはconstructorで不変の構成を受け取る。DIではoptionsを登録し、名前付きResourcesの `UseSource<TSource>()` に接続する。DIを使わない場合も同じconstructorで作り、Managerへ渡す。Sourceは自らファイルを書き込んだりDLLをロードしたりしない。

### FileSystemのRevisionと変更検出

`FileResourceSource` はroot内の元ファイルを直接開く。`OpenSnapshotAsync` が返すsnapshotは、観測した長さ・作成時刻・最終更新時刻を保持する入力descriptorである。Revisionはこの三値からなる不透明なtokenとし、呼び出し側は内容を解釈せず比較だけに使う。`GetRevisionAsync` はpayloadを読まずに同じメタデータを確認し、ファイルが存在しなければnullを返す。

readerは `FileShare.ReadWrite | FileShare.Delete` で開き、writerやatomic replacementを妨げない。reader開始時と各readの前後に、開いたhandleと現在のpathのメタデータをdescriptorと比較する。変更・置換・削除を検出した場合は `IOException` 系の例外で失敗する。観測した範囲の途中で予期しない終端に達した場合も `EndOfStreamException` とする。

失敗したreadはdestinationを変更している場合があるため、呼び出し側はその内容を使用してはならない。再試行する場合は新descriptorを取得し、構築または読み込み操作全体をやり直す。構築済みのCPU/GPU出力をLeaseで保持することと、元ファイルの旧内容を保持することは別の契約である。

この検出はbest effortである。同じ長さ・作成時刻・更新時刻を保つ変更は見逃し得る。時刻だけの変更で再構築される場合もある。read前後の確認はファイル全体の不変性や原子的な読み取りを証明しない。全量コピー、内容hash、CAS、旧版保存は行わない。

### rootとパス

rootは既存directoryを要求し、絶対化して保持する。ResourceIdから解決したpathがroot外へ出る場合は引数例外で拒否する。rootの祖先と入力pathに存在するsymbolic link・reparse pointは `IOException` とし、読み込み開始時にも確認する。

アプリケーションがrootのdirectory treeを管理する。portableなリンク検査はpath確認とopenの間の差し替えを原子的に防ぐ機構ではなく、敵対的な同時変更に対する隔離境界としては使わない。

### Embeddedの識別と寿命

`EmbeddedResourceSource` は既にロードされたAssemblyと、ResourceIdから正確なmanifest名へのaliasを受け取る。alias辞書はconstructorでコピーする。未登録IDはその値をmanifest名として使用し、slashとdotの暗黙変換は行わない。manifest名の照合はcase-sensitiveである。

RevisionはAssemblyのManifestModuleのMVIDとmanifest名から構成し、同じAssembly内では固定する。欠落時の `GetRevisionAsync` はnull、`OpenSnapshotAsync` は `FileNotFoundException` を返す。指定Assemblyへ直接埋め込まれたresourceだけを扱い、外部リンクresourceや別Assemblyへの参照は `NotSupportedException` とする。元streamはseek可能でなければならない。

Source・snapshot・readerはAssembly参照を保持する。snapshotを破棄しても既存readerがAssemblyを保持し、collectible Assemblyのアンロードで入力が消えることを防ぐ。DLLのロード・交換・アンロードはアプリケーションの責任であり、Sourceが自動的に別Assemblyへ切り替わることはない。

### 範囲・非同期・解放

両Sourceのsnapshotは長さを公開し、`CanReadRange` はtrueとする。`OpenReadAsync(offset, length)` は独立cursorを持つ読み取り専用streamを返す。offsetは0以上かつ入力長以下、明示したlengthは正数で入力範囲内を要求する。length省略時は末尾までを返し、空入力と末尾位置は空のstreamとして扱う。

streamの `Length` と `Position` は要求範囲に相対的であり、Seekもその範囲内に限定する。範囲外Seekは `IOException`、書き込みは `NotSupportedException` とする。各readerは独立して利用できるが、同じreaderへの並行Read・Seekは利用側で直列化する。

snapshotの破棄は新readerの生成を拒否するだけで、開いたreaderを破棄しない。取得したstreamの解放はStageまたは呼び出し側の責任であり、snapshotはManagerの依存寿命に従って解放する。非同期メソッドのtokenは当該操作を取り消す。descriptor取得・Revision確認のメタデータ操作は同期的に完了するため、非同期APIであることを別threadへの配送や処理途中の割り込み保証と解釈しない。

## 公開APIと契約

比較元は `5660924`。Source実装の追加APIを示す。snapshotとstreamの共通契約は [RESOURCES-0001](RESOURCES-0001-typed-pipelines.md) を用いる。

```diff
+namespace Lumyte.Resources.FileSystem
+{
+    public sealed record FileResourceSourceOptions
+    {
+        // 既存root。祖先と入力pathのsymbolic link・reparse pointは拒否する。
+        public required string RootPath { get; init; }
+    }
+    public sealed class FileResourceSource : IResourceSource
+    {
+        public FileResourceSource(FileResourceSourceOptions options);
+        // 全量コピーせず観測メタデータを保持する。欠落入力はIOException系の例外。
+        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default);
+        // payloadを読まない。欠落時はnull。内容hashではない。
+        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default);
+    }
+}
+namespace Lumyte.Resources.Embedded
+{
+    public sealed record EmbeddedResourceSourceOptions
+    {
+        // 既にロードされたAssemblyを借用し、Sourceが保持する。
+        public required Assembly Assembly { get; init; }
+        // 省略時は空辞書。constructorでコピーし、未登録IDは値をそのまま使う。
+        public IReadOnlyDictionary<ResourceId, string> ResourceNames { get; init; } = new Dictionary<ResourceId, string>();
+    }
+    public sealed class EmbeddedResourceSource : IResourceSource
+    {
+        public EmbeddedResourceSource(EmbeddedResourceSourceOptions options);
+        // 指定Assemblyへ直接埋め込まれたresourceを範囲読み込みできる。
+        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default);
+        // MVIDとmanifest名に基づく固定Revision。欠落時はnull。
+        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default);
+    }
+}
```

## 検討した代替案

- 全量コピーによる不変snapshot: 読み込み中の版は固定できるが、取得ごとのI/Oとディスク容量、保持期間と回収方針が必要になる。
- RAMへ全量コピーする: snapshotは単純になるが、未使用範囲を含む大きな入力がメモリ予算を消費する。
- 全量hashをRevisionにする: メタデータより変更を検出しやすいが、Revision確認にも入力サイズに比例するI/Oが必要になる。書き込みとの原子性はhashだけでは得られない。
- manifest名を自動変換する: IDの記述量は減るが、dotとdirectory区切りが曖昧になる。正確な名前と明示aliasを採用する。

## 結果と影響

取得・Revision確認でpayload全体をコピーせず、必要範囲だけ読み込める。FileSystemの各readにはメタデータ確認のコストが加わり、変更を検出した操作は新descriptorで再試行する必要がある。Metadataが同じ変更の完全検出や入力旧版の保存を要求する用途には、別のSource実装が必要になる。

Embeddedは読み込み中の内容を固定できるが、Sourceやreaderの参照がAssemblyの寿命を延ばす。両Sourceともデコード済み値をキャッシュせず、共有とメモリ予算はResourceManagerとStageに集約される。

## 検証方針

`FileResourceSourceTests` で実ファイルの置換・削除・同サイズ上書き・切り詰めによるreader失敗、範囲境界、独立cursor、取消、空入力、descriptor破棄後の既存reader、root逸脱とlink拒否を確認する。大きな入力の末尾範囲を読み、全量コピーやscratchファイルを作らないことも確認する。メタデータを維持する変更の完全検出は受け入れ条件に含めない。

`EmbeddedResourceSourceTests` で実DLLのalias、固定Revision、欠落・空resource、範囲・書き込み拒否、constructor後のalias辞書変更の非伝播、DI接続を確認する。collectible Assemblyを使い、snapshotを返した後もreaderが入力を保持し、利用を終えた後に回収可能になることを検証する。
