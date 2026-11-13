# 数値・時刻・メモリ・テキスト編集の負荷カタログ

基準コミット: `05d391955a607e7fb948812c9660dc2bed32704d`。状態は設計案であり、ケースや実行基盤の実装完了を示さない。
意味の異なる処理経路を先に整理し、実行頻度の確定は全 Core カタログの確認後に行う。
共通の測定契約・モード定義は[共通 ADR](../../adr/core/CORE-0005-benchmark-catalog-and-modes.md)に従う。

## プロジェクトごとの役割

| プロジェクト | 位置付けと境界 |
| --- | --- |
| Lumyte.Core | 独立測定。時刻取得と検査付き時刻算術を扱い、スケジューラや待機時間を代用しない。 |
| Lumyte.Mathematics | 独立測定。補間、幾何、色の意味別経路を対象とし、型・別名の総当たりにはしない。 |
| Lumyte.Memory | 独立測定。会計と実配列の借用を区別し、所有期間・回収・競合を含める。 |
| Lumyte.Memory.DependencyInjection | 利用側統合測定。登録から解決・解放までを Memory の構築系列へ統合し、転送メソッドだけを順位付けしない。 |
| Lumyte.TextEditing | 独立測定。単一行の文書モデル・変換中文字列・候補検証を扱い、フォント・描画・OS の IME は含めない。 |

これら 5 プロジェクトにコンパイル時専用の計測対象はない。色名の生成済み拡張は色名取得の利用経路へ統合し、生成物のメソッド数をケース数に換算しない。
追加する場合の配置は `benchmarks/Core/<target>.Benchmark`、統括は Fallout とする。公開 API の追加や内部アクセスの特例は前提にしない。
既存の Mathematics は独自ループの変換境界・バッチ距離測定であり、以下の系列や共通モードが既に揃っているわけではない。

## ケースの読み方

- 各 ID は安定した処理系列を表す。同じ行に列挙した処理は個別ケースに分け、合算時間で意味の異なる経路を隠さない。
- 入力の「代表・境界・負荷」は選択式のプロファイルであり、型・長さ・値・実行モードの直積を作らない。サイズは特記しない限り要素数。
- 各系列末尾の「全」は `verify / screen / compare / diagnose` の適格性、「確認」は `verify` のみを表す。性能値の判断は共通 ADR に従う。
- 「全」の異常経路も正常経路と別ケースにする。例外の妥当性確認だけが目的の場合は確認に限定する。
- 環境の既定は CPU の管理コード。OS・ランタイム・アーキテクチャを固定し、幾何と色では使用命令セットも記録する。競合ケースはワーカ数と同期境界を追加する。
- 特記しない純粋演算は入力配列を外で構築し、出力全体を外で消費する。定数だけの式にせず、固定シードの非同一入力を巡回する。構築以外の準備と正解照合は計時外。
- 割当は管理ヒープ、保持量は生存オブジェクト、Memory の会計値は論理的な課金量として別々に扱う。管理配列の課金をネイティブ使用量と呼ばない。

## 時刻

### CORE-TIME-01 — 時刻取得と時計の生成

- 経路: [SystemMonotonicClock.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Core/Time/SystemMonotonicClock.cs)。SystemMonotonicClock の構築と Now 取得を別ケースとし、1 操作は 1 インスタンス生成または 1 読み出し。代表は同一時計を 256 回巡回、境界は生成直後、負荷は 4096 読み出し。返却 Ticks を消費する。正解は非減少かつ原点以降であり、読み出しごとの増加は要求しない。OS の時計源依存を記録。管理割当と時計インスタンス保持を区別。全。

### CORE-TIME-02 — 検査付き算術と変換

- 経路: [Duration.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Core/Time/Duration.cs)。Duration の加減・符号反転・倍率・除算・FromSeconds と TimePoint の差分・加減を別ケースにする。1 操作は 1 変換または演算。代表は ±1〜100000 ticks、境界は 0・負値・long 上限付近・有限でない秒数、負荷は 4096 組。整数期待値と丸め規則、OverflowException／ArgumentOutOfRangeException を照合し、例外は別ケース。値を蓄積し続けず入力組から毎回計算する。全。

### CORE-TIME-03 — 決定的時計の前進と拒否

- 経路: [ManualClock.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Core/Time/ManualClock.cs)。ManualClock.Advance と Now。1 操作は正の Duration による前進と読み出し。代表は 1 tick、境界は 0・負値・加算の桁あふれ、負荷は 4096 前進ごとに新しい時計へ戻す。正解は開始値に前進量と加算回数の積を足した値、拒否後の不変性。ゼロ前進は確認専用とし、正常系列を無操作にしない。正常は全、拒否は確認。

時刻専用のテストプロジェクトは基準時点にないため、上記の算術・非減少条件を確認ケースの受け入れ条件とする。
TimePoint の実際の算術境界は [TimePoint.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Core/Time/TimePoint.cs) に固定する。

## 数学・幾何

### CORE-MATH-01 — 線形・整数・緩急補間

- 経路: [Interpolation.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Interpolation.cs)。Interpolation.Linear／EaseIn／EaseOut／EaseInOut と IntegerInterpolation.Linear。1 操作は 1 値または 1 ベクトルの補間。代表は float と Vector3 の異なる両端、整数 long、境界は amount=0・1・外挿 ±0.25 と逆符号の大値、負荷は 4096 組。両端保持・整数丸め・有限結果を既存試験と照合。全。

### CORE-MATH-02 — 接線・回転・委譲補間

- 経路: [HermiteInterpolation.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/HermiteInterpolation.cs)。HermiteInterpolation.Interpolate、QuaternionInterpolation.Slerp／Normalize、BezierInterpolation.Cubic を別ケース化。1 操作は 1 補間。代表は scalar・Vector3・Quaternion、interval=1、境界は interval=0／2・近接回転・反対符号の等価回転・ゼロ正規化入力、負荷は 4096 組。委譲関数は静的で構築を除外。接線の interval 依存と回転長を確認し、退化入力は実装の契約に照合する。全。

### CORE-MATH-03 — 時間曲線の構築と逆解法

- 経路: [CubicBezierTiming.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/CubicBezierTiming.cs)。CubicBezierTiming 構築と Transform を別ケース化。1 操作は 1 曲線生成または 1 amount 評価。代表は (0.25,0.1,0.25,1)、境界は水平微分ゼロ・amount=0／1・縦方向の過大応答・不正制御点、負荷は 4096 個の内点。独立な二分探索の参照解と許容差で照合し、恒等曲線だけで代用しない。構築割当を分離。全。

### CORE-MATH-04 — 数値座標の構築と変換

- 経路: [Point2D.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Geometry/Point2D.cs)。Point2D／Point3D の構築・TryCreate・FromCoordinates・CopyTo・ConvertChecked と形状の ConvertChecked。1 操作は 1 値の構築または変換。代表は int→double と float、境界は非有限・宛先範囲外・短い Span、負荷は 4096 座標。成分・失敗時の出力を照合し、コピー先は再利用。BigInteger は別プロファイルで 128／1024 bit に制限し管理割当も観測。全。

### CORE-MATH-05 — 距離・射影・線分交差

- 経路: [Segment2D.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Geometry/Segment2D.cs)。Point2D.DistanceTo／DistanceSquared、Segment2D.ClosestPoint／DistanceTo／Intersects、Geometry3D.Distance／ClosestPointOnSegment。1 操作は 1 点対または 1 線分対。代表は double と int→double、境界は端点・零長・共線・非交差・大座標、負荷は 4096 組。既知の直角三角形、クランプされた射影、交差の対称性と試験期待値を照合。全。

### CORE-MATH-06 — 矩形境界と包含形状

- 経路: [Geometry2D.Bounds.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Geometry/Geometry2D.Bounds.cs)。Rect2D／Bounds2D の包含・交差・結合・Offset／Outset、Circle2D／Ellipse2D／RoundedRect2D／Sphere3D／AxisAlignedBox3D.Contains を意味別に分離。1 操作は 1 判定または 1 境界演算。代表は double の重なり、境界は閉／半開の端・空・接触・半径ゼロ・桁あふれ、負荷は 4096 組。端の包含規則と囲みの保守性を照合。全。

### CORE-MATH-07 — 境界縮約とアフィン変換

- 経路: [Geometry2D.Affine.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Geometry/Geometry2D.Affine.cs)。TransformAffineFused／TransformAffineChecked、Rect2D.FromPoints、AxisAlignedBox3D.FromPoints、Geometry2D.GetBoundsBcl／GetTransformedBoundsBcl。1 操作は 1 点変換または 1 集合縮約。代表 32 点、境界 0／1 点・回転・非一様倍率・大座標、負荷 4096 点。独立な全頂点変換の包囲結果と照合し、単位行列だけにしない。出力型の選択を固定。全。

### CORE-MATH-08 — 多角形と巻き数

- 経路: [Geometry2D.Polygon.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Geometry/Geometry2D.Polygon.cs)。ContainsPolygonClosed／ContainsPolygonEvenOddClosed、GetWindingContributionFused／AccumulateWindingFused。1 操作は 1 点に対する全辺判定、単辺寄与は別ケース。代表 32 頂点の凸・凹、境界 0／1／2 頂点・辺上・自己交差・大整数、負荷 4096 辺。偶奇と非零規則の違い、向き反転と境界点を試験期待値へ照合。全。

### CORE-MATH-09 — 曲線評価と上限制御付き分割

- 経路: [Geometry2D.Bezier.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Geometry/Geometry2D.Bezier.cs)。EvaluateQuadraticBezier／EvaluateCubicBezier と TryFlattenQuadraticBezier／TryFlattenCubicBezier、描画用 AppendFlattened 系列。1 操作は 1 曲線評価または 1 曲線分割。代表 tolerance=0.25・maxDepth=10・maxPoints=1024、境界は定数曲線・深さ／点数不足、負荷は深さ12・4096点上限。出力リストは毎回 Clear、失敗後の宛先保持と端点・誤差条件を照合。デリゲートと構造体 sink は分ける。全。

### CORE-MATH-10 — ベクトル化バッチ距離

- 経路: [Geometry2D.Batch.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Geometry/Geometry2D.Batch.cs)。SquaredDistancesBcl の float／double。1 操作は全配列への 1 呼び出しであり、点当たりは派生値。代表 1024、境界 0／3／31／32／33・短い出力・重複 Span、負荷 65536。出力配列は再利用し、既存の scalar 参照式と各要素を照合。HWIntrinsic と SIMD 幅を記録。正常は全、不正 Span は確認。

数学の正解根拠は [InterpolationTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/InterpolationTests.cs)、[IntegerInterpolationTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/IntegerInterpolationTests.cs)、[ExtrapolationTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/ExtrapolationTests.cs)、
[NumericUtilityTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/NumericUtilityTests.cs)、[NumericShapeTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/NumericShapeTests.cs)、[GeometryNumericalRegressionTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/GeometryNumericalRegressionTests.cs)、
[GeometryCurveTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/GeometryCurveTests.cs)、[GeometryBatchTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/GeometryBatchTests.cs)を使う。試験の成功は性能差の証拠ではない。

## 色・画素

### CORE-COLOR-01 — 型付き色の構築・検査付き変換

- 経路: [Rgba.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Colors/Rgba.cs)。`Rgba<T>`／`SRgba<T>`／`LRgba<T>` の構築・TryCreate・FromComponents／CopyTo・ConvertChecked・NormalizeUnorm／QuantizeUnorm、SRgba8 の符号値と正規化値の変換。1 操作は 1 色の変換。代表 byte→float・float→byte、境界 0／最大・半量子・非有限・範囲外、負荷 4096 色。符号値変換と色空間変換を混同せず丸め・成分を照合。BigInteger は 128／1024 bit の別ケース。全。

### CORE-COLOR-02 — 伝達関数と明度変換

- 経路: [Rgba.Operations.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Colors/Rgba.Operations.cs)。DecodeSrgb／EncodeSrgb、SRgba.ToLinear／LRgba.ToSrgb、ToLinearGrayscale。1 操作は 1 色変換。代表 float、境界は伝達関数の折点前後・負の線形領域・極小値・0／1、負荷 4096 色。既存の参照式・端点・許容差と照合し、float／double／decimal は独立プロファイル。全。

### CORE-COLOR-03 — 透明度・補間・合成・行列

- 経路: [Rgba.Operations.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Colors/Rgba.Operations.cs)。PremultiplyLinear／UnpremultiplyLinear、InterpolateSrgbLinearPremultiplied、SourceOverLinearPremultiplied、AddLinearPremultiplied／AddUnitLinearPremultiplied、ApplyMatrixLinearPremultiplied。1 操作は 1 色または色対の処理。代表 alpha=0.5、境界 alpha=0／1・極小・補間端点・飽和、負荷 4096 色。前乗算済み入力を明示し独立式と比較。恒等行列だけを使わない。Raw 補間は旧精度方針の別ケース。全。

### CORE-COLOR-04 — 色モデル変換

- 経路: [Rgba.Models.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Colors/Rgba.Models.cs)。FromHslSrgbDegrees／FromHsvSrgbDegrees と ToHslSrgbDegrees／ToHsvSrgbDegrees。1 操作は 1 モデル変換。代表 6 色相区間、境界は無彩色・色相の周回・黒白・最大彩度、負荷 4096 色。往復誤差だけに頼らず既知色と試験期待値を照合。全。

### CORE-COLOR-05 — 型付き画素の入出力

- 経路: [Color.Pixels.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Colors/Color.Pixels.cs)。Color.DecodePixels／WritePixels、Rgba.DecodePixel／WritePixel。1 操作は 1 画素または 1 Span 全体の処理で分離。代表 RGBA UInt8 1024 画素、境界は RGB／BGRA／GrayAlpha・UInt16 の両バイト順・浮動小数形式・0／1画素・短い Span、負荷 65536 画素。既知バイト列と書込長、未書込領域を照合。バッファ再利用、単画素と配列の割当境界を区別。全、不正長は確認。

### CORE-COLOR-06 — 圧縮画素の符号化と解釈

- 経路: [RawRgb565.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Colors/RawRgb565.cs)。RawRgb565／RawRgba4444／RawRgba5551／RawRgba1010102／RawBgra1010102 の Read／Write・FromSrgb／FromLinear・InterpretSrgb／InterpretLinear、Color.ReadPackedPixels／WritePackedPixels。1 操作は 1 語変換または 1024 画素 Span 処理。境界は全零・全ビット・alpha閾値・バイト順、負荷 65536 画素。量子化後の期待ビット列と誤差を照合し、転送関数は別の意味として分類。全。

### CORE-COLOR-07 — 色名参照と初期化

- 経路: [CssColors.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Mathematics/Colors/CssColors.cs)。CssColors.TryGet の一致・大文字小文字違い・未知名と All の初回初期化。1 操作は 1 検索、初回は新プロセスの最初のアクセス。代表 32 名、境界 null／空・別名、負荷 4096 検索。正解は既知の SRgba8 値と失敗 bool。生成済み NamedColors 拡張は代表 1 型の取得を利用側確認に統合し、全生成物を独立測定しない。辞書の保持量と初回割当を分離。全。

色の根拠は [ColorStorageTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/ColorStorageTests.cs)、[ColorOperationsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/ColorOperationsTests.cs)、[TypedColorLayoutTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/TypedColorLayoutTests.cs)、
[TypedPackedPixelTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/TypedPackedPixelTests.cs)、[TypedColorModelTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/TypedColorModelTests.cs)、[CssColorsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Mathematics.Tests/CssColorsTests.cs)。
生の格納型、sRGB と線形の型、成分順は意味を確認する代表型を選び、同じ実装への転送だけを重複計時しない。

## メモリ・依存性注入

### CORE-MEM-01 — 管理器・スコープ・プールの初回と再参照

- 経路: [MemoryManager.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Memory/MemoryManager.cs)。MemoryManager 構築、CreateScope／DisposeAsync、GetSnapshot の新規プールと既存プール。1 操作は管理器寿命、スコープ寿命、または snapshot 取得のいずれか。代表 1／8 プール、境界 0 プール設定・空スコープ・不正予算、負荷 64 プール。初回は新管理器を使い、プール名を無制限に増やさない。正解は予算・初期会計値と解放成功。全。

### CORE-MEM-02 — 予約・確定・返却と予算境界

- 経路: [MemoryReservation.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Memory/MemoryReservation.cs)。IMemoryScope.ReserveAsync→IMemoryReservation.Commit→IMemoryCharge.Dispose と未確定予約の Dispose。1 操作は 1 所有寿命。代表 4 KiB Exact／Estimate、境界は soft／hard に一致・1 byte 超過・過少予約から hard 超の確定、負荷は同時 256 件まで。計測外で毎回会計を戻し、Reserved／Committed／Estimated／Released の差分を照合。外部資源の課金だけで実資源を確保したと主張しない。全。

### CORE-MEM-03 — チャンク内借用・新規・繰越

- 経路: [ThreadMemoryChunks.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Memory/ThreadMemoryChunks.cs)。公開入口 `IMemoryScope.RentAsync<byte>`／`RentAsync<int>` とバッファ Dispose。初回・繰越の 1 操作は 1 借用寿命、チャンク内ヒットは容量内の有限ウェーブを 1 操作とし、件数と借用当たりの換算を併記する。代表 chunkSize=65536 byte・要求256 byte、境界は残量丁度・残量+1・要求65537 byte・推奨容量が予算を超え要求量へ縮小、負荷は 1 MiB 以下の生存配列。ヒットは同一スレッドで先行借用を保持し、先行分を除いた残容量内に合計要求量が収まる回数を固定する。返却してもカーソルは戻らないため、ウェーブ外で全借用を返却し、新しいチャンクと先行借用を準備する。無制限反復をそのまま適用せず、この計測外resetができるjobのみを適格とする。容量超過・繰越は別プロファイルにし、最後の返却後に退役したチャンクを再利用ヒットと呼ばない。容量・非重複・課金とウェーブ件数を照合。全。

### CORE-MEM-04 — スコープ寿命と遠隔返却

- 経路: [MemoryScope.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Memory/MemoryScope.cs)。複数スコープで共有チャンクを保持し、buffer.Dispose／scope.DisposeAsync を分離。1 操作は 1 スコープの一括解放または 1 バッファ遠隔返却。代表 32 借用、境界は最終参照・二重返却・閉鎖後アクセス、負荷は 256 借用。準備時の所有集合を毎回作り直し、反復が空スコープ解放へ変わらないようにする。最終返却時のみチャンク課金が消えること、破棄後 Memory が拒否されることを照合。全。

### CORE-MEM-05 — 明示回収・失敗・取消

- 経路: [MemoryManager.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Memory/MemoryManager.cs)。RegisterReclaimer／ReclaimAsync と登録解除。1 操作は 1 登録寿命または 1 回収要求。代表 1／8 回収器・目標4 KiB、境界は目標未達・空登録・途中例外集約・事前取消・待機中取消、負荷は 64 回収器。公開 IMemoryReclaimer を実装した決定的な課金解放器を使い、毎回新しい charge を補充。返却値は申告量でなく ReleasedBytes の差分と照合。遅延注入を計時の主成分にしない。正常は全、取消の順序保証は確認／diagnose。

### CORE-MEM-06 — 共有予算と所有権の競合

- 経路: [MemoryScope.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Memory/MemoryScope.cs)。ReserveAsync の同一／別プール競合、RentAsync と最終遠隔返却、scope.DisposeAsync と借用の競合、同一プールの ReclaimAsync 直列化を別ケース化。1 操作は同期開始した有限ウェーブ全体。代表2／4ワーカ・各64操作、境界8ワーカで予算競合、負荷は各256操作・生存合計4 MiB以下。固定ワーカを準備しウェーブ後に join と解放を完了。許可された成功／拒否と最終会計ゼロを照合し、任意の成功順序を固定しない。全、競合の原因特定は diagnose。

### CORE-MEM-07 — 依存性注入の登録から破棄

- 経路: [MemoryServiceCollectionExtensions.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Memory.DependencyInjection/MemoryServiceCollectionExtensions.cs)。AddMemoryManagement(options)→BuildServiceProvider→IMemoryManager 解決→scope 借用→非同期解放を利用側統合系列にする。1 操作は 1 provider 寿命、登録・初回解決・既存 singleton 解決は別ケース。代表1設定プール、境界は重複登録・無効チャンク・設定後 options 変更、負荷は無関係サービス64件。各反復で新しい collection を構築し既存登録への重複へ変わらないようにする。singleton 同一性・設定コピー・最終解放を照合。標準管理器は全、独自 TManager 登録は確認専用。

正解根拠は [MemoryManagerTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Memory.Tests/MemoryManagerTests.cs) と [ThreadChunkTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Memory.Tests/ThreadChunkTests.cs)。
会計の桁あふれ後の再確定・二重確定・破棄順序違反は同試験に沿う確認ケースとし、巨大な実配列を確保して再現しない。
会計値、GC 割当、最後の借用までの配列保持を別出力にする。ネイティブ資源はこのライブラリ単独ケースでは確保しない。

## テキスト編集

### CORE-TEXT-01 — 構築・単一行正規化・文字境界

- 経路: [TextDocument.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.TextEditing/TextDocument.cs)。TextDocument 構築、Normalize、GraphemeBoundaries を別ケース化。1 操作は 1 文書構築または 1 文字列走査。代表 UTF-16 長256／4096の日本語・ASCII・結合文字・絵文字、境界は空・CRLF／CR／LF・不正 surrogate、負荷は32768 code units。構築上限は入力と一致する設定へ固定。改行のみ空白化し正規化形式を変えないこと、末尾境界を含むことを照合。全、不正 Unicode は確認。

### CORE-TEXT-02 — 置換・選択・原子的確定と破棄

- 経路: [TextDocumentTransaction.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.TextEditing/TextDocumentTransaction.cs)。BeginEdit→Replace／SetSelection→Commit と未確定 Dispose、TryReplace／TrySetSelection、ReplaceAll／ReplaceSelection を意味別に分ける。1 操作は 1 transaction または 1 command。代表4096文字の中央16文字置換、境界は先頭末尾・全置換・逆向き選択・長さ制限丁度、負荷32768文字で32編集。等長で異なる内容を交互に使い、選択も交互に動かす。公開前 snapshot 不変・確定後 revision と変更通知・破棄復元を照合。履歴は毎操作外で DrainChanges。全。

### CORE-TEXT-03 — 変換中文字列の更新と部分確定

- 経路: [TextDocumentTransaction.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.TextEditing/TextDocumentTransaction.cs)。SetComposition→Replace→Commit、同一 ID の範囲縮小、EndComposition(Commit／Cancel)。1 操作は 1 更新 transaction、開始から終了の寿命は別ケース。代表文書256文字・変換範囲16文字・属性8件、境界は範囲0・属性0／256件・範囲を跨ぐ置換・別ID、負荷文書32768文字で32更新。毎回同じ checkpoint から再構築。Text／CommittedText／Selection と取消後の部分確定保持、呼出元の属性リスト変更への独立性を照合。全、拒否は確認。

### CORE-TEXT-04 — 書記素単位の削除と論理移動

- 経路: [TextEditingCommands.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.TextEditing/TextEditingCommands.cs)。Execute(DeleteBackward／DeleteForward／SelectAll／Previous／Next／Start／End)。1 操作は 1 command。代表4096文字の結合文字・旗・絵文字列、境界は空・端・選択範囲削除・変換中拒否、負荷32768文字。削除後は元文書に戻し、移動は往復させる。削除は独立した既知書記素列で期待結果を定義。移動は ITextDocumentNavigator に公開契約の決定的な論理境界実装を渡し、その呼出しと選択を観測する。実フォント由来の視覚移動性能とはしない。全、端の無操作は確認。

### CORE-TEXT-05 — スナップショットと変更列の取得

- 経路: [TextDocument.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.TextEditing/TextDocument.cs)。CaptureSnapshot、DrainChanges。1 操作は 1 snapshot 取得または 1 変更バッチの drain。代表変更32件、境界は0／1件・選択のみ・変換属性のみ、負荷256件。文書長4096固定で事前に異なる編集を生成し、各 drain 後に補充。変更フラグ・revision順・origin と2度目が空であることを照合。snapshot の割当と snapshot を保持した際の旧文字列保持量は別観測。全。

### CORE-TEXT-06 — 改訂不一致と公開前検証

- 経路: [TextDocument.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.TextEditing/TextDocument.cs)。TryReplace／TrySetSelection の古い expectedRevision、BeginEdit の排他拒否、Replace の surrogate途中・範囲外・最大長超過。1 操作は 1 拒否試行。代表は現revisionとの差1、境界はUTF-16対の間とMaximumLength+1、負荷は256試行まで。毎回同じ合法文書から開始し、Text／Selection／Revision と変更列が不変であることを照合。拒否条件の異なる例外を混ぜない。確認、必要時のみ diagnose。

### CORE-TEXT-07 — 候補リストの検証と防御コピー

- 経路: [ImeCandidateValidation.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.TextEditing/ImeCandidateValidation.cs)。ImeCandidateValidation.Copy。1 操作は候補 snapshot の全検証とコピー。代表32候補・各16文字・4ページ、境界は0／1024候補・1候補4096文字・総262144文字・ID／index／ページ不正、負荷は1024候補×256文字の単一プロファイル。新規の不変入力を反復再利用し出力を保持し続けない。配列の別所有と内容・選択・ページ規則を照合。不正Unicodeや総長超過は確認、正常は全。

文書モデルの正解根拠は [TextDocumentTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/UI/Lumyte.UI.TextEditing.Tests/TextDocumentTests.cs)。
配置が UI の試験でも、ここで用いるのは文書モデルの確認だけで、描画・Parley・OS 接続は取り込まない。
候補コピーと移動については本体の検証分岐・公開インターフェースを根拠に追加の確認条件を定義し、既存試験が全ケースを保証すると主張しない。

## 既存の負荷定義を再利用する対応

| 固定ソースとメソッド | 対応系列 | 再利用する内容と分離する責務 |
| --- | --- | --- |
| [Program.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Mathematics.Benchmark/Program.cs) の Main／Optimized／Reference／Points／Rectangle | CORE-MATH-07 | シード9417・256組の座標／行列、4隅変換と境界縮約の参照解、結果消費を再利用。旧 array／LINQ 経路は比較対象の意味を明示して残す。 |
| [BatchBenchmark.cs](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Mathematics.Benchmark/BatchBenchmark.cs) の Single／Double／Reference | CORE-MATH-10 | シード9417、長さ3／32／1024／65536、点(1.25,-3.75)、scalar参照式と全要素照合を再利用。新たな同内容の測定プロジェクトへ複製しない。 |

両方とも配置済みの `benchmarks/Core/Lumyte.Mathematics.Benchmark` を拡充する案である。
独自 Stopwatch 区間、固定反復・warmup、手動の割当集計は共通測定境界へ置き換え、入力生成と正解検査の意味を保存する。
JIT／命令セット設定の表示は環境記録として扱い、過去の設定値や手動 GC を共通の既定へ昇格させない。参照ソースの負荷定義と過去の測定値は別物であり、過去の数値を今回の比較根拠にしない。
UI／Graphics のツールは Core に依存するだけでは本カタログへ移さない。純粋な幾何・色の処理だけを抽出する場合も、本表の系列と同じ入力・正解境界かを確認する。

## 実行の組合せと暫定候補

- まず上記の全系列から、正常・境界・寿命・競合の意味的な漏れをレビューする。実行頻度はモードとは別に決める。
- screen の暫定候補は CORE-MATH-10 の1024点、CORE-COLOR-02 の4096色、CORE-MEM-02 の予約寿命、CORE-MEM-03 の生存チャンク内借用、CORE-TEXT-02 の4096文字置換。CI 採用の確定ではない。
- compare は系列内の同じ入力・所有境界・実行環境について独立起動で行う。小さい時刻演算も十分な反復を使うが、複数APIをまとめて別の仕事へ変えない。
- 初回構築・色名初期化・新規チャンクは冷状態、既存プール・生存チャンク・定常純粋演算は温状態として別ケースにする。冷温を screen／compare の別名にしない。
- 競合・取消・保持量の診断は diagnose を別起動する。ワーカの生成、参照解の実行、GC強制、トレースを通常の計時へ混ぜない。
- PR ごとの確認、定期実行、手動実行への割当は、全体カタログと実行コストを確認してから選ぶ。ここでは実測時間・改善率・許容退行率を置かない。
