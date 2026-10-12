# ADR-DIAGNOSTICS-0003: 操作ペイロード上限と高頻度入力の通信枠

- 状態: 採用（プロトコル変更を実装）
- 日付: 2026-10-11

## 背景

[入力の診断設計](../input/INPUT-0004-diagnostics-and-overrides.md)のTapeチャンクと設定ツリーは4096文字を超える。[DIAGNOSTICS-0001](DIAGNOSTICS-0001-diagnostics-transport.md)の実装は、カタログのMaxLengthにかかわらず全Stringを4096文字へ制限していた。また、ライブ入力を通常操作として送ると、完了済み要求を含む256件の保持枠を短時間で使い切り、心拍や停止まで受付不能になる。

通常操作の重複排除を維持しながら、宣言された大容量フィールドと高頻度入力を扱うため、操作通信の上限・受付・版の契約を変更する。本ADRはDIAGNOSTICS-0001の操作ペイロード制限と受付枠を具体化・変更する。テレメトリーの収集契約は維持する。

## 決定

### カタログに基づくサイズ検証

操作引数・成功結果のStringは宣言されたDiagnosticField.MaxLengthに従い、未指定なら4096文字とする。宣言できる最大値は1,048,576文字。未宣言フィールド、欠けたフィールド、型・範囲・長さの不一致はサーバーとゲーム側の両方で拒否する。拒否・競合結果に成功結果のフィールドを混入させない。

HTTPとMagicOnionの操作は同じ検証を使う。個別フィールドの文字数上限とは別に、符号化した操作要求全体を4 MiBまでとする。JSONのエスケープで増えたサイズも含むため、1 MiBの文字列ならどんな内容でも送れるという保証ではない。ゲームからのpublicationも既存の4 MiB上限に従う。Log / Metric / TraceのStringは引き続き4096文字までとし、操作向けの上限を適用しない。

Tapeの32 KiBチャンクはMaxLengthを明示して送る。設定ツリーは既存Settingsカタログの1 MiB宣言を使う。Agentは結果送信前にも全体のサイズを検証し、上限を超える成功結果を小さなresult-too-large拒否へ置き換える。JSONとMessagePackの上限を保守的に見積もり、大きな結果ひとつで診断接続を切断しない。

### 操作種別ごとの受付と保持

OperationDescriptor.TrafficにStandard・Stream・Controlを追加する。クライアントの要求は種別を指定できず、サーバーとゲームのPumpは公開カタログから判定する。RequiredPermissionは独立して検証し、Controlを指定しても権限は増えない。

| 種別 | 用途 | サーバー保留数 | サーバー保持数 | 要求ペイロード保持枠 |
| --- | --- | --- | --- | --- |
| Standard | 設定保存、Tapeチャンクと再生開始、通常照会 | 64 | 256 | 8 MiB |
| Stream | 連番付きライブ入力、周期的な現在値照会 | 48 | 1024 | 2 MiB |
| Control | 心拍、停止、解放 | 16 | 256 | 1 MiB |

枠はSessionと種別ごとに独立させ、StandardやStreamの満杯がControlの受付を拒否する原因にならないようにする。ゲームのPumpも保留枠をStandard 192・Stream 48・Control 16、保持枠をStandard 1024・Stream 1024・Control 256に分ける。Controlを先に実行し、StreamとStandardは交互に進める。HTTPの配送でも保留中のControlを先に取り出す。処理中のハンドラーは途中中断せず、所有スレッドの実行予算は維持する。

版2のAgentは結果待ちの要求があっても次の要求を受信し、最大128件をPumpへ提出できる。同一RequestIdの処理中の再配送はまとめ、期限を変換し直して別要求にしない。HTTPは版2で未応答要求の再配送を100 ms間隔にし、その間にも新しい要求とControlを配送する。これにより通信受信側で先行操作の結果を待ってControlの到着を止めない。版1のAgentは従来の逐次受信を維持する。

Streamの要求期限は最大1秒とし、完了結果の重複排除情報もその期限まで保持する。期限を延長してキャッシュを保持し続けることはできない。StandardとControlのサーバー側保持は従来どおり要求期限に30秒を加える。全種別で保持中の同じRequestIdを別内容・別種別へ再利用したら拒否する。未失効の要求を容量確保のために勝手に追い出さない。Pumpで実行中の要求は期限を超えても完了するまでIDと保持枠を維持し、二重実行を防ぐ。

Streamは期限が過ぎた要求を自動再送しない。期限後には重複排除情報を捨てるため、ライブサービスもSessionのバッチ連番で古い入力を拒否する。途中のPress/Releaseを失った場合は停止し、次の最新値から履歴を捏造しない。ゲーム側での処理が追いつかない場合はbusy/expiredを返し、無制限に待ち行列を伸ばさない。

通常操作のRequestId保持を短縮することで全体の負荷を吸収せず、明示されたStreamだけに短い保持を使う。結果の保持、通信バッファ、Telemetryの既存の件数・メッセージサイズ上限も維持する。

### 互換性

拡張を使うClientHelloはProtocolVersion 2とする。AgentはカタログにStream/Control、または4096を超えるMaxLengthがあれば2を選ぶ。それ以外は1を維持する。サーバーは1と2を受け付け、版1で拡張機能を宣言したカタログは拒否する。版2を拒否されたAgentは版1へ自動フォールバックせず、ライブ操作を通常操作として黙って扱わない。

JSONはTraffic未指定をStandardとする。MagicOnionはWireOperationの末尾へキーを追加し、旧カタログにキーがなければStandardになる。既存フィールドの番号・意味は変えない。

### 公開 API 一覧

比較元は本PR取り込み前のmain（c1f7c1d）。

```diff
 namespace Lumyte.Diagnostics
 {
+    public enum DiagnosticOperationTraffic { Standard, Stream, Control }
-    public sealed record OperationDescriptor(string Id, string DisplayName,
-        DiagnosticPermission RequiredPermission, DiagnosticField[] Arguments,
-        DiagnosticField[] Results, bool RequiresRevision = false);
+    public sealed record OperationDescriptor(string Id, string DisplayName,
+        DiagnosticPermission RequiredPermission, DiagnosticField[] Arguments,
+        DiagnosticField[] Results, bool RequiresRevision = false,
+        DiagnosticOperationTraffic Traffic = DiagnosticOperationTraffic.Standard);
     public sealed class DiagnosticOperationAttribute : Attribute
     {
+        // 生成カタログへ反映する。既定はStandard。
+        public DiagnosticOperationTraffic Traffic { get; set; }
     }
 }
 namespace Lumyte.Diagnostics.Transport.MagicOnion
 {
     public sealed partial class WireOperation
     {
+        [Key(6)]
+        public int Traffic { get; set; }
     }
 }
```

### 検証

32 KiBチャンクと1 MiB設定ツリーのHTTP/MagicOnion往復、schema既定4096・宣言上限・符号化4 MiB上限・未宣言フィールド・型/範囲の拒否、Telemetryの4096制限を確認する。60 Hzを超える件数の連続Stream、同一RequestIdの重複排除、種別を変えた再利用拒否、通常枠飽和時のControl受付・優先実行、版1/2と旧MessagePackカタログの互換も検証する。Agentを通した複数要求とControlの配送、大きすぎる結果の拒否後の接続継続、HTTPの版2再配送、実行中に期限を超えた要求の重複排除も確認する。

## 検討した代替案

### Tapeを4096文字の通常要求へ分割する

1 MiBで少なくとも256要求となり、通常要求の保持枠を使い切る。文字列全体の制限をカタログ宣言と一致させ、より大きな有界チャンクを使う。

### 完了した通常要求を即座に破棄する

応答が失われた後の再送で設定保存やリース操作を重複実行する。短い保持はバッチ連番などの独立した再送防止を備えるStreamだけに限定する。

### 別の入力専用ソケットを設ける

将来の帯域要求によっては検討できるが、現段階では既存の認証・カタログ・HTTP/MagicOnion経路へ有界の種別別受付を追加する。ブラウザーをゲームへ直接接続しない。

## 結果と影響

大きな操作ペイロードを宣言どおり送信でき、ライブ入力が通常操作や停止用の保持枠を消費しなくなる。既存の全体サイズ制限と重複排除を維持し、要求種別はゲームが公開したカタログに従う。

版2の対応が必要になり、独自クライアントはStream期限・連番・再送契約を実装する必要がある。上限はバックプレッシャーを提供するもので、ハンドラーの実行時間やネットワーク遅延に関係なく一定フレームレートを保証するものではない。
