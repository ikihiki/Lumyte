# ADR-MATHEMATICS-0001: 2D・3D 幾何の精度を型引数で選択する

- 状態: 提案
- 日付: 2026-11-12
- 基準コミット: `1dbf62977a4805cb32d97811f8a890237bea20cb`

## 背景

利用者は、描画とは独立して 2D・3D の形状を表現し、座標の精度を用途ごとに選びたい。基準コードの Mathematics は補間を提供するが汎用の幾何型を持たず、Drawing2D の `Rect2D` は float の描画用矩形なので、double の幾何計算には利用側の型や計算が必要になる。

基準コードは `src/Core/Lumyte.Mathematics/Lumyte.Mathematics.csproj`、同ディレクトリの `Interpolation.cs`、`src/Graphics/Lumyte.Graphics.Drawing2D/Model/Rect2D.cs` の各宣言で確認できる。[ADR-0002 の各ディレクトリの役割](../0002-repository-layout.md#各ディレクトリの役割)と [ADR-ANIMATION-0004 の汎用数学計算の配置](../animation/ANIMATION-0004-format-independent-value-sources.md#汎用数学計算の配置)に従い、数学から描画・時計・Animation への依存は追加しない。

## 決定

既存の `Lumyte.Mathematics` パッケージに、名前空間 `Lumyte.Mathematics.Geometry` の不変な幾何値型を追加する。型名の `2D`・`3D` は座標空間の次元、型引数 `T` は成分と計算の浮動小数点型を表し、`Circle2D<float>` と `Circle2D<double>` を同じ API で使えるようにする。

### 利用者の変更前・変更後

目的は、中心からの距離で円の内側を判定し、同じ処理を必要な精度で使うことである。変更前は汎用円型が存在しないため、例えば double の計算を利用側で記述する。

```csharp
// 変更前: 呼び出し側の計算。Lumyte の既存 API ではない。
double centerX = 0, centerY = 0, radius = 5;
double x = 3, y = 4;
double dx = x - centerX, dy = y - centerY;
bool inside = Math.Sqrt(dx * dx + dy * dy) <= radius; // true
```

変更後は以下の提案 API を使う。境界上の点を含む結果は維持し、精度の選択を型引数へ移す。

```csharp
using Lumyte.Mathematics.Geometry;

var circle = new Circle2D<double>(new Point2D<double>(0, 0), 5);
bool inside = circle.Contains(new Point2D<double>(3, 4)); // true

var compact = new Circle2D<float>(new Point2D<float>(0, 0), 5);
bool same = compact.Contains(new Point2D<float>(3, 4)); // true

var sphere = new Sphere3D<double>(new Point3D<double>(0, 0, 0), 5);
bool inside3D = sphere.Contains(new Point3D<double>(0, 3, 4)); // true
```

型引数が変われば丸め誤差と表現範囲も変わる。上の小整数の例は結果を共有するが、任意の入力で異なる精度の判定が一致する保証はしない。

### 次元・精度と依存境界

すべての新規幾何型は `where T : unmanaged, IFloatingPointIeee754<T>` を使う。対応する組み込み型は `Half`・`float`・`double` とし、受け入れ検証では三つを区別する。int・decimal はこの制約に含めず、整数格子や十進演算は今回の対象外とする。制約を満たす独自型は同じ契約を満たす必要があり、個別の性能や精度は保証しない。

保存する成分、距離や半径の戻り値、演算の中間値は T とし、実装都合で float・double へ暗黙に変換しない。精度を上げる場合も呼び出し側が成分を明示変換して構築する。異なる T の暗黙変換や混合演算は追加しない。これは浮動小数点型の選択であり、任意精度や厳密な幾何判定を保証する仕組みではない。

位置と変位を区別する `Point2D<T>`・`Point3D<T>` と `Vector2D<T>`・`Vector3D<T>` を用意する。`System.Numerics.Vector2`・`Vector3` は float 固定なので保存形式には使わず、`System.Numerics.Vector<T>` も座標次元を表す型として使わない。新しいベクトル型は幾何演算に必要な範囲に限定し、既存補間 API のベクトル型は変更しない。

同じ名前空間へ両次元を配置し、2D・3D ごとにパッケージや名前空間を分割しない。値型は描画・物理のオブジェクトを保持せず、座標の単位、画面の上下方向、左右手系を固定しない。

### 形状と数値の契約

初期範囲は点、変位、線分、軸平行矩形・箱、円・球と、それらの下記主要演算とする。直線、半直線、平面、三角形、回転可能な箱、交差集合の計算、空間索引は今回決める範囲に含めない。

- 成分は有限値を要求する。コンストラクターと演算の引数に NaN・無限大があれば `ArgumentOutOfRangeException` とする。半径・幅・高さが負、または箱の各軸で Min が Max より大きい場合も同様とする。
- `default` は各成分ゼロの有効値である。半径ゼロ、幅・高さゼロ、Min と Max が同じ箱、始点と終点が同じ線分を許す。これらは空集合ではなく、点や線へ退化した閉集合として扱う。
- `Contains` は境界を含む。矩形・箱は各軸の閉区間、円・球は中心からの距離が半径以下の領域とする。ゼロ半径の円・球は中心だけを含む。距離はユークリッド距離で、点から線分への距離は射影を端点で制限し、ゼロ長線分では端点への距離を返す。
- 同値性は成分ごとの T の数値等値とし、許容誤差を混ぜない。正負のゼロは等値として同じハッシュを返す。近似比較や許容誤差付き判定の API は今回追加しない。
- 数値処理は不要な二乗オーバーフローを避けるスケーリングを行う。有限入力でも必要な差や最終結果を T の有限値として表現できない場合は `OverflowException` とし、NaN・無限大を正常な計算結果として返さない。`Contains` も演算不能を false に読み替えない。
- 例として中心ゼロ・半径 5 の円は (3, 4) を含み、(3, 4.5) を含まない。幅ゼロの矩形はその直線上の閉区間を含む。double の (1e200, 0) と原点の距離は有限の 1e200 を返し、不要な二乗の無限大を返さない。

不変な値型なので、破棄・スレッド所有・更新通知の仕組みは持たない。GPU バッファとのメモリ配置互換や SIMD 化、割り当て・速度の数値目標はこの判断では保証しない。

### 公開 API 差分

基準コードに対する追加の主要宣言を以下に示す。宣言は提案の抜粋であり、実装コードではない。省略した各型の読み取り専用プロパティはコンストラクター引数の先頭を大文字にした同名・同型とし、全型で成分の値等値を提供する。引数の有限性、退化形状、算術失敗は「形状と数値の契約」に従う。

```diff
+using System.Numerics;
+
+namespace Lumyte.Mathematics.Geometry;
+
+public readonly struct Point2D<T> where T : unmanaged, IFloatingPointIeee754<T>
+{
+    public Point2D(T x, T y);
+    public T X { get; }
+    public T Y { get; }
+    public T DistanceTo(Point2D<T> other);
+    public static Vector2D<T> operator -(Point2D<T> left, Point2D<T> right);
+    public static Point2D<T> operator +(Point2D<T> point, Vector2D<T> offset);
+}
+
+public readonly struct Point3D<T> where T : unmanaged, IFloatingPointIeee754<T>
+{
+    public Point3D(T x, T y, T z);
+    public T X { get; }
+    public T Y { get; }
+    public T Z { get; }
+    public T DistanceTo(Point3D<T> other);
+    public static Vector3D<T> operator -(Point3D<T> left, Point3D<T> right);
+    public static Point3D<T> operator +(Point3D<T> point, Vector3D<T> offset);
+}
+
+public readonly struct Vector2D<T> where T : unmanaged, IFloatingPointIeee754<T>
+{
+    public Vector2D(T x, T y);
+    public T X { get; }
+    public T Y { get; }
+}
+
+public readonly struct Vector3D<T> where T : unmanaged, IFloatingPointIeee754<T>
+{
+    public Vector3D(T x, T y, T z);
+    public T X { get; }
+    public T Y { get; }
+    public T Z { get; }
+}
+
+public readonly struct Segment2D<T> where T : unmanaged, IFloatingPointIeee754<T>
+{
+    public Segment2D(Point2D<T> start, Point2D<T> end);
+    public T DistanceTo(Point2D<T> point);
+}
+
+public readonly struct Segment3D<T> where T : unmanaged, IFloatingPointIeee754<T>
+{
+    public Segment3D(Point3D<T> start, Point3D<T> end);
+    public T DistanceTo(Point3D<T> point);
+}
+
+public readonly struct Rect2D<T> where T : unmanaged, IFloatingPointIeee754<T>
+{
+    // X・Y が最小側、Width・Height が非負の長さ。最大側も有限であること。
+    public Rect2D(T x, T y, T width, T height);
+    public bool Contains(Point2D<T> point);
+}
+
+public readonly struct AxisAlignedBox3D<T> where T : unmanaged, IFloatingPointIeee754<T>
+{
+    public AxisAlignedBox3D(Point3D<T> min, Point3D<T> max);
+    public bool Contains(Point3D<T> point);
+}
+
+public readonly struct Circle2D<T> where T : unmanaged, IFloatingPointIeee754<T>
+{
+    public Circle2D(Point2D<T> center, T radius);
+    public bool Contains(Point2D<T> point);
+}
+
+public readonly struct Sphere3D<T> where T : unmanaged, IFloatingPointIeee754<T>
+{
+    public Sphere3D(Point3D<T> center, T radius);
+    public bool Contains(Point3D<T> point);
+}
```

`Rect2D<T>` の x + width、y + height が有限に収まらない場合、構築時に `OverflowException` とする。`Sphere3D<T>` は球であり、3D 空間内に置いた円を意味する `Circle3D<T>` と混同しない。軸平行を表す型名は、回転可能な箱と区別できる `AxisAlignedBox3D<T>` とする。

既存 `Lumyte.Graphics.Drawing2D.Rect2D` の名前空間、float 成分、コンストラクターは維持する。数学側へ描画型を参照させず、利用側は成分を取り出して描画型を構築する。double などから float へ落とす際は利用側が範囲・丸めを確認し、変換結果が有限かを検証する。補間 API のジェネリック化、描画 API の置換、暗黙の相互変換は含めない。

## 検討した代替案

- `Circle2F`・`Circle2D` のように精度別の型名を用意する: 各型を最適化しやすいが、次元と精度の D が紛らわしく、同じ処理の宣言と検証が増える。次元は名前、精度は型引数へ分離する。
- `Geometry2D.Circle<T>`・`Geometry3D.Sphere<T>` と次元別に配置する: 型名は短くなるが、両次元を使う処理で修飾や別名が増える。共通名前空間と次元接尾辞を選ぶ。
- `INumber<T>` 制約で整数・decimal も一括対応する: 保存型を広げられるが、距離の平方根、丸め、オーバーフローの契約が数値体系ごとに異なる。今回は IEEE 浮動小数点に絞る。
- すべて double、または既存の float ベクトルへ統一する: 宣言は簡単だが、利用者が成分型と計算精度を選べない。保存だけ T にして内部を double へ固定する案も採らない。

## 結果と影響

利用者は次元と精度を独立して選び、2D・3D の幾何を描画依存なしで扱える。既存コードは書き換え不要だが、新しい幾何型と描画型の境界では明示的な成分変換が必要になる。ジェネリック化により型ごとのコード生成量や最適化の差が生じるため、SIMD の利用や高速化は保証ではなく実装時の確認対象となる。

受け入れ条件は、Half・float・double での構築、境界を含む内外判定、2D・3D の距離と線分への射影、退化形状、default、正負のゼロの等値・ハッシュを検証することである。NaN・無限大・負の寸法・逆転した境界の拒否、表現限界付近の距離、算術結果のオーバーフロー、精度ごとの丸め差も確認する。double の値が float に狭められていないことは、float では区別できない座標差を使って確認する。

既存 Mathematics・Animation・Drawing2D のテストと公開宣言を照合し、幾何追加によって依存方向や既存 API の意味が変わらないことを確認する。この文書は設計提案であり、コンパイル・実行可能性や各受け入れ条件の達成は実装の検証を要する。
