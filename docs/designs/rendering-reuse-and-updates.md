# 描画データの保持と部分更新

## 目的と位置付け

毎フレーム一部の transform や材質値だけが変わる描画では、全 draw item の再生成、入力全体のコピー、全資源の再列挙、全データの GPU 転送を繰り返さない設計にする。本書は他 library の一次資料と Lumyte の採用方針を整理する設計メモであり、性能の実測報告ではない。

公開契約は [共通 RenderGraph](../adr/0030-render-graph-api.md)、[Model 描画](../adr/0035-model-render-passes.md)、[2D 描画](../adr/0036-2d-render-passes.md) で定める。資産のロードは Lumyte.Resources、ECS・node・Component の管理と変更の抽出は上位が担当する。Graphics は準備済みの描画データを保持し、Native／Portable がそれぞれ GPU 配置・更新・描画を実装する。

## 他 library で確認できる方式

以下は公式文書が説明する方式である。変更履歴で確認した導入経緯と、現在の全実装がそのまま一致するという主張は区別する。

| 対象 | 保持するもの | 更新するもの | 毎フレームの処理 |
| --- | --- | --- | --- |
| Unity DOTS Instancing | GraphicsBuffer 上の instance data | 変化した instance data | draw setup と描画対象の選択は data の設定から分離する |
| Unity Entities Graphics | ECS の描画 component と GPU 側の instance data | component の変更判定に基づく転送 | ECS の変更抽出、culling、batch と draw の処理 |
| Unreal Mesh Drawing Pipeline | 安定した mesh batch と pass 用の draw 記述 | 依存が変わった記述、GPU 側の primitive 値 | 可視 draw の選択、sort／統合、RHI command の記録 |
| Three.js BufferAttribute | geometry の属性データと更新 version | needsUpdate と updateRanges による属性／範囲の転送 | scene の描画処理と属性更新は別に扱う |

### Unity

DOTS Instancing は instance data を GPU 上に保持し、実際に変更されたときに設定する方式を説明している。instance data の準備と draw call の設定を別にすることで、未変更データの再設定を避けられる。これは CPU 側の全 scene 列挙も自動的になくなるという意味ではない。[Unity DOTS Instancing](https://docs.unity3d.com/6000.0/Documentation/Manual/dots-instancing-shaders.html)

Entities Graphics の公式変更履歴には、Hybrid Renderer V2 の導入時に `chunk.DidChange<T>` による差分更新、`SparseUploader`、GPU に常駐するデータを採用したことが記録されている。この記述は 2020 年の設計導入の根拠として使い、現在の packing や転送経路の詳細を固定する資料としては扱わない。[Entities Graphics changelog](https://docs.unity3d.com/Packages/com.unity.entities.graphics@1.4/changelog/CHANGELOG.html#040---2020-03-13)

Entities の change version は chunk 内の component 配列に対応し、書込み可能としてアクセスされた時点で変わる。値が実際に変わったことを保証する比較結果ではない。このため、変更検出の単位によって未変更要素を含む範囲も更新対象になり得る。[Entities version numbers](https://docs.unity3d.com/Packages/com.unity.entities@1.3/manual/systems-version-numbers.html#chunkchangeversion)

Entities Graphics は同じ mesh と material を持つ instance の batch 化を説明する一方、batch 作成の負担や hardware による GPU 常駐データとの相性も挙げている。常駐方式の採用だけで、すべての規模・端末における高速化が決まるわけではない。[Entities Graphics Performance](https://docs.unity3d.com/Packages/com.unity.entities.graphics@1.4/manual/entities-graphics-performance.html)

### Unreal

Mesh Drawing Pipeline は view に依存しない描画記述を保持し、毎フレーム使うものを選ぶ方式を説明している。変化する値を永続 buffer に置けば、その内容の更新と draw 記述の再構築を分離できる。shader や描画状態など、記述の構築時に参照した条件が変わった場合は対応する cache を無効化する。[Unreal Mesh Drawing Pipeline](https://dev.epicgames.com/documentation/en-us/unreal-engine/mesh-drawing-pipeline-in-unreal-engine)

同文書の GPU Scene は、primitive ごとの値を scene 全体の buffer に格納し、PrimitiveID で参照する方式である。保持される draw 記述と、毎フレーム記録される RHI command は別であり、GPU command list 自体の無条件な再提出を意味しない。参照先 resource の置換・破棄も cache の寿命に関係する。文書にある特定の vertex factory や RHI の対応範囲は、そのまま Lumyte の対応制限として採用しない。[同文書の GPU Scene と resource lifetime](https://dev.epicgames.com/documentation/en-us/unreal-engine/mesh-drawing-pipeline-in-unreal-engine#gpu-scene)

### Three.js

BufferAttribute は needsUpdate による変更通知、version、addUpdateRange による更新範囲を持つ。範囲は配列の component 単位で指定し、属性全体の中の一部を GPU へ更新できる。usage は利用傾向の指定であり、最初の利用後に変える場合は別の属性を作る契約である。この属性 API だけで scene 全体の CPU 走査や GPU command 記録が不要になるとは述べられない。[Three.js BufferAttribute](https://threejs.org/docs/pages/BufferAttribute.html)

Lumyte ではこの変更単位の明示を参考にし、Model の WithRange を要素単位で定義する。ただし可変配列と dirty flag を実行中の入力として共有せず、完全な新しい不変世代と変更範囲を受け渡す。GPU cache が対応する基底を持つときだけ部分更新を使い、履歴がなければ完全状態から復元する。

## Lumyte に採用する方針

以下は調査結果を踏まえた Lumyte の設計であり、他 library が同じ API を持つという説明ではない。

### 共通 plan と今回の入力を分ける

共通 plan は機能 pass、logical resource、Read／Write／ReadWrite と出力の構造を保持する。変化する CPU 入力は型付き input slot と提出ごとの bindings で受け渡す。camera や Model の snapshot を差し替えるために graph を構築・Compile し直すことを要求しない。

Read／Write の宣言は plan 内で固定する。input slot の値によって新しい外部 resource を勝手に読み書きしたり、Read を Write へ変えたりしない。外部依存、出力 description、pass 構成が変わる場合は、新しい plan を構築する。実装専用の一時 buffer、upload、culling pass の展開は provider 内で変えられる。

presentation target も取得ごとに物理 object が変わるため、固定した logical 出力に今回の target を対応付ける。取得時の token と runtime の使用保持は提出ごとに管理する。形状・format・sample count 等が plan の契約と変わった場合は構造変更として扱う。

### Model の描画集合を保持する

`ModelDrawList` は draw item の集合と順序を CPU 上に保持する。`Add` は安定した世代付き handle を返し、`Set` はその描画項目だけを置き換える。`Remove` は削除、`MoveBefore` は順序変更、`Snapshot` は現在の完全な不変値を取得する操作とする。

handle は ECS の Entity や GPU address ではない。上位は自分の Entity と handle の対応を管理し、変更した Component から更新対象だけを作る。Graphics は ECS を毎フレーム走査せず、node 階層の評価も行わない。

geometry、material、transform、skin palette、morph weights は独立した値として更新する。transform だけの変更が geometry や画像の再登録・再転送を起こす要件を作らない。material の数値変更と、shader variant や batch 分類を変える構造変更も実装側で区別する。透明描画の同値時に使う順序を保持し、cache や batching の都合で描画結果を変えない。

### 完全な snapshot を共有して取得する

snapshot は、その時点の全描画内容を保持する。不変の部分木と未変更のデータを共有し、変更した経路と項目だけを新しくする。毎フレームの `Snapshot` や input bindings 作成が全 draw item の複製を要求してはいけない。

各部分木は、それが参照する準備済み CPU upload data の所有をまとめて保持する。snapshot を取得・提出するたびに全 draw の geometry、material、画像をたどって強参照の一覧を作り直すことも避ける。共有部分の所有と参照集合を再利用し、変更部分だけを更新する。この所有範囲は CPU 入力の生存に関するものであり、GPU 使用完了までの資源保持は provider と Resources が別に担当する。

元の可変配列を ReadOnlyMemory で包むだけでは不変性も所有も保証できない。公開時点で不変の所有済み memory を使い、上位が次の Component 更新や資産 cache の解放を行っても、保持中の snapshot の内容を変えない。

差分だけを残して過去の履歴がないと描けなくなる入力は採用しない。provider が前回との差を見つけるために共有部分木や有界の補助情報を使うことはできるが、完全な snapshot が常に復元元になる。世代を飛び越した提出、古い snapshot の再提出、異なる device／provider の初回利用、cache eviction 後も、履歴のロードや最新版の問い合わせなしで実行できる。

### provider は内容更新と描画分類を分ける

provider は snapshot の同一部分を飛ばして変更を求め、部品ごとの GPU cache と描画分類の cache を更新する。geometry と material の参照が安定している場合、transform 更新だけで全描画の分類を作り直さない。変更された描画項目が属する分類の更新で済む構造を選ぶ。

GPU 転送は変更データまたは実装がまとめた変更範囲を対象にする。転送 alignment、連続範囲への集約、allocation の拡張により、実際の転送 byte 数が変更値の byte 数と完全一致するとは要求しない。初回の全転送や cache eviction 後の復旧と、常駐状態での部分更新は区別して計測する。

Native は bindless、linear range、GPU culling や indirect を使え、Portable は自系統の binding と buffer 更新を使える。利用側は shader、batch 用の GPU 構造体、descriptor を管理しない。CPU の共通 snapshot を両系統で受け付けることと、GPU 配置や command の形式を共通にすることを混同しない。

旧 snapshot を先行する GPU work が読んでいる場合、新しい内容でその領域を先に上書きしない。別領域・pool・ring を使うか、内部 graph に更新と reader の依存を登録する。CPU snapshot の保持終了と GPU completion は別であり、古い版の保持を無期限に増やさない。

### 毎フレームの仕事を明記する

camera、可視判定、LOD、透明描画の sort、実行時の barrier と command 記録は、入力や view に応じて引き続き必要になる。静的な draw が多くても、camera が動けば可視集合は変わり得る。保持 cache は、これらを無条件に前フレームから流用する約束ではない。

Graphics が確認するのは自分の plan・型付き入力・handle・内容世代等の契約である。native API／WebGPU／shader compiler が検証できる GPU usage、binding、PSO、同期等の合法性を、保持処理のために独自 validator として再実装しない。

## 計測項目と受入れ指標

下記は実装後の受入れ指標であり、現時点の達成値ではない。CPU 時間と GPU 時間を分け、最初の準備を終えた状態で、入力規模と更新数を固定して測る。時間は中央値と上位 percentile、CPU allocation、転送 byte 数、再構築件数を併記する。

| 計測対象 | 確認する費用 | 避ける誤認 |
| --- | --- | --- |
| 上位からの抽出・CPU 組立て | 作成した draw item 数、変更抽出の件数・時間 | GPU 転送だけ減っていても、全 Entity の毎回再合成は無料ではない |
| 入力 snapshot と bindings | 複製 byte 数、生成した部分木の数、保持 root の取得時間 | Snapshot という名前だけで O(1) と判断しない |
| CPU upload data の保持 | 新たに列挙した依存数、再利用した所有範囲 | 強参照を作るための全 draw 再走査を隠さない |
| provider の差分・batch 更新 | 比較した部分、更新項目数、再分類件数 | camera 変更と draw 構成変更を一つの再構築時間に混ぜない |
| GPU upload | 転送 byte 数、copy／update の件数、packing 時間 | driver や alignment による範囲拡大と入力全体再転送を区別する |
| 可視判定・LOD・sort | view ごとの対象数、CPU／GPU 時間 | 部分更新の件数だけに比例すると仮定しない |
| command 記録・提出・GPU 実行 | 記録時間、draw／dispatch 数、GPU 時間 | CPU draw 記述 cache と GPU command 再使用を同一視しない |

少なくとも次の条件を Native／Portable で別々に確認する。10 万 draw は規模を固定する試験入力であり、その全体をどの GPU でも 60 FPS で描画できるという要求ではない。

| 条件 | 受入れ指標 |
| --- | --- |
| 10 万 draw、入力に変更なし | 同じ snapshot と plan を再利用する。draw item の全再生成、全コピー、CPU 所有依存の全再列挙、未変更 upload data の再転送を行わない |
| 10 万 draw のうち 1 draw の transform 更新 | 変更項目と部分木の経路に応じた snapshot 更新に収める。未変更 geometry・material・画像を再転送しない。全 draw を再分類しない |
| 10 万 draw のうち 100 draw の transform 更新 | 更新件数と更新範囲に応じた CPU／転送費用になることを、1 件更新・全件更新と比較する。変更の位置が連続／散在する条件を分ける |
| 1 geometry を共有する多数の draw、position 属性のみ改版 | 共有する新しい属性の転送を重複させない。依存する bounds・変形結果等の更新数を別に数える |
| camera だけ更新 | 同じ draw snapshot と常駐 geometry を使う。可視判定・LOD・sort の必要な費用は計上する |
| Add／Remove／MoveBefore | 影響する項目・順序と分類を更新し、古い snapshot の内容と順序を維持する |
| 1 → 10 の世代飛越し、10 → 1 の再提出 | 差分履歴なしでも、指定 snapshot の完全な内容で描画する |
| 別 device／provider、cache の解放後 | 完全な CPU snapshot から再転送できる。元ファイル、外部 Component の現在値に依存しない |
| 複数 view、提出が重なる更新 | CPU snapshot を共有でき、先行 GPU reader に後続更新が混入しない |
| presentation target の取得ごとの差替え | 同じ出力契約なら plan の再 Compile を行わず、今回の target と token に提出を対応付ける |

初回構築、cache miss、GPU memory の拡張は定常更新と別に報告する。毎フレームすべてが変化する条件も比較し、保持・差分処理自体の費用を把握する。60 FPS は workload、解像度、shader、GPU、CPU を決めた統合試験で判断する。ここで採用する契約は、未変更部分の再構築を避けられることであり、16.67 ms 以内の実行そのものの保証ではない。

## 実装状態

本書は目標設計と受入れ指標を定める。型付き input slot、bindings、ModelDrawList、共有部分木と CPU 所有範囲、provider の差分更新・描画分類 cache、presentation target の差替え、および上記の計測は実装完了を意味しない。性能値は未測定である。
