# Slang から WGSL の直接 root 入力を生成する実験

確認日: 2026-09-10（日本時間）。対象は [ADR 0023](../adr/0023-shader-design-and-api.md) の「直接 root の生成が未確認」という点。実験用 source と実行手順は [再現用ディレクトリ](../../tools/experiments/slang-wgsl-root/README.md) に保存した。

## 結論

**Slang から直接 root を使う WGSL を生成し、GPU で正しく実行できる。ただし、通常の push constant 宣言だけでは成立せず、Portable 専用の小さな WGSL 接続が必要である。**

Slang 2026.7.1 と 2026.17 の通常の root 宣言は `var<uniform>` へ出力された。対して、`__requirePrelude` で `var<immediate>` を宣言し、`__intrinsic_asm` の scalar／vector accessor から読む経路は成立した。2026.17 の生成 WGSL を一切書き換えず、Edge 152 の実 GPU で異なる root 値を読み戻した。root 用の GPU buffer や binding は作っていない。

このため「未確認なので root を使う shader は直接 WGSL で書く」という条件を改め、**固定した Slang と Portable 専用 root accessor を採用する**。計算本体の Slang 共有は可能であり、全 shader を手書き WGSL にする必要はない。accessor の WGSL 接続を含むため、純粋な標準 Slang のみの解決とも扱わない。

## 確認環境

| 項目 | 使用した値 |
| --- | --- |
| OS | Windows、x64 |
| 既存 compiler | Slang 2026.7.1。repository の `.packages/slang/2026.7.1/bin/slangc.exe` |
| 比較 compiler | Slang 2026.17、2026-09-04 公開の公式 Windows x86_64 ZIP |
| 2026.17 ZIP の SHA-256 | `bc8cf08b24aaf44d98f06b7d578d0557a21bfed1ce6bfb9765a0f0d21dec8f31`。GitHub release asset の digest と一致 |
| Browser | Microsoft Edge 152.0.4191.66。実験専用 profile の headless 実行 |
| Browser が選択した adapter | NVIDIA／Turing、`isFallbackAdapter = false`。CDP の GPU 一覧は GeForce RTX 2080、driver `32.0.15.9186` |
| WebGPU の直接入力 | `immediate_address_space` あり、device の `maxImmediateSize = 64`、`setImmediates` あり |
| Harness | Node.js 24.19.0。追加 npm package なし |

ブラウザーに unsafe／experimental feature の起動指定はしていない。high-performance と low-power の両要求は同じ NVIDIA adapter を選んだため、別 GPU 二台や iGPU の試験とは数えない。CDP の ANGLE／D3D11 表示は WebGL 側の renderer 情報であり、WebGPU が使用した下位 API の証明には使わない。64 byte は今回の device の報告値であり、Lumyte の固定 ABI にしない。

## コンパイル実験

両版で同じ 13 fixture を WGSL、SPIR-V assembly、HLSL へ生成した。各 fixture は実際に入力を計算結果へ使用し、最適化で root 全体が不要になる例を避けた。SPIR-V は直接生成する経路で `PushConstant` を確認した。HLSL は source の生成確認であり、DXIL の実行確認ではない。

| 入力 | 両版の WGSL 出力 | 判定 |
| --- | --- | --- |
| root なし、共有 `shared_math` module | storage の結果出力のみ | 共有 module の通常の生成が可能 |
| `ConstantBuffer`、global `uniform` | `var<uniform>` | 直接 root にならない |
| `[push_constant]`、`[[vk::push_constant]]` の buffer／cbuffer／struct／scalar | `var<uniform>` | 直接 root にならない。push constant 系は group／binding のない uniform を生成するため、コンパイル成功だけで WGSL の合法性も保証されない |
| entry point の `uniform` 引数 | `var<uniform>` | SPIR-V では push constant でも WGSL の直接 root にはならない |
| WGSL prelude と scalar／vector accessor | `var<immediate>`、root の uniform 宣言なし | 採用する経路。2026.17 は GPU readback も成功 |
| `__target_intrinsic` で構造体名だけを WGSL 型へ mapping | 宣言の member 名と参照の生成名が不一致 | この方式は採用しない |

`__target_switch` を使った fixture は WGSL の immediate と Native の push constant を一つの入力 source から比較するための実験である。Native／Portable の本番 root 宣言・ABI を共通化する決定ではない。

最初の `__intrinsic_asm "lumyteRoot";` は Slang が `lumyteRoot()` と出力し、WGSL frontend が変数を関数として呼び出していると診断した。式として `__intrinsic_asm "(lumyteRoot)";` を指定すると正しく生成された。構造体名だけの mapping でも `count` と `count_0` 等の食い違いを確認したため、root の field は独立した scalar／vector accessor で読み、通常の Slang の値型へ組み立てる。

## GPU 実行結果

生成物の SHA-256 を記録し、browser へ渡す際にも一致を確認した。storage buffer は計算結果と readback のためだけに使用し、root は `setImmediates` に渡した。shader の compilation info、validation／internal／out-of-memory の error scope と queue 完了を確認し、期待値との比較を行った。

| 試験 | root | 期待値と実測値 | 結果 |
| --- | --- | --- | --- |
| 手書き WGSL、異なる値で二回 dispatch | 8 byte | `[40, 74]` | 一致 |
| Slang の直接 root と共有計算 module、異なる値で二回 dispatch | 16 byte | `[40, 74]` | 一致 |
| Slang の mixed root、異なる値で二回 dispatch | 32 byte | `[19, 61]` | 一致 |
| 手書き WGSL、同じ compute pass 内で root を更新 | 32 byte | `[8.5, 2, 3, 0, -6, 6, 8, 1]` | 一致 |
| 手書き WGSL、vertex と fragment の双方で直接 root を参照 | 16 byte | RGBA32Float `[0.25, 0.5, 0.75, 1]` | 一致 |

mixed root の配置は `u32@0`、`f32@4`、`vec3<f32>@16`、`f32@28`、全体 32 byte。8〜15 byte の padding を含む。host bytes は実験で手動作成したものであり、C# 生成器の検証ではない。compute の記録後に入力の CPU 配列を別の値で上書きしても、提出した GPU command は元の値を使用した。

すべての成功ケースで shader diagnostics はなく、三種類の error scope は null、uncaptured error もなかった。最終 harness は device loss も失敗として記録する。raster は手書き WGSL の runtime 対応を検証したものであり、Slang の raster entry の実行確認と混同しない。

| 2026.17 の実行対象 | WGSL の SHA-256 |
| --- | --- |
| `target_switch_immediate.wgsl.wgsl` | `0591c5f70cb19c93edc40b51979d96e6f28a7ff2d7bf721bcd18bc6ec802523b` |
| `scalar_getter_mixed_immediate.wgsl.wgsl` | `22aeadaab338734708f0a722e2484b6e0a9c157bfde3e3b84e0a22257b744242` |

保存した fixture から再生成した WGSL もこの hash と一致した。実行の JSON、compiler の引数・終了値・reflection・生成物は `artifacts/experiments/slang-wgsl-root/` に保存する。browser profile や compiler binary とともに生成物は Git 管理の対象にせず、source と本記録を保持する。

## 採用時の境界

Slang の相互運用機能は公式文書にあるが、安定した言語仕様ではなく内部機能として公開されている。初期の検証基準を 2026.17 に固定し、直接 root の宣言と accessor を offline tool の一箇所へまとめる。更新時に生成結果と GPU readback を再確認する。[Slang の相互運用機能](https://docs.shader-slang.org/en/latest/external/slang/docs/user-guide/a1-04-interop.html)

prelude の root は Slang reflection の root として列挙されない。さらに、通常の push constant は WGSL に uniform と出力されても reflection が `pushConstantBuffer` と表示する。最終 WGSL を公式 frontend に処理させ、その型・offset・padding を package と C# 生成の根拠にする。Native の reflection や Slang の生成名から推測しない。

`requires` は型や変数の宣言より前に必要になる。固定版の compiler API は `setLanguagePrelude` を提供し、language prelude を root 用 prelude より先に出力する。統合実装では要求 feature ごとに設定を固定した session を使う。`__requireTargetExtension` はこの版で `enable` を出すため、`requires immediate_address_space;` の代用にしない。この API 統合は source で確認した設計であり、今回の CLI 実験は一つの自己完結した prelude に directive と root 宣言を含めている。[公開 API](https://github.com/shader-slang/slang/blob/v2026.17/include/slang.h#L4183-L4192)、[出力順](https://github.com/shader-slang/slang/blob/v2026.17/source/slang/slang-emit.cpp#L3064-L3096)、[extension 出力](https://github.com/shader-slang/slang/blob/v2026.17/source/slang/slang-extension-tracker.cpp#L17-L24)

## 既存実装と未確認範囲

調査時の旧 SlangPackageCompiler には、予約 binding の `var<uniform>` を `var<immediate>` へ置換する処理があった。本実験はその処理を通していない。旧 compiler と文字列置換経路は 2026-09-14 に削除済みである。専用 accessor による Slang の本番統合は後続作業であり、本実験の成功と production compiler の完成は区別する。

未実装・未確認なのは、root accessor の生成器、公式 WGSL frontend からの ABI／C# 生成、Slang の raster variant、matrix／array 等の全入力型、各機能 pass、C# の WebGPU 接続を経由したこの生成物の実行、他の browser／GPU である。比較用の Native shader は SPIR-V assembly と HLSL 生成まで確認した。DXIL の追加試行は実験 compiler 環境の `dxcompiler.dll` 読込みに失敗し、今回の直接入力実験では Native GPU 実行をしていない。

既存コードへの回帰確認として `dotnet test Lumyte.slnx --no-restore` は終了コード 0、失敗・スキップなしで完了した。これは既存テストの結果であり、新しい二系統 toolchain の実装完了を示さない。

## 参考文献

- [Slang v2026.17 release](https://github.com/shader-slang/slang/releases/tag/v2026.17): 比較に用いた固定版。
- [Slang v2026.17 WGSL emitter](https://github.com/shader-slang/slang/blob/v2026.17/source/slang/slang-emit-wgsl.cpp#L172-L177): 通常の parameter group は uniform を出力する。
- [WGSL address spaces](https://gpuweb.github.io/gpuweb/wgsl/#address-spaces): immediate と resource buffer の区別。
- [WebGPU immediate data](https://gpuweb.github.io/gpuweb/#immediate-data): 直接入力の command と pipeline の契約。
- [Chrome 149–150 の immediates](https://developer.chrome.com/blog/new-in-webgpu-149-150): language feature の検出と browser 側の利用方法。今回の実行結果の代わりには使わない。
