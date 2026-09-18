# Model normal map の接線生成検証

2026-09-18 に Native／Portable の共通幾何計算へ、三角形専用の managed MikkTSpace を追加した。
コードは `src/graphics/Shared/Models/MikkTangents.cs`、原作者の通知は同じディレクトリの
`MikkTSpace.LICENSE.txt` に置き、両 provider の配布 package にも含める。
実行時に native DLL や JavaScript の接線生成器を要求せず、Browser でも同じ計算を使う。

## 採用した処理

元は [Morten S. Mikkelsen の参照実装](https://github.com/mmikk/MikkTSpace/tree/3e895b49d05ea07e4c2133156cfa94369e19e409)
で、既定の角度閾値 180 度を採用する。位置・法線・UV の一致による結合、辺の接続による群分け、
UV の向きの分離、接平面へ投影した角度による重み付け、縮退面への接線の継承を行う。
quad は Graphics の入力ではないため追加せず、TriangleStrip／TriangleFan は先に三角形へ展開する。

参照 C の既知問題をそのまま再現することは目的としない。
結合代表は最小の入力 index に固定し、辺の全グループを face 順に扱う。
元の高速辺ソートには最後のグループをソートしない箇所がある。
この問題と代表選択の違いは [Bevy の MikkTSpace 実装](https://github.com/bevyengine/bevy_mikktspace)
でも `corrected-edge-sorting`／`corrected-vertex-welding` として区別されている。
Lumyte のコードは元の C と同一のソースや bit 単位互換品とは表示しない。

## 独立比較

実験は `artifacts/experiments/mikktspace/` で行った。
参照 `mikktspace.c` の SHA-256 は
`de87e74107df766ce68108801262bd8d53899414236b59810509a8fc2a51e288`。
Zig 0.14.1 の C コンパイラを `-shared -O2 -ffp-contract=off -target x86_64-windows-gnu` で使用した。
比較側は上記 C の頂点結合を最初の完全一致頂点の探索へ置換し、辺ソートの末尾二グループの処理を補った。
接線・重み・向きの計算には手を加えていない。

固定 seed 4672 の 6×6 grid を 5,000 組作り、頂点の高さと法線、UV、面順を変えた。
反転 UV、UV の縮退、幾何の縮退、同じ辺を共有する複数面も含める。
比較数は **755,004 corner**、接線 XYZ と handedness W のベクトル距離の最大は
**4.056292×10⁻⁷** だった。
修正前の公式 C では同じ入力集合に最大 **2.0020626** の差が出ることも確認した。
これは全入力・全 CPU の bit 一致を保証する試験ではない。

通常の xUnit は外部ツールを必要としない。mirrored UV、接続しない面、縮退面と、
公式 C から取得した四面 fan の小さな数値参照を `MikkTangentsTests` で固定している。
描画 conformance は別に、provided／generated tangent、handedness、反転 UV、負の world scale、
NormalScale、skin、tangent morph、flat normal と UV 変換を検証する。

## 描画への接続

既存 normal がある場合、欠けた tangent は変形前の属性から生成し、morph の tangent XYZ 変位と
skin の線形変換を適用する。tangent W は morph せず、変換の反転を反映する。
normal がない場合は変形後の flat normal と geometry から生成し、元の tangent と tangent morph は使わない。
生成に使う normal map の UV set と変換を geometry cache の key に含める。
pixel shader は接線を法線に直交化し、数値として読んだ normal map の XY に NormalScale を掛ける。
裏面の補正は normal map を合成した法線へ適用する。
