# ADR-0012: bindless 参照の追跡と有限 binding への変換

- 状態: 採用
- 日付: 2026-10-08

## 背景

[ADR-0008](0008-resource-bindings.md) は論理 Argument Tableに descriptor を登録する bindless 型 API を定める。WebGPU では buffer 内の整数を texture／sampler object へ変換できず、任意に大きな登録表を一つの draw へ束縛できない。利用側の API を物理 binding 集合の生成に戻さず、構造体の参照情報から必要な集合を導出する機構が必要になる。

glTF は primitive に一つの material が対応するため、その要素だけを root 引数で指定すれば、その material の texture／sampler を収集できる。一方、同じ draw で複数 material を選ぶ、または buffer を間接参照する場合には、その候補全体を追跡しなければならない。shader の実行結果を CPU が予測する設計にはしない。

本 ADR は要素単位の CPU metadata、到達集合の探索、安定した wire 参照と draw 固有 binding の変換、Slang helper、上限と寿命を扱う。利用側の resource 転送は [ADR-0005](0005-buffer-resource-contract.md) と [ADR-0009](0009-command-buffer.md) の明示操作を維持する。wgpu の実装範囲は [ADR-0011](0011-wgpu-first-backend.md) に記載する。main に未導入の API は本 PR 内で本設計へ更新する。

## 決定

### CPU 側で追跡する情報

backend は Device 内で複数の論理 Argument Table を統合した登録表を持つ。キーは table identity、resource 種別、slot と世代であり、異なる Argument Tableの同じ slot 番号を区別する。論理 Argument Table を一つの巨大な WebGPU bind group として生成することはしない。

serializer は参照 field の pack と同時に、次の情報を CPU metadata へ記録する。型・field path・offset・stride は Slang reflection と schema で検証し、resource identity は serializer が受け取った不透明参照から取得する。任意の raw bytes から参照を推測しない。

- buffer identity／内容世代、schema・wire ABI、登録された byte 範囲と要素数。
- 要素ごとの texture、sampler、raw buffer descriptor と shader data 範囲の参照。
- field の存在有無、期待する resource 型、shader visibility と使用可能な読み取り操作。
- nested data の参照先範囲と、登録された descriptor 世代。

metadata の各要素は「何番目の構造体がどの登録を必要とするか」の対応表になる。係数・色・material 名など、buffer 内容の意味を Core が定義しない。raw numeric buffer の参照はその buffer binding を追加するが、数値を descriptor と解釈しない。参照を持つ shader data 範囲だけを再帰的にたどる。

### pack、コピーと metadata の伝播

CPU pack は利用者の idle な Upload byte 範囲に wire data と metadata を登録する。pack 成功前に全 field・参照・必要 bytes を検証し、失敗時に新しい登録や lease を残さない。buffer の SizeInBytes や count は調整しない。

RecordCopyBuffer は登録済み全要素に整列する完全な範囲のコピーで metadata をコピー先へ対応付ける。配列全体だけでなく、登録済み要素の境界に一致する部分配列も伝播できる。GPU 命令の byte alignment と用途は別途検証する。要素を切るコピー・raw 上書き・GPU 書き込みは重なった要素の metadata を失効させる。範囲を再 pack するまで、その要素の参照解決を拒否する。

記録時の metadata はそのコピーより後の命令にだけ有効な予定の状態とし、対応する依存を利用者が明示する。同じ Encoder の後続 draw は予定の状態を使用できる。別 Submission では完了を観測するか、サポートする明示的 queue 依存を満たす。未送信破棄・送信失敗・DeviceLost は予定の metadata と lease を回収する。古い完了通知で、上書きにより失効した内容世代を復活させない。

### root 引数からの到達集合の構築

1. ShaderArguments の serializer が、root の直接 descriptor 参照と GpuReference を記録する。
2. 単一要素の参照ならその要素だけ、配列範囲の参照なら範囲内の全要素を CPU 対応表から読む。
3. 各要素の参照を収集し、nested shader data 範囲も探索する。探索済みの buffer／世代／範囲を記録し、循環や共有参照で無限探索・重複収集しない。探索サイズと深さの上限超過は診断して拒否する。
4. texture view、sampler、buffer range を別々に deduplicate する。texture は同じ native image でも view format／subresource が異なれば別。sampler は同じ確定設定を安全に共有できるものをまとめる。buffer は同じ allocation・アクセス・互換 layout の範囲だけを backend が安全に統合する。
5. compiled BindingPlan に従って物理配置を決め、参照変換表と bind group を生成・キャッシュする。draw／dispatch 記録時に pipeline、layout、内容世代と全参照を再検証する。

root の整数 material index だけから shader の選択を静的解析して推測しない。CPU が指定できる単一 material は単一要素 GpuReference で表す。GPU が material index を選ぶ draw は候補配列の範囲を渡し、全候補を集合に含める。参照 field が runtime branch で未使用になる可能性があっても、初期契約ではその field の候補を保守的に含める。

buffer に格納した参照を GPU が新たに生成・変更する方式は初期契約の対象外。書き込み可能な shader data を参照解決に使用しない。GPU-driven／indirect draw を後で追加する場合も、CPU に登録した候補集合と有効範囲を別途定める。

### 安定した wire ID と draw 用変換表

wire data は backend 内部の安定した参照 ID を保持する。この ID は Device・登録世代と関連し、Argument Table の論理 slot や draw 用 binding 番号と同一にはしない。複数 draw で物理配置が変わっても material buffer を書き換えない。同じ sampler を異なる texture が共有する場合も、texture ID と sampler ID は独立する。

WebGPU は到達集合に含まれる ID だけを持つ、draw 固有の変換表を GPU に渡す。初期方式は resource 種別ごとに安定 ID を局所 binding 番号順に並べた有限配列とし、Slang helper が検索する。buffer entry は局所 storage binding、base offset、範囲長と必要な型情報を含む。Argument Table 全容量に比例する巨大な dense table を毎 draw 作らない。lookup のコストとキャッシュ効果は実装時に測定する。

単一要素の論理 offset が native storage binding の alignment を満たさない場合は、検証済みの包含範囲を束縛して helper の base offset で位置を表す。包含範囲・上限・アクセスを検証し、論理要素数や SizeInBytes を丸めない。

shader は load した安定 ID を変換表で解決し、compiled helper の switch から具体的な texture／sampler／buffer binding にアクセスする。buffer は range を検査し、schema と一致する load helper で扱う。WGSL resource object を integer cast で生成せず、WGSL が認める具体的な個別 binding の操作へ lowering する。

texture と sampler を独立した番号で sample するため、helper は compiled layout が許す組合せへ lowering する。sample type と sampler category が不適合な組合せは serializer／引数構築でも拒否する。switch の組合せによるコードサイズは容量 variant の設計と検証に含める。fragment では一様な制御フローで求めた gradient を渡す sampleGrad、または明示 LOD の sampleLevel を使い、非一様な branch 内の暗黙 derivative に依存しない。

変換表にない ID、世代不一致、範囲外アクセスは無効 descriptor を参照しない。CPU が検出できる違反は記録前に拒否し、shader helper の防御的な範囲外処理は zero／無効値を返す。これは不正な参照を有効な別の resource に差し替える保証ではない。

### Slang 成果物と binding 容量

compiled artifact は texture の型別容量、sampler のcategory別容量、uniform／storage buffer の容量、stage visibility、予約された root／変換表の binding、wire ID・lookup helper の ABI を記録する。texture と sampler の一対一ペア数を共通契約にしない。glTF の一つの material でも通常5枚の texture を使い得るため、独立した種類別の容量を指定する。

容量は artifact の生成時に固定する。描画時に暗黙のコンパイルや capacity の補正をしない。容量 variant と helper はオフラインで生成して DLL へ埋め込み、WGSL をコミットしない。明示的なオンライン compiler provider も同じ artifact／metadata を生成する。library ABI と cache key は ADR-0010 に従い、参照追跡 schema と lookup ABI の版も含める。

宣言済みで未使用の個別 binding は、同じ型の到達集合にある有効 resource を複製して埋める。該当型の到達 resource がない場合に必要な neutral fallback は、pipeline 作成時に backend が明示された初期化契約の下で用意し、Device／pipeline 内で保持する。ユーザーデータの Upload や material の意味を推測する処理とは分離する。実際に参照される ID がない限り fallback へ sample／load しない。fallback の確保・初期化コストと、失敗時の pipeline 生成エラーを報告する。描画時に fallback texture の確保や user upload を追加しない。

### 上限と失敗

sampled textures、samplers、uniform buffers、storage buffers の stage ごとの上限は独立して検証する。bind group 数、binding 数、buffer binding size、dynamic offset と alignment などの共通制約も検証する。root と変換表、環境 map など material 以外の引数も含め、到達した実 resource 数だけでなく compiled layout の宣言数に対して limits を照合する。

一つの draw の到達集合が artifact の容量を超える場合は、その種類の必要数・容量・Device 上限を診断して記録前に拒否する。自動 split、draw の追加、並べ替え、atlas 化はしない。Renderer は描画順と geometry／material の対応を知るため、必要ならそこで分割する。通常の glTF primitive は一つの material の参照だけを収集できるため、シーン全体の登録数で分割する必要はない。

API 差分の比較元は origin/main（Graphics API は未導入）。物理 binding を操作する公開 API は追加しない。以下は未実装の backend 内部契約の概念的な抜粋。

```diff
+namespace Lumyte.Graphics.Implementation
+{
+    internal interface IBindingLoweringBackendContract
+    {
+        // 複数論理 Argument Tableの登録表を解決。Device／世代／型と有効期間を検証する。
+        ResolvedDescriptor Resolve(DescriptorIdentity identity);
+        // root と要素単位 metadata の到達集合を収集。単一要素と配列範囲を区別する。
+        // shader 実行の予測、GPU readback、GPU work は行わない。
+        DependencySnapshot Collect(RootArgumentSnapshot root, ShaderReferenceSchema schema);
+        // resource 種別ごとに集約し、limits／compiled capacity を検証。
+        // layout・descriptor世代・buffer範囲・使用する変換表の一致をキャッシュキーにする。
+        BindingSnapshot Build(BindingPlan plan, DependencySnapshot dependencies);
+        // 検証済み snapshot と内部変換表を現在の command 記録へ配置する。
+        // 別 Submission、user staging の確保、user data のコピー、待機はしない。
+        void RecordBindings(CommandRecordingContext commands, BindingSnapshot snapshot);
+    }
+}
```

Snapshot／identity／schema／context は backend の非公開概念型であり、共通 Core の buffer 内容として公開しない。Draw／Dispatch は記録前に snapshot を準備し、検証失敗なら命令や lease を残さない。native 記録失敗は ADR-0009 の Faulted 契約に従う。

### 内部データ、キャッシュと寿命

bind group、root 引数、変換表は backend のコマンド用内部データ。backend が現在の記録に必要な内部領域を配置し、同じ Submission で可視になるよう native 同期を扱う。この処理はユーザーバッファの CopyFrom／CopyTo を自動 Upload へ変更するものではない。別送信や隠れた待機で material／texture の未完了 Upload を代行しない。

CPU dependency snapshot は登録世代と対象 buffer metadata を保持する。引数の有効期間から記録まで descriptor の上書きを拒否し、CommandBuffer は実際の resource と変換表を lease する。未送信破棄で記録 lease を返し、送信時は Submission 完了まで保持する。キャッシュは構造と有効な resource identity を区別し、関連世代の失効で entry を除去する。完了前に transient table の領域を再利用しない。

bind group の再利用キーには pipeline layout、resource 型・view・sampler category、native identity・世代、buffer offset／sizeを含める。変換表の再利用には安定 ID と局所番号の対応も一致する必要がある。bind group の物理配置が同じでも ID が異なる場合、古い変換表は再利用しない。

### native backend と Browser

DirectX／Vulkan は ADR-0008 の native descriptor indexing を使用する。同じ不透明参照と metadata の契約で Device／型／世代／範囲・寿命を検証するが、WebGPU 用の有限個別 binding と switch／変換表方式を必須にしない。native wire 表現は native descriptor と Slang の能力に対応させる。必須機能不足を有限 binding へ自動 fallback しない。

wgpu は既存 .NET binding で bind group／内部 buffer を実装し、独自 .Native は不要。Browser は同じ artifact・schema・lookup ABI を使用し、JS／Wasm 側が登録表と snapshot を管理する。ホストの native compiler を実行時に必須としない。

## 検討した代替案

### draw ごとに material buffer の descriptor 番号を書き換える

共有する buffer と同時進行の draw の整合性を壊し、暗黙 Upload も必要になる。stable wire ID と draw 用変換表を分け、既存 material buffer を変更しない。

### shader ソースから実際に使用する material を推測する

反射で分かるのは型・配置・宣言であり、runtime の分岐と index の値は決まらない。serializer の意味付けと型付き範囲参照で候補集合を明示する。

### 利用側が各 draw の binding 集合を指定する

不透明参照を渡すだけで到達集合を収集し、物理配置・重複排除は backend に任せる。利用側に物理集合の構築を要求しない。

## 結果と影響

- 利用者は論理 Argument Tableへの登録と root data の参照指定だけで、必要な binding を生成できる。
- 単一要素参照はその material の依存だけを集め、配列参照は GPU が選ぶ候補全体を安全に集める。
- CPU metadata、内容世代、安定 wire ID、draw 用変換表と Slang helper の整合性が必要になる。
- WebGPU の宣言数・型別容量は残る。シーン全登録数と一つの draw の到達数を混同しない。
- lookup、snapshot 構築、内部 table とキャッシュのコストが追加される。性能は実測する。
- 20枚のサンプルは、20登録の Argument Table と一つの material 配列から単一要素の root 参照を渡して描画する。利用側で binding 集合を分割しない。

## 検証方針

共通 API のみを使用し、将来の実装で以下を確認する。

- 20個以上の texture をArgument Table へ登録し、単一要素参照のdrawが対応する一枚だけを解決する。buffer の同じ stable ID が異なるdraw配置でも正しい画像を指す。
- 一つの material の5枚以上のtexture、samplerの共有、複数の Argument Tableの同じslot、独立したbuffer参照とnested data、循環・共有参照を扱う。参照数を種類別に検証する。
- 単一要素の到達数と候補配列の到達数、capacityの境界と超過、宣言数によるDevice limits違反、layout・view型・sampling適合・別Device違反を拒否する。
- 完全要素コピーでのmetadata伝播、部分byte上書き・GPU書き込みでの失効、未送信破棄・DeviceLost・古い完了通知・slot再利用を検証する。
- 同じmaterial bufferを異なるdraw／in-flight Submissionで使っても、stable IDや変換表が書き換わらない。lease、キャッシュ失効、内部tableの完了前再利用拒否を確認する。
- shaderのsampleGrad／sampleLevel、buffer範囲検査、未知IDの安全な処理、WGSL uniformity、オフラインDLL埋め込みと明示オンラインcompilerの同一ABIを確認する。
- 登録・pack・参照追跡でuser upload／GPUコピー／Submit／待機を実行せず、drawの自動分割や順序変更も行わないことを検証する。

対応範囲ごとに共通 API の GPU テストで確認し、実行結果は PR に記録する。未対応の nested schema、Browser と native backend を検証済みとは扱わない。

## 参考資料

- [論理 Argument Table と bindless API](0008-resource-bindings.md)
- [バッファと明示的コピー](0005-buffer-resource-contract.md)
- [コマンドバッファと送信完了](0009-command-buffer.md)
- [Slang コンパイルと data layout](0010-shader-compilation-and-data-interop.md)
- [wgpu の対応範囲](0011-wgpu-first-backend.md)
- [WebGPU limits](https://www.w3.org/TR/webgpu/#limits)
