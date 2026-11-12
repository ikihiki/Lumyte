# ADR-GRAPHICS-0033: クリップ交差を CPU で正規化し、サンプル相関を保って描く

- 状態: 採用
- 日付: 2026-11-12
- 基準コミット: `c2b941cc767f06cbfbd92b77e6cef2a9fad5a221`

## 背景

多段クリップを使う Drawing2D 利用者は、交差領域が単純でも各画素の各サンプルで元のパスを繰り返し評価する負担を受ける。準備時に交差を確定すれば GPU の反復を減らせるが、境界の数値規則、図形・paint とのサンプル相関、子スコープと資源の寿命を保つ必要がある。

基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Rendering/DrawingProgram2D.cs` の `ApplyClip` は、捕捉した各 clip に Path と Multiply を生成する。`Shaders/drawing-fragment.slang` はそれを 16 サンプルで評価する。`Rendering/PreparedRectangleBatch2D.cs` の `Fill` と `Shaders/rectangles-fragment.slang` は矩形ごとの clip を転送して最大 8 段を反復し、複雑な clip は矩形インスタンシングの対象外となる。これらのパスはすべて同じ `src/Graphics/Lumyte.Graphics.Drawing2D/` 配下である。`Rendering/DrawingBounds2D.cs` の境界は保守的な範囲であり、パス内部への包含証明ではない。

## 決定

準備時に clip の交差を CPU で確定し、空・不要・整数 scissor・単一解析区間・合成済みサンプル mask のいずれかへ正規化する。CPU の数値結果を clip の正とし、GPU は完成した交差を既存の 4×4 サンプル位置で消費する。利用者の描画記述と GPU 完了までの所有責任は維持し、CPU 作業量と格納量には明示的な有限上限を設ける。

### 利用者の変更前・変更後

内部変更なので、利用側の `Renderer2D` / `DrawingScene2D` による記録、準備、Record、submit、完了待ち、解放の記述は変わらない。変更前は同じ clip stack を GPU が毎サンプル反復する。変更後は準備済みの一つの交差を使い、既知の空描画は payload・登録表の確保前に除く。矩形インスタンシングは単純な clip なし経路も維持し、複雑な clip のために描画全体を interpreter へ落とさない。

公開シグネチャの追加・削除はない。ただし clip の数値境界は CPU 判定へ変わり、大きすぎる入力は準備時に `NotSupportedException` となる。旧 GPU の浮動小数点境界との完全一致を保証する契約ではなく、以下の CPU 判定と上限が利用者に見える契約となる。

### 分類とスコープ

| 分類 | 完成した交差 | GPU の扱い |
| --- | --- | --- |
| Empty | 生存サンプルなし、または無効な逆行列 | 通常描画を除去し、paint 子では透明値を返す |
| None | target 全域で clip が不要 | clip coverage 命令を生成しない |
| Scissor | サンプル集合が整数画素境界の矩形 | 外側は interpreter で hardware scissor、矩形 batch でインスタンス境界の切り詰め。残余 coverage 命令なし |
| Analytic | 軸平行矩形交差の端がサンプル区間となる | CPU で確定した整数サンプル添字区間を 1 命令で比較 |
| SampleMask | 一般パスの交差 | 画素ごとの 16 bit の membership を 1 命令で読む |

隣接する paint clip は同じスコープ内だけで統合する。opacity・composition 境界を越えて移動せず、paint 子に大域 scissor を設定しない。子の整数 clip でも coverage が必要なら、その子で解析区間として評価する。外側の整数 clip は interpreter では hardware scissor、矩形 batch では各インスタンスの geometry 境界との交差で処理する。矩形 batch の hardware scissor は target 全域のままなので、異なる整数 clip を同一 batch に保持できる。双方とも残余 coverage 命令は不要である。これはスコープと描画経路に基づく決定的な規則であり、CPU/GPU の適応的切替ではない。

一般 mask の全 bit が 0 なら Empty、全 bit が 1 ならその境界に応じて None / Scissor へ縮約できる。単なる AABB の内包からパス内部と推定して clip を除去してはならない。

### 数値規則と 16 サンプル相関

画素 `(x,y)` のサンプルを `s=(x+(i+0.5)/4, y+(j+0.5)/4)`、`i,j=0..3` とする。出力は `sum(shape(s) * paint(s) * clipIntersection(s)) / 16` である。mask の bit `j*4+i` はその位置での交差 membership を表す。先に平均した alpha を積分後の図形や gradient に乗算する方式へ置き換えない。

既存の `PathFlattening2D` の画素誤差契約で曲線を平坦化し、捕捉頂点を device 空間へ送る。座標変換は binary64 の明示的な入れ子 FMA、`FMA(m11,x,FMA(m21,y,m31))` と Y の対応式で順序を固定する。巻数は binary64 の交差積を用い、上向き辺は `a.y <= s.y < b.y` かつ side > 0、下向き辺は `b.y <= s.y < a.y` かつ side < 0 で加減する。NonZero と EvenOdd の規則、穴、捕捉 transform を保持する。特異行列または有限でない逆行列は透明となる既存の扱いを維持する。

軸平行矩形は同じ格子上で CPU が交差を確定する。端点の添字は `clamp(ceil(edge*4-0.5),0,extent*4)` とし、左・上を含み右・下を含まない区間にする。全端点が 4 の倍数なら整数 scissor とできる。GPU には元の矩形や逆行列による再判定をさせない。

GPU の既存 shape・paint 算術は変えない。旧 clip の inverse-space float 比較、積和の縮約、交差積とは境界で相違し得るが、それを比較 tolerance の拡大で吸収しない。CPU oracle と GPU に渡る各 bit の一致、および独立した shape・paint 相関を検証する。旧 GPU 算術の bit 単位再現や旧 inverse predicate の保持は採用しない。

### 決定的な経路と有限予算

経路は不変の clip geometry、捕捉 transform、fill rule、target extent、pixel origin、DPR から決まる。cache hit/miss、メモリ圧力、時間、負荷、backend の速度で別の評価方式を選ばない。mask 面積・演算・辺・payload 上限の超過は準備を拒否し、元の GPU clip stack へ戻さない。

| 対象 | 上限・条件 |
| --- | --- |
| 捕捉要素 | 4096 |
| clip を持つ target | 各辺 32768 以下。clip なし target へ追加制限を適用しない |
| 合計の非退化な平坦化辺 | 65536 |
| 一般 mask 面積 | 1,048,576 画素 |
| sample-edge 作業量 | `area * 16 * (edgeCount + 1) <= 268,435,456` |
| 準備ローカルの CPU cache | 最大 4096 entry / mask データ 32 MiB |
| interpreter の geometry と payload 合計 | 既存の 262144 Vector4、命令数 256 を維持 |
| 矩形 batch payload | 32 MiB |

mask 境界は非退化辺の保守的範囲を target と矩形 clip で交差して絞る。孤立 move で面積を膨らませない。有限領域でも大きな複雑 mask は拒否され得る。cache が満杯なら新たな記憶を行わず同じ CPU 結果を返すので、cache 収容限界自体は描画拒否や意味変更ではない。

### 格納・所有権・RenderGraph

画素の 16 bit 値は整数を正確に表現できる float lane として格納し、4 画素を float4 に入れる。これは **4 B/画素**であり、密な ushort の理論値 2 B/画素ではない。数値として load し、平均 alpha、正規化 texture、filtering を介さない。inactive SIMD lane の load は範囲内へ clamp する。

CPU cache は準備 context に属する descriptor のみを持ち、可変の大域 GPU mask cache を作らない。GPU mask は既存の owned geometry buffer に入り、遅延 upload、CopyWrite/ShaderRead 宣言、barrier、共有引数表からの間接参照を再利用する。prepared owner と retained asset は GPU 完了と command disposal まで有効とする。target・property・generation による既存の再利用・失効規則を保ち、Graph の producer/read 宣言も省略しない。追加 GPU reference や別系統の寿命管理は不要とする。

## 検討した代替案

- 元の GPU clip stack を残す: 数値変更と CPU mask 生成を避けられるが、同じ交差を各サンプルで繰り返す費用を解消できない。予算超過時の fallback としても使わず、入力から経路が決まる規則を優先する。
- 平均 alpha mask を後から乗算する: 格納と合成は容易だが、shape・gradient と clip の同一サンプル相関が失われるため採用しない。
- 旧 GPU の float inverse predicate を CPU で忠実に再現する: 旧境界を保つ利点はあるが、GPU の contraction や交差積の差まで同一とする保証が得られない。順序を明示した CPU 判定を正とする。
- 全 clip を mask 化する: 表現を統一できるが、整数矩形にも面積比例の生成・転送負担が生じる。geometry/scissor、解析区間、mask の分類を採用する。
- paint 子の clip を外側 scissor に昇格する: 命令を省けるが、composition の backdrop まで切り落とすため採用しない。
- 別の geometry library または別資源系統を導入する: 汎用実装を使う利点はあるが、既存の平坦化誤差・16 サンプル・所有契約との一致が必要になる。既存の平坦化と shader-data の資源管理を使う。

## 結果と影響

多段 clip の GPU 反復と不要な payload を減らせる一方、一般 clip の CPU rasterization と 4 B/画素の格納費用を負担する。経路の変化には、複雑 clip 付き矩形でインスタンシングを維持できる効果も含む。固定容量 buffer arena、pass 融合、resource set 復元、batching 全体の再設計は対象外とする。

### 検証条件と確認済みの範囲

分類・命令数、空 payload の不生成、予算拒否と cache hit の決定性、origin/DPR、穴、隣接 float 境界、非退化境界、opacity で分離した paint scope を確認する。独立 oracle の三角形は mask `0x0137`（6 サンプル）となる。境界 0.125 とその隣接 float の CPU alpha 1 / 0.75、複数 packed lane にわたる 16 bit の個別転送を interpreter と矩形経路で確認する。

相関の独立期待値は shape + gradient + clip の `RGBA=(0.0703125,0,0,0.3125)` とし、平均 alpha では再現できない条件を使う。別 clip を持つ opacity/composite で backdrop が切れないこと、Graph の依存宣言、完了待ち・cleanup・retained asset の寿命も確認する。

統合元の検証記録では CPU/Graph/lifetime/cleanup が **262 passed / 0 failed / 0 skipped**、Radeon の公開 API GPU suite が **29 / 0 / 0**、llvmpipe も **29 / 0 / 0** である。Release build、diff check、Markdown lint は 307 files / 0 issues だった。既存の compiler representation assertion 3 件は正規化表現へ変更しつつ、shape・transform・composition の検査を保ち、scissor/単段条件を加えた。pixel tolerance、timeout、feature/API assertion、GPU skip guard は緩和していない。本書の統合時には benchmark や runtime suite を再実行していない。

### 測定境界と実測のトレードオフ

256×192 の同一ハーネスで、clip を再利用する mask-cached、毎回 mask を作る mask-dynamic、clip なし矩形 128 個の rect-control を比較した。clipped fixture は同じ三角形と小数端点の矩形 clip を持ち、cached は static scene、dynamic は毎回記録する scene である。動的操作は記録・準備・mask 生成を計測に含める。cached は setup の完了済み frame で公開された prepared asset を使い、動的再コンパイルと cached 再利用を runner で assert する。

1 operation は必要な記録、retained preparation、新規 command buffer、target barrier、Record/Finish、queue submit、Completed を assert する WaitAsync、command disposal、frame retirement、submission disposal までを含む。setup では pipeline と cached fixture を温める。表示・VSync・process 間の重複実行はなく、GPU timestamp は取得していない。

BenchmarkDotNet の事前固定順は baseline A、candidate A、candidate B、baseline B（ABBA）。各 process は warmup 3 回、100 ms を目標とする measured iteration 8 回、MemoryDiagnoser、full JSON、outlier 除外なしである。中央値/P95 は iteration estimate の分位点であり、個々の frame latency の分位点ではない。

環境は Radeon 8060S（RADV STRIX_HALO）、Mesa 26.0.8、Ryzen AI MAX+ 395、Ubuntu 26.04.1、SDK 10.0.403 / runtime 10.0.14。ソフトウェア検証は Radeon と区別して llvmpipe（LLVM 21.1.8）を明示選択した。

各 cell は process A / B。時間は ms、allocated は完了操作あたりの managed byte。

| Fixture | Baseline median | Candidate median | Baseline P95 | Candidate P95 | Baseline allocated | Candidate allocated |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| mask-cached | 7.721762 / 7.500289 | 0.142192 / 0.158978 | 8.027742 / 7.860633 | 0.153467 / 0.164861 | 1081913 / 1081913 | 10452 / 10525 |
| mask-dynamic | 19.874373 / 20.588669 | 3.542554 / 3.440409 | 20.215780 / 22.193188 | 3.786990 / 3.885901 | 9411856 / 9411577 | 189528 / 189528 |
| rect-control | 0.442389 / 0.454911 | 0.494883 / 0.481076 | 0.464087 / 0.467670 | 0.521035 / 0.503787 | 263635 / 263630 | 264203 / 263979 |

paired median の変化は cached が -98.16% / -97.88%（-7.580 / -7.341 ms、allocation 約 -99.03%）、dynamic が -82.18% / -83.29%（-16.332 / -17.148 ms、allocation -97.99%）。**clip なし矩形は +11.87% / +5.75%（+0.0525 / +0.0262 ms）悪化**し、managed allocation も +568 / +349 B/frame だった。全 workload の改善とは主張しない。

全 12 case/process に 8 estimate があり、除去した outlier はない。candidate dynamic は iteration あたり 1 operation の短時間観測で、P95 は保持した 4.089 ms の outlier を含む。100 ms は目標であり実時間の保証ではない。一般 FPS、長期安定性、純 GPU 時間、全 clip geometry、large-target scaling の認定には使わない。好都合な再測定や測定後の production 変更で control の悪化を隠していない。

### 数値証拠の保管と再現

本文へ説明を集約する依頼に従い、この節だけは通常の ADR の参照範囲を拡張し、同梱の数値証拠を案内する。これは基準時点のソースや既存判断の根拠ではなく、上記の限定的な測定値を再確認するための記録である。

<!-- markdownlint-disable-next-line LMADR003 -->
[追跡対象の測定 JSON](../../performance/data/drawing2d-normalized-clips-2026-11-12.json) に iteration 値、実 operation 数、paired delta を保持する。比較の baseline は基準 SHA、production 実装は `635dbb9a`、辺数上限を含む測定時の production freeze は `87b6acaa` である。

ハーネスは `tools/benchmarks/drawing2d-skia-bdn/ClipFrameBenchmarks.cs`。baseline に元々ないため両 checkout へ同じ凍結ソースを配置し、SHA-256 manifest の完全一致を確認した。benchmark assembly と production Drawing2D assembly の hash を variant ごとに保存し、各 2 process 後に不変を確認した。生成 host は通常の BDN cleanup を使い、保持した hash で識別する。

保存先は home-backed candidate checkout の `artifacts/normalized-clips/`。full JSON/CSV/report/process log は `final-base-a`、`final-candidate-a`、`final-candidate-b`、`final-base-b`、集計は `comparison-summary.json`、baseline の由来は `provenance-baseline/` にある。correctness log は `normalized-unit.log`、`normalized-hardware.log`、`normalized-software.log`、環境情報は `hardware-identity.txt`、`software-identity.txt`、`environment.txt`、CPU の算術曖昧性 witness は `arithmetic-boundary/` にある。witness は旧 GPU 境界との同値証明ではない。これらの local artifact の存在を tracked JSON の代替の公開保証にはしない。

candidate checkout は `/home/hideaki/.cache/lumyte-normalized-clips`（branch `feat/clip-normalization-home`）、matched baseline は `/home/hideaki/.cache/lumyte-clips-baseline`（基準 SHA に detached）だった。元の `/tmp/lumyte-drawing2d-normalized-clips` と未追跡資料は保存し、reset/削除していない。`/tmp` の usrquota により空き容量があっても native write が EDQUOT となったため、home-backed checkout と local TMPDIR を使用した。OS/config は変更していない。

空の `baseline-pilot/FrameBenchmarks-20261112-181517.log` は完了・計測の証拠ではない。sealed benchmark class を BDN が拒否した最初の validation-only attempt も実行された benchmark を含まない。class を unsealed に直してソースを再凍結した `final-*` だけを上記の数値に使い、観測済みの timing は除外していない。

再現時は隔離した baseline / candidate に同じソースを置き、source hash を一致させ、ABBA 順に次を実行する。`NAME` は対応する process 名に置換し、環境情報と binary hash も保持する。

```sh
export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env
export TMPDIR="$PWD/artifacts/tmp"
source tools/setup/activate.sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
export LUMYTE_CLIP_BENCH=1
dotnet build tools/benchmarks/drawing2d-skia-bdn/Drawing2DSkiaBdn.csproj -c Release
dotnet tools/benchmarks/drawing2d-skia-bdn/bin/Release/net10.0/Drawing2DSkiaBdn.dll --filter '*' --artifacts artifacts/normalized-clips/NAME
```
