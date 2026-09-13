# ADR 0035: 形式とシーン管理に依存しない汎用 Model RenderPass

## 状態

採用（目標設計）。Model 描画を独立した機能契約とし、ファイル由来のモデル、ECS の描画対象、Component から合成した描画データ、実行時に生成・更新する geometry を同じ API で扱う。glTF 2.0 core と採用拡張の描画能力を初期要件に含める。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

| ADR | 利用する契約 |
| --- | --- |
| [0029 Resource Management](0029-resource-management-api.md) | 二系統の資源、descriptor／binding、GPU 配置と使用保持 |
| [0030 RenderGraph](0030-render-graph-api.md) | 共通の要求、論理 texture、不変の upload data と非同期提出 |
| [0031 Native RenderGraph](0031-native-render-graph-implementation.md)・[0032 Portable RenderGraph](0032-portable-render-graph-implementation.md) | 各系統の内部 pass、GPU program と upload の準備 |
| [0033 機能 RenderPass](0033-feature-render-passes.md) | 共通契約と二つの実装 library |
| [0034 RenderPass カテゴリ](0034-render-pass-categories.md) | Model、画像処理、合成と出力の責務分担 |

## 決定

`AddModelPass` は geometry、material、最終変換と任意の変形データを組にした `ModelDrawItem` の列を受け取り、選択した camera から scene-linear HDR の Color と Depth を描く。Model は描画機能の名前であり、単一ファイル、node tree や ECS Entity の所有単位を意味しない。

共通化するのは geometry・材質・変形の意味と外部出力である。Native／Portable の本体は GPU 構造体、配置、変形処理、culling、batch と内部 pass をそれぞれ実装する。利用 library は同じ `AddModelPass` を呼び、shader を管理せず、GPU API の選択で再ビルドしない。

Native は vertex／indexed 経路に加えて optional な mesh shader 経路を持つ。どちらも同じ `ModelGeometryData`、index、material と draw item を入力にし、利用者に meshlet、mesh shader、amplification payload や GPU の上限を渡させない。Portable は自身の vertex／indexed 経路を実装し、mesh のエミュレーションを要件にしない。PBR 等の計算を Slang source で共有できる部分と、各系統の entry／resource／root ABI は分ける。

入力を geometry、頂点属性、index、material、画像、skin palette、morph weights に分ける。部品を直接参照して描画を合成し、共有と改版の単位をモデル全体に固定しない。node、親子関係、scene 選択、Entity ID、Component container、animation clip を Graphics の必須入力にしない。

連続描画では `ModelDrawList` に描画対象を保持し、変更項目だけを更新する。`Snapshot()` は未変更部分を共有する不変状態を返す。固定の RenderGraph は一度 Compile し、`ModelRenderSnapshot` を型付き入力 slot へ渡して同じ plan を提出する。全 draw の再抽出・配列コピー・子資源の再列挙を毎フレーム必須にしない。

ファイル取得、依存解決と形式の decode は `Lumyte.Resources` の分野とする。実行時の階層評価、animation 混合、IK、物理、手続き生成と Component からの描画データ抽出は、ECS／scene library／application などの上位処理が担当できる。実行時データを一度ファイル資産や Resources のロード経路へ変換する必要はない。Graphics は受け取るデータ型と描画 API を定義し、loader、ECS query、node 管理や評価サービスを追加しない。

### 上位の管理と描画境界

| 供給側 | 上位が管理するもの | Graphics に渡すもの |
| --- | --- | --- |
| glTF や別のモデル形式 | ファイル、依存、形式固有の scene・材質・animation | 共通の geometry、material、画像と評価済み描画値 |
| ECS | Entity の世代、Component、親子関係、更新順序 | 必要な Component を組にした、その時点の draw item 列 |
| 通常の scene library | node 階層、可視性、部品の配置と再利用 | 最終変換と描画対象。階層自体は渡さない |
| 手続き生成・simulation | 現在の頂点、index、材質、姿勢 | 所有済みの不変データ世代と描画範囲 |

同じ geometry を別の material と transform で何度でも参照できる。一つの Entity が複数 draw item を出してよく、複数 Entity が同じ部品を共有してよい。GeometryComponent、MaterialComponent、TransformComponent、SkinComponent 等は上位の構成例であり、Graphics 指定の基底 Component や ECS 実装への依存は設けない。light と camera も独立して抽出する。

## API

### Geometry と個別の転送データ

共通型は `Lumyte.Graphics.Passes` に置く。`IGpuUploadData` の Key は [ADR 0030](0030-render-graph-api.md) の不変内容の識別子であり、ロード用 ID ではない。collection と memory は所有する不変値とし、GPU handle、shader struct と native API の型を含めない。

| API | 契約 |
| --- | --- |
| `ModelGeometryData(Key, Topology, Vertices, Indices = null, MorphTargets = []) : IGpuUploadData` | 一つの接続方式を持つ geometry。頂点属性、任意の index、morph target を直接参照する。material、node、skeleton 階層を所有しない |
| `ModelTopology` | `Points / Lines / LineLoop / LineStrip / Triangles / TriangleStrip / TriangleFan`。入力の接続関係であり、低レベル topology の対応を要求しない |
| `ModelVertexAttributeData<T>(Key, Values) : IGpuUploadData` | 一つの意味属性の所有済み値列。各属性が独立した内容世代を持つ。T は下記で定める Vector2／Vector3／Vector4 とし、任意の GPU ABI bytes の入口にはしない |
| `ModelVertexData(Positions, Normals = null, Tangents = null, TexCoords = [], Colors = [], SkinInfluences = null)` | 属性の組。Positions／Normals は Vector3、Tangents は Vector4（W は接空間の向き）。Positions は必須、存在する他属性の頂点数は一致させる |
| `ModelTexCoordSet(SetIndex, Values)` | 指定 UV set の `ModelVertexAttributeData<Vector2>`。材質が使用する set を固定個数へ切り詰めない |
| `ModelColorSet(SetIndex, Values)` | 指定 color set の `ModelVertexAttributeData<Vector4>`。linear straight RGBA。RGB source の alpha 補完は供給側が済ませる |
| `ModelIndexData(Key, Values) : IGpuUploadData` | geometry の頂点を指す uint 列。Indices が null なら non-indexed、存在する空列は index 数 0 であり、non-indexed に切り替えない |
| `ModelVertexAttributeData<T>.WithRange(nextKey, first, values)`／`ModelIndexData.WithRange(nextKey, first, values)` | 要素数を変えず指定範囲を置換した新しい不変世代を返す。first は頂点／index 要素単位。新値を所有・コピーし、未変更 page を共有する。GPU を更新する操作ではない |
| `ModelSkinInfluenceData(Key, VertexOffsets, Influences) : IGpuUploadData` | 頂点数 + 1 個の offset と連続 influence 列。隣接 offset 間が一頂点の全 influence。頂点ごとに可変個数を保持する |
| `ModelJointWeight(JointIndex, Weight)` | 描画時に渡す palette の添字と weight。Entity、node、骨名やファイル内 index を解決する操作を含めない |
| `ModelMorphTargetData(Key, PositionDeltas, NormalDeltas, TangentDeltas, TexCoordDeltas, ColorDeltas) : IGpuUploadData` | target 一つの属性変位。位置・法線・tangent は Vector3 属性データ、UV／color は set ごとの属性データ。省略列は変位 0、tangent の W は変位させない |
| `ModelDrawRange(First, Count)` | index があれば index 列、なければ頂点列の部分範囲。Count 0 は描画無し。各範囲の接続は独立し、strip／fan を前の draw に接続しない |

各 index は geometry の頂点列を直接指し、First を頂点 index へ加算しない。draw ごとに範囲を選べるため、頂点・index の集合を共有したまま submesh ごとに material を組み合わせられる。index 幅や interleave、圧縮、buffer 分割、alignment は各本体が選ぶ。

position のみを変更する場合は、新しい position 属性とそれを参照する geometry の世代を作り、normal 等の変化しない属性、index、material と画像の値・Key を維持する。geometry の集約 Key の変更は、子データすべての再転送を要求しない。含まれる子の内容が変わったのに親の Key を再使用してはならない。

### 独立した material と画像

| API | 契約 |
| --- | --- |
| `ModelMaterialData : IGpuUploadData` | Key と `BaseColorFactor / MetallicFactor / RoughnessFactor / EmissiveFactor / EmissiveStrength / AlphaMode / AlphaCutoff / DoubleSided / Unlit` を持つ不変の材質。geometry とは独立して共有・改版する |
| `BaseColorTexture / MetallicRoughnessTexture / NormalTexture / OcclusionTexture / EmissiveTexture` | 任意の `ModelTextureUse`。用途を明示し、GPU slot や binding index を含めない |
| `NormalScale / OcclusionStrength` | normal map の XY scale と occlusion の強度。texture が無ければ対応効果を加えない |
| `ModelAlphaMode.Opaque / Mask / Blend` | alpha の描画意味。AlphaCutoff は Mask に使用する |
| `ModelTextureData(Image, Sampler)` | decode 済み `GpuImageUploadData` と `ModelSamplerData`。URI や画像ファイル bytes を受け取らない |
| `ModelTextureUse(Texture, TexCoordSet, Transform)` | texture、UV set index と Matrix3x2 の UV 変換。source の変換順は確定済み |
| `ModelSamplerData(MinFilter, MagFilter, MipFilter, WrapU, WrapV)` | sampling の意味。`ModelFilter` は Nearest／Linear、`ModelMipFilter` は None／Nearest／Linear、`ModelWrap` は ClampToEdge／MirroredRepeat／Repeat |
| `GpuImageUploadData / GpuImageSubresourceData` | ADR 0030 の共通画像型。寸法、画素 format、色・alpha、mip／layer と row／slice stride を明示する |

初期の材質契約は metallic-roughness PBR と Unlit とする。base color／emissive の RGB は色として、normal／occlusion／metallic-roughness は数値として読む。metallic-roughness の G は roughness、B は metallic、occlusion の R は遮蔽値である。base color に color set 0 があれば RGBA を乗算し、他の color set も入力では保持する。材質の省略値と入力 channel の意味は [Metallic-Roughness schema](https://raw.githubusercontent.com/KhronosGroup/glTF/main/specification/2.0/schema/material.pbrMetallicRoughness.schema.json) に対応させる。別形式の importer や手続き生成側もこの描画意味へ値を整える。

同じ geometry に material を差し替える場合は draw item の参照だけを変える。材質値・texture の更新も、既存 geometry や他の material の内容世代を変えない。同じ decoded image を色と数値の両方で使う場合は、用途と入力の変換済み状態に合わせて各本体が view／資源を用意し、二重に復号・premultiply しない。

汎用性は任意の shader や全形式の材質を暗黙変換する保証ではない。高度な材質、追加の属性意味や別の変形方式は、それらの入力型と機能契約、両本体の処理を定めて追加する。未知の property bag を shader reflection で解釈する API は設けない。

### 描画単位、変形とフレームの入力

| API | 契約 |
| --- | --- |
| `ModelSkinPaletteData(Key, JointMatrices) : IGpuUploadData` | 入力頂点を draw local 空間へ skin する最終 Matrix4x4 列。必要な bind pose／inverse bind と joint の評価は反映済み |
| `ModelMorphWeightsData(Key, Weights) : IGpuUploadData` | geometry の MorphTargets の順に対応する評価済み weight 列。既定値と animation の適用は供給側で完了する |
| `ModelDeformationData(Skin = null, Morph = null)` | 任意の palette と weights の直接参照。skin と morph を独立して共有・更新できる。morph 後に skin を適用する |
| `ModelDrawItem(Geometry, Material, LocalToWorld, Deformation = null, Range = null, Visible = true)` | 一つの geometry の範囲を、一つの material と最終変換で描く不変値。Range が null なら列全体。Visible が false の項目は集合内の位置と参照を保ち、描画しない。Model 内 index や Entity ID の照合を要求しない |
| `ModelRenderSnapshot(Camera, Draws, Lighting)` | camera、`ModelDrawSnapshot` と lighting の不変な組。カメラだけの更新で draw 集合を作り直さない。node 階層や Component の合成は上位で完了する |
| `ModelCamera.Perspective(eye, target, up, verticalFieldOfView, near, far)` | world 空間の視点と radian 単位の垂直画角。far の省略は無限遠。aspect は出力範囲から決める |
| `ModelCamera.Orthographic(eye, target, up, verticalSize, near, far)` | world 空間の視点と高さで指定する平行投影 |
| `ModelCamera` | 不変の視点、projection の意味と任意の固定 aspect。固定 aspect は出力内へ等比で収め、余白は clear 値とする |
| `ModelLighting(Lights, Environment = null)` | world 空間の light と任意の環境照明。空は照明無しであり、隠れた既定 light はない |
| `ModelLight.Directional(direction, color, intensity)` | world 空間の光の進行方向、linear RGB と照度 |
| `ModelLight.Point(position, color, intensity, range)` | world 空間の位置、linear RGB、光度と任意の減衰範囲 |
| `ModelLight.Spot(position, direction, color, intensity, range, innerAngle, outerAngle)` | point の値に world 空間の方向と radian 単位の円錐半角を加える |
| `ModelEnvironment(Image, Rotation, Intensity)` | linear sRGB primaries の HDR equirectangular radiance である画像、world 空間の回転と非負倍率。GPU 前処理は本体が所有する |

skin を使わない draw は geometry の頂点に LocalToWorld を掛ける。skin を使う draw は palette の結果へ LocalToWorld を一度だけ掛ける。palette が返す draw local 空間は供給側が選べる。例えば glTF の skinned mesh は評価済み joint と inverse bind からモデル基準の palette を作り、LocalToWorld にモデル全体の配置を渡せる。非 skinned mesh は評価済み node の配置を LocalToWorld に合成する。この接続に mesh node の逆行列は必要なく、mesh node transform を skin 結果へ二重に掛けない。

CPU の行列は [System.Numerics の row-vector 規約](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.matrix4x4#remarks) とし、点 p の world 座標は非 skin なら p * LocalToWorld、skin なら各 p * JointMatrices[j] の重み付き和 * LocalToWorld とする。morph はこの p と対応属性へ先に反映する。shader 側の転置・packing は各本体が合わせる。Skin が null なら influence を使用せず、Morph が null なら全 weight は 0 とする。有効な Skin／Morph を渡した場合は必要な influence／palette の添字と target 数を一致させ、4 influence や固定 morph 数へ切り詰めない。

座標は右手系、上方向 +Y、距離は meter とし、camera は自身の -Z を向く。最終 camera と light は上位が world 空間へ評価する。光の強度と減衰は [KHR_lights_punctual](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_lights_punctual) の意味を共通の照明契約にも採用する。version 1 は影を生成せず、environment は材質を照らすだけとする。利用者は cube texture、BRDF LUT、sampler を作らない。

### 保持する描画集合

| API | 契約 |
| --- | --- |
| `ModelDrawList()` | CPU 上の描画項目と明示順序を保持する編集用集合。GPU object、ECS Entity、親子関係を管理しない |
| `Add(draw)` | 末尾へ一項目を追加し、`ModelDrawHandle` を返す |
| `Set(handle, draw)` | 指定項目の不変値を置換する。呼出し側は変更項目だけ渡せる。固定サイズの draw 値と参照世代を比較し、変更 field を内部で区別する |
| `Remove(handle)` | 項目を集合から除く。発行済み snapshot に含まれる項目は変化しない |
| `MoveBefore(handle, before = null)` | 描画の明示順序を変える。before が null なら末尾。geometry や material の再登録を要求しない |
| `ModelDrawHandle` | 集合、slot と再使用世代の識別値。別集合や削除後の handle を受け付けない。Entity ID、GPU index、資源所有権ではない |
| `Snapshot()` | 現在の全状態を表す `ModelDrawSnapshot`。変更がなければ同じ不変 root を返す |
| `ModelDrawSnapshot.From(items)` | 小さい単発入力や一括初期登録向けに列を一度コピー・固定する。大量項目の毎フレーム更新には ModelDrawList を使う |
| `ModelDrawSnapshot.Count / Items` | 項目数と明示順序での読取り列。全列挙は可能だが提出時の必須操作にはしない |

ModelDrawList の編集は呼出し側で直列化し、同時に編集しながら Snapshot しない。ECS の change version や dirty list は上位が解釈し、変更した Component に対応する handle へ Set／Remove を送る。Graphics が全 Entity を比較走査する処理や独自の simulation thread を持たない。

snapshot は順序付き page／部分木を共有し、変更した項目と必要な索引だけを新しくする。各部分には参照する upload data の所有集合を保持する。変更 k 件に対する固定サイズ page と索引更新を許すが、未変更 N 件の draw コピー、deep hash、資源列挙を毎回行う実装を採用しない。snapshot／bindings の取得も同じ共有情報を使う。

CPU の機能 library は、不変 page の同一性・子参照・変更索引を Native／Portable の本体が利用できる内部契約として提供する。公開 Items の全列挙だけを差分準備の入口にしない。内部情報も完全な snapshot を基準にし、raw GPU address や低レベルの GPU ABI は含めない。

完全な snapshot と変更索引の扱いは [ADR 0030 の不変入力と使用保持](0030-render-graph-api.md#不変入力と使用保持) に従う。Model では list の編集世代、handle の再使用世代、upload data の Key.Revision と GPU allocation 世代を区別する。

### Pass の追加と実装

| API | 契約 |
| --- | --- |
| `graph.AddModelPass(name, request)` | `ModelPassContract.Instance` を共通 AddPass に渡し、`ModelPassResult` を返す |
| `ModelPassRequest(Data, Color, Depth)` | Data は `GpuGraphValue<ModelRenderSnapshot>`。不変値または型付き入力 slot を受け取る。Color／Depth は固定の logical texture。ClearColor は透明な黒、ClearDepth は 1 が既定値 |
| `ModelPassResult(Color, Depth)` | 全体を初期化した Color／Depth。後続 pass が利用できる |
| `ModelPassContract.Instance / Id / Version` | ID は `lumyte.model.render`、初期 Version は 1。同じ値を双方の registry へ登録する |
| `ModelPassContract.Snapshot(request)` | 固定の出力と定数値／input slot を固定する。生の ECS query や更新中の配列を後で読む契約にしない |
| `ModelPassContract.Declare(context, request)` | Color／Depth を Write とし、`ReadInput(request.Data, ModelRenderInputContract.Instance)` を登録する。フレームごとの draw 数や資源を Compile 時に展開しない |
| `ModelRenderInputContract.Instance` | `IGpuGraphInputContract<ModelRenderSnapshot>`。Snapshot は描画条件を固定して Draws の不変 root を共有する。Retain は型の直接参照と既存の所有情報から geometry／属性／index／morph target／material／palette／画像を ReadUpload で保持し、未変更部分を再走査しない |
| `NativeModelPass`／`PortableModelPass` の `BuildAsync` | `context.GetInput(request.Data)` で今回の不変値を取得し、必要な差分の packing、GPU 資源と内部 pass を準備する。準備済みの shader package は専用 factory から受け取る。Native は必要なら meshlet の構築と mesh 経路の選択も行う |
| 各本体の `DisposeAsync()` | runtime が構築と GPU 使用の終了を保証した後、実装所有の cache と保持を終了する |

shader package の取得と展開は host／Lumyte.Resources が担当する。各本体は準備済み package に対する自系統の ShaderLoader.Load(package) で program を準備する。AddModelPass の利用者に shader 管理を要求しない。

Native の vertex、mesh-only、amplification 付き経路はそれぞれ別の program／package とし、必要な準備済み package を host から factory に渡す。[Native Shader package](0015-native-shader-package-api.md) の loader が一つの program を別の stage 構成へ変換する契約にはしない。`NativeModelPass` が program と対応する GPU input／root の型・packing を組で選び、Portable の package／loader は独立したままとする。

初期出力は、同サイズの 2D texture、mip／layer／sample count は各 1、Color は目標追加する `GpuFormat.Rgba16Float`、Depth は `GpuFormat.D32Float` とする。Color は linear sRGB primaries の scene-linear HDR、premultiplied alpha で、SDR 範囲へ clamp しない。ClearColor は straight alpha の linear RGBA とし、clear 時に RGB を alpha で乗算する。camera の余白を含め全体を clear する。空の Draws も正常な入力であり、clear だけを行う。

Depth は near 0／far 1 とし、Opaque／Mask が LessEqual の深度比較と書込みを行う。Blend は深度を読み、書き込まない。ClearDepth は 0 から 1 とし、指定値によって比較規約を変えない。別 format や MSAA は対応契約を追加してから使う。

## 再利用、動的データと所有

静的資産と動的生成物を同じ転送型で扱う。transform だけでなく、position・normal 等の属性、index、topology、draw range、material、画像、skin palette と morph weights の更新を受け付ける。頂点／index 数が増減する生成 geometry も新しい内容世代として渡せる。GPU allocation を caller が更新・再作成する API は要求しない。

| 更新 | 供給側が渡すもの | 再利用できるもの |
| --- | --- | --- |
| 配置・可視性 | 新しい draw item の値・列 | geometry、material、画像、変形データ |
| 材質 | 新しい material 世代または別 material の参照 | geometry と変更していない画像 |
| 一部の頂点属性 | 変更属性とそれを参照する新しい geometry 世代 | 同じ Key の他属性・index・material |
| index／topology／範囲 | 必要な index／geometry 世代または draw range | 変更していない頂点属性 |
| animation／物理変形 | 評価済み palette／weights、または直接生成した頂点属性 | 静的 geometry のうち変更していない部品 |
| 画像 | 新しい画像世代と参照する material 世代 | geometry と他の画像 |

入力は各世代の完全な値を持つ。WithRange は完全な新しい属性／index の値と変更範囲の情報を作り、GPU cache が該当基底世代を持つ場合に必要範囲だけ再 packing・転送できる。基底がなければ完全値から再構築する。差分しか存在せず過去世代のロードが必要になる入力は設けない。変更しない不変 memory は構造的に共有でき、生成側が新しい memory の所有を移譲して全配列コピーを避けてもよい。ECS の可変配列を ReadOnlyMemory で包んだだけの値や、書換え可能な Component への参照を Snapshot 後まで残さない。抽出中の読み取り整合性と同期は上位側が確保する。

各本体は部品ごとの Key、GPU packing の版、実装系統と device を cache identity に使う。cache が有効な未変更部品を、別部品の改版だけを理由に再転送することは要求しない。物理的には適合する部品をまとめて配置してよいが、動的属性の更新を material・画像の再生成に結び付けない。cache eviction 後の再転送は保持済み CPU データから行う。

入力部品の転送 cache と、生成した normal／tangent、topology 展開、変形結果や bounds の cache は区別する。派生結果の key には、実際に依存する draw range、normal map の UV set・変換、palette／morph の世代、最終変換等も含める。同じ geometry を別の描画条件で使った結果を誤共有しない。依存しない材質値の変更まで geometry の再転送理由にしない。

meshlet の接続情報も派生 cache とし、入力 topology、index の内容世代（non-indexed なら頂点列の個数と順序）、draw range、分割方式・packing の版と device の関連上限を key に含める。位置に基づいて分割する方式なら position の内容世代も含める。接続だけで分割する方式は position 更新だけで再分割せず、頂点 data と bounds を更新できる。meshlet ごとの bounds／cone と可視 list は接続 cache と分離し、実際に依存する position、morph、skin、transform、camera 等の世代・値を追跡する。変形後に有効と保証できない静的 cone で culling しない。

転送順序と提出前後の世代の公開は [ADR 0030 の GPU 内容世代と提出結果](0030-render-graph-api.md#gpu-内容世代と提出結果) に従う。各本体は部品別 cache に provider の内容世代 ticket を保持し、RegisterContent／TryUseContent で転送結果・依存・使用保持を接続する。CPU の packing が完了しただけで、geometry、材質や変形結果の GPU 世代を再利用可能と扱わない。

meshlet の GPU data、生成した geometry／可視 list／間接引数もこの内容世代 SPI を使う。生成に使う copy／compute を内部 graph の writer に登録し、後続 mesh／amplification stage の読取りと indirect 引数への依存を宣言する。失敗した世代の再利用や受理済み writer の早期回収を防ぎ、cache が失効した場合は保持した完全な CPU 入力から再構築する。

共有するのは GPU 資源だけではない。各本体は安定した draw の分類、参照の対応、opaque batch、内部 graph の template と CPU 側の使用保持情報も再利用する。transform 一件の更新で全 draw の材質分類や GPU 資源を作り直さない。material の値だけの変更と AlphaMode／texture 組合せの変更は影響範囲が違い、後者では該当 batch／binding を更新する。

共通 plan の再利用条件は pass と外部 Read／Write、入出力の形状が同じことである。draw の追加・削除・移動、Visible、camera、material や geometry の更新はこの条件を変えず、Model の内部処理だけを更新する。内部 pass の構成が変われば該当 provider の内部計画を更新し、共通 graph を作り直す理由にはしない。出力サイズ、pass の増減、外部資源の依存が変わる場合は新しい共通 plan を作る。

culling の bounds は今回の geometry、draw range、morph、skin と transform に対応させる。変形後の実形状またはそれを必ず包含する bound を各本体が用意し、古い静的 bound で動的 geometry を除去しない。透明 sort は後述の共通規約で計算し、culling 用の保守的な bound の選び方で描画順を変えない。

## コード配置

以下は repository root からの相対パスによる目標配置である。feature／Hosting／test project は新設予定とし、公開モデルをファイル形式や GPU の構造体配置から独立させる。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Passes/Models/` | `ModelPassRequest`／result／contract、`ModelRenderInputContract` と `AddModelPass`。 |
| `src/graphics/Lumyte.Graphics.Passes/Models/Data/` | geometry、属性、index、material／texture use、skin palette、morph、camera／lighting の不変転送値と範囲更新。GPU pointer／binding 用の型は置かない。 |
| `src/graphics/Lumyte.Graphics.Passes/Models/Draws/` | `ModelDrawItem`、`ModelRenderSnapshot`、`ModelDrawList`／handle／snapshot と変更 page・所有情報の共有。ECS／node の管理を含めない。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/Models/` | `NativeModelPass` と内部の変形・culling・描画・環境前処理。`GpuData/`、`Upload/`、`Cache/` に専用 packing、差分転送、部品／draw の cache を置く。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/Models/Mesh/` | meshlet 分割・packing、対応 artifact と描画条件による経路選択、mesh／amplification の内部 pass と派生 cache。ファイル decode や ECS 抽出は行わない。 |
| `src/graphics/Lumyte.Graphics.Portable.Passes/Models/` | `PortableModelPass` と自身の内部処理。`GpuData/`、`Upload/`、`Cache/` に Portable の入力、転送、binding 単位の batch と cache を置く。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/Models/Shaders/*.slang`、`src/graphics/Lumyte.Graphics.Portable.Passes/Models/Shaders/*.slang` | 系統別の PBR／Unlit、skin／morph、環境前処理と entry／resource adapter。Native は vertex／mesh／amplification の各 entry もここに置く。Portable は必要な entry を直接 WGSL の `*.wgsl` として実装してよい。生成 C# と artifact は各 project の `obj/<Configuration>/<TargetFramework>/Shaders/Native/` または `Shaders/Portable/` に出力する。 |
| `src/graphics/Shaders/Shared/Models/`、`src/graphics/Shaders/Shared/Math/` | 両系統が build 時に import する Slang の材質・変形・色・数学の計算 module。entry、descriptor／binding 宣言、GPU ABI 型と runtime project は置かない。 |
| `src/graphics/Lumyte.Graphics.Passes.Hosting/Models/` | `AddModelRendering()`、両本体の登録と、準備済み shader package を factory に渡す起動用定義。 |
| `src/graphics/Lumyte.Graphics.Passes.Tests/Unit/Models/` | owning data、範囲更新、draw handle と snapshot、契約の宣言を検証する隣接 xUnit project。 |
| `src/graphics/Lumyte.Graphics.Native.Passes.Tests/Unit/Models/`、`src/graphics/Lumyte.Graphics.Portable.Passes.Tests/Unit/Models/` | fake による差分準備、世代、cache、透明順序と失敗時の保持の xUnit 試験。 |
| `src/graphics/Lumyte.Graphics.Native.Passes.Tests/Integration/Models/`、`src/graphics/Lumyte.Graphics.Portable.Passes.Tests/Integration/Models/` | 同一 consumer による PBR、変形、動的更新と描画品質の GPU 適合試験。Native は vertex／mesh-only／amplification 付き経路、未対応 GPU での vertex 経路も確認する。`Fixtures/` には Graphics が受け取る準備済み入力と期待する意味を置く。 |
| `benchmarks/Lumyte.Benchmarks/Graphics/Models/` | 既存 benchmark project に追加する 10 万 draw の変更なし／1 件／100 件更新、転送量、準備と描画の計測。Native の vertex／mesh／amplification を同じ scene で比較する。 |

旧 Library の描画実装は削除済みであり、Model 本体は本 ADR に従って新設する。glTF 等の loader／decoder と sample asset の読取りは `Lumyte.Resources`、ECS からの抽出・node／animation 評価は上位が所有し、この配置に新設しない。共通 `GpuImageUploadData` は RenderGraph 側の定義を参照し、Models に複製しない。

## 使用例

以下の初期登録と graph の Compile は一度だけ行う。`geometry` と `material` は Resources または手続き生成側が用意した独立したデータ、camera／lighting は評価済み値とする。全項目の列挙・コピーを frame loop へ入れない。

```csharp
var draws = new ModelDrawList();
var movingDraw = new ModelDrawItem(geometry, material, Matrix4x4.Identity);
var moving = draws.Add(movingDraw);
draws.Add(new ModelDrawItem(geometry, alternateMaterial,
    Matrix4x4.CreateTranslation(2, 0, 0)));

var graph = new GpuRenderGraph();
var modelInput = graph.CreateInput("model.data",
    ModelRenderInputContract.Instance);
var color = graph.CreateTexture("model.color", hdrColorDescription);
var depth = graph.CreateTexture("model.depth", depthDescription);
var model = graph.AddModelPass("model", new ModelPassRequest(modelInput, color, depth));
graph.ExportTexture(model.Color);
var plan = graph.Compile();
var bindings = plan.CreateBindings();
```

毎フレームは変更項目と今回の描画条件だけを渡す。`currentWorld`、camera、lighting は上位の今回の値であり、未変更項目の Component を再抽出する必要はない。

```csharp
movingDraw = movingDraw with { LocalToWorld = currentWorld };
draws.Set(moving, movingDraw);
bindings.Set(modelInput,
    new ModelRenderSnapshot(camera, draws.Snapshot(), lighting));

using var execution = await runtime.SubmitAsync(
    plan, bindings.Build(), cancellationToken);
```

Submit の受理後に毎回 GPU 完了を待つループにはしない。execution の Dispose は使用保持を GPU 完了前に壊さない。提出数と表示の pacing は host が制御する。画面表示では初期 graph に ToneMap／Output と presentation 用 texture input を組み込み、同じ plan を `GpuRenderContext.SubmitAsync(plan, bindings.Build(), targetInput, cancellationToken)` に渡す。acquire した今回の target だけを差し替え、resize 時に plan を作り直す。

この使用例は Native が mesh 経路を選ぶ場合も同じである。呼出し側は `AddModelPass`、geometry と index の入力を変更せず、同じ plan を使い続ける。

ECS 接続も同じ更新 API を使う。下記の component と handle の対応は上位が持ち、Graphics が Component を探索しない。

```csharp
draws.Set(renderHandle, new ModelDrawItem(
    Geometry: geometryComponent.Data,
    Material: materialComponent.Data,
    LocalToWorld: transformComponent.WorldMatrix,
    Deformation: new ModelDeformationData(
        Skin: skinComponent.EvaluatedPalette,
        Morph: morphComponent.EvaluatedWeights)));
```

頂点の一部だけを変える場合は、型付きの範囲更新で新しい完全状態を作る。WithRange は要素数を保ち、`changedPositions` は指定範囲の所有済み値、各 key は新しい Revision を持つ。

```csharp
var currentGeometry = movingDraw.Geometry;
var updatedPositions = currentGeometry.Vertices.Positions.WithRange(
    positionKey, firstVertex, changedPositions);
var updatedGeometry = currentGeometry with
{
    Key = geometryKey,
    Vertices = currentGeometry.Vertices with { Positions = updatedPositions }
};
movingDraw = movingDraw with { Geometry = updatedGeometry };
draws.Set(moving, movingDraw);
```

未変更の属性・index・material・画像は同じデータを共有する。頂点数の増減は新しい完全な属性／index を作って Set し、整合する値を上位が用意する。既に CPU で変形した最終頂点には、Deformation で同じ skin／morph を再適用しない。単発描画では `ModelDrawSnapshot.From(items)` と定数の ModelRenderSnapshot を同じ API に渡してよい。

## glTF の対応範囲と接続

glTF は汎用 API の初期適合対象とする。形式の意味は [glTF 2.0 仕様](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html) に従い、core と下記五拡張の描画結果を表現できることを必要とする。Graphics は元形式を判定せず、他形式や ECS からも同じ入力を受け付ける。

| 分類 | Resources／上位が用意する内容 | 汎用描画データへの対応 |
| --- | --- | --- |
| コンテナと依存 | glTF／GLB、外部 buffer／image、data URI、PNG／JPEG のロードと decode、世代の固定 | geometry、material と GpuImageUploadData。ファイル情報は渡さない |
| Geometry | 七 topology、indexed／non-indexed と index component の意味 | ModelGeometryData、uint の ModelIndexData、必要なら ModelDrawRange |
| Accessor | offset／stride、normalized、sparse、省略時の値と component type を解釈 | 意味値へ展開した各頂点属性。元の bytes を GPU ABI にしない |
| Scene | scene の選択、階層と matrix／TRS、描画対象と joint の評価 | 対象 primitive ごとの ModelDrawItem と最終変換。node tree は上位だけが保持する |
| Material／Texture | metallic-roughness、各 texture 用途、factor、UV set、sampling と既定値 | 独立した ModelMaterialData、ModelTextureUse と共有画像 |
| 表面と alpha | Opaque／Mask／Blend、alpha cutoff、double-sided と欠落属性 | material の意味を保つ描画。負・非一様 scale も保持する |
| Skin | 全 joint／weight set、inverse bind と joint の評価 | 全 influence と ModelSkinPaletteData。draw ごとに palette を選べる |
| Morph | target 変位と既定・評価済み weights | geometry の target 列と独立した ModelMorphWeightsData。morph 後に skin |
| Animation | TRS／weights、STEP／LINEAR／CUBICSPLINE と quaternion の評価 | 時刻ごとの transform／palette／weights。再生時刻や clip は渡さない |
| Camera／Light | camera の射影と world pose、light の world 配置 | 独立した ModelCamera と ModelLighting。mesh の所有と結び付けない |

[Accessor schema](https://raw.githubusercontent.com/KhronosGroup/glTF/main/specification/2.0/schema/accessor.schema.json)、[Mesh Primitive schema](https://raw.githubusercontent.com/KhronosGroup/glTF/main/specification/2.0/schema/mesh.primitive.schema.json)、[Animation Sampler schema](https://raw.githubusercontent.com/KhronosGroup/glTF/main/specification/2.0/schema/animation.sampler.schema.json) は上位の形式解釈の基準である。scene の既定選択、描画対象のない部品資産や position のない primitive の診断も上位で扱う。

filter 指定がない場合の既定値は拡大 linear、縮小 trilinear とし、供給側が sampler 値に確定する。必要な mip が入力に含まれなければ本体が GPU 準備で生成する。全 UV set、全 skin influence と提供された morph 属性を保持し、容量不足では失敗を返して黙って切り詰めない。source の再ロードは行わない。

| 区分 | 拡張 | 接続と描画の要件 |
| --- | --- | --- |
| 初期必須 | [KHR_materials_unlit](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_unlit) | Unlit の材質、alpha と両面性を保持する |
| 初期必須 | [KHR_texture_transform](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_texture_transform) | UV set と ModelTextureUse の変換へ反映する |
| 初期必須 | [KHR_mesh_quantization](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_mesh_quantization) | Resources が属性を展開し、上位が必要な変換を draw item へ合成する |
| 初期必須 | [KHR_lights_punctual](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_lights_punctual) | directional／point／spot を world 空間へ評価して ModelLighting に渡す |
| 初期必須 | [KHR_materials_emissive_strength](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_emissive_strength) | EmissiveStrength を HDR 出力へ反映する。Bloom は自動追加しない |
| 追加候補 | [KHR_texture_basisu](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_texture_basisu)、[KHR_draco_mesh_compression](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_draco_mesh_compression)、[EXT_meshopt_compression](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Vendor/EXT_meshopt_compression) | Resources で復号した画像・geometry は同じ型で扱う。Graphics に decoder を設けない |
| 追加候補 | [KHR_materials_variants](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_variants)、[EXT_mesh_gpu_instancing](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Vendor/EXT_mesh_gpu_instancing) | 上位で選択・展開し、material の差替えと draw item の集合へ接続する。通常の部品共有と複数配置は初期から使える |
| 追加候補 | [KHR_materials_clearcoat](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_clearcoat)、[KHR_materials_transmission](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_transmission) 等 | 対応する材質の意味、追加資源と描画を別途定義する |

extensionsRequired、任意拡張の fallback と形式の診断は Resources 側が扱う。glTF 1.x や未知拡張の形式対応は初期要件に含めない。loader と renderer の適合が揃って初めて glTF の end-to-end 対応とし、汎用描画型で表現できることだけを形式のロード対応と呼ばない。

## 二系統の内部設計

| 内部の担当 | Native | Portable |
| --- | --- | --- |
| 部品の GPU data | 統一 heap、linear range、texture と Bindless descriptor を Native Resources で管理する | Buffer／Texture を直接作成し、実際の組合せから binding を cache する |
| Shader | Native package、vertex／mesh／amplification の entry、専用 root と Parameter Data の型を所有する | Portable package、vertex／compute の entry、専用 root と binding／data の型を所有する。共通の計算 module は Slang から利用できる |
| 変形と描画 | vertex pulling／indexed、optional mesh／amplification、GPU culling、indirect、まとめ描きを選べる | 自系統に適した CPU／GPU culling、binding 別 batch、変形処理を選べる |
| 更新と配置 | 共有部品と更新頻度に応じて heap 上の配置・領域再利用を選ぶ | object／buffer range の再利用と staging を選ぶ。heap、ExplicitPlacement、Bindless emulation は要求しない |

同じ geometry／material を使う複数 draw をまとめてよいが、まとめ描きは実装上の選択であり、共通入力に native indirect command や shader pointer を要求しない。GPU 配置をファイル単位や ECS archetype に固定しない。専用 package の統合 allocation が適する場合も、部品の独立した内容と使用保持を維持する。

PBR、skin、morph の実行位置や内部 pass 数は変えられる。外部の材質意味、変形と出力は一致させる。初期の BRDF は metallic-roughness の GGX、Smith visibility、Schlick Fresnel とし、数値適合用の CPU 参照計算、安定化と環境前処理の試験値を固定する。

### Native の mesh 経路

`NativeModelPass` は [Native Graphics の capability と上限](0002-native-graphics-api.md)、準備済み package の artifact、入力 topology／range、材質と変形方式を見て、提出の準備中に vertex／mesh の経路を選ぶ。`MeshShaders` が false、適合する artifact がない、または描画条件に合わない場合は既存の vertex／indexed 経路を使う。`AmplificationShaders` は別に判定し、mesh-only と amplification 付きの両方を候補にできる。対応しているだけで速いとは仮定せず、scene の規模、更新頻度、準備費用と GPU 時間を測定して適用範囲を決める。

meshlet 分割は受け取った geometry を device に適する GPU 表現へ準備する処理であり、`NativeModelPass` が所有する。`Lumyte.Resources` に meshlet 形式の生成を必須化せず、loader、ECS Component や共通 upload API を追加しない。indexed／non-indexed、選択した draw range と topology の意味を保ち、skin influence／morph target を切り詰めない。動的 geometry でも現在の完全な入力世代から必要部分を再構築し、費用や対応条件に応じて vertex 経路を選べる。

直接／間接 mesh work は [Native command API](0011-native-command-recording-api.md) で記録する。meshlet の引数・payload と GPU 構造体は Native 本体の内部表現とし、公開 Model の index 列を mesh shader の出力 indices と同一視しない。経路を選択した後の shader／PSO／記録／提出失敗は通常の失敗として通知し、別経路で黙って再提出しない。device loss や結果不明の提出を成功へ置き換えない。

### Slang の計算共有

[機能 pass の source 共有規則](0033-feature-render-passes.md) に従い、GGX、Smith visibility、Schlick Fresnel、色変換、UV 変換、skin／morph の数学を Slang の共通 module に分ける。計算には値を渡し、material／texture の取得、頂点・palette の読取り、root、binding／descriptor、entry point、stage 間出力と mesh payload は各系統の shader module が担当する。計算用の値型を共有しても、Native と Portable の GPU packing、生成 C#、package と runtime loader を同じ ABI にはしない。

共通関数は vertex／mesh／compute のどこから呼ぶかを固定しない。Portable の WGSL target で使えない機能や entry の制約は系統別 source に閉じ込め、共有率を上げるための性能低下や root の buffer fallback を導入しない。直接 WGSL の entry を使う場合も同じ数値適合条件を満たし、共有計算と同じ結果を確認する。

点・線は viewport 上で 1 pixel 幅を基本とし、必要なら geometry へ展開する。normal と tangent があれば normal map を含む照明、normal だけなら normal map を除いた照明、normal がなければ base color と emissive を使う。点・線へ三角形の flat normal を生成しない。

normal がない三角形には morph を反映した flat normal を用意し、元の tangent とその変位は使わない。normal map 用の有効な tangent がなければ対応 UV に対する MikkTSpace を基準に生成する。負 scale、非一様 scale と裏面でも法線、接空間と面の向きを保持する。これらは pass 本体の幾何処理であり、低レベル command の暗黙補正にはしない。

## 透明描画と失敗

Opaque／Mask の後に Blend を描く。Blend は draw range が実際に参照する頂点へ今回の変形と world transform を反映し、world 軸に沿った最小包含 box の中心を camera 空間へ変換して奥行の遠い順に安定 sort する。同値の場合は ModelDrawSnapshot の明示順序を使い、slot の再使用、compaction や batching でこの順を変えない。Visible が false の項目と空の範囲は sort と描画から除く。

この方式は交差する透明面、自己交差、同じ draw 内の三角形順序を完全には解決しない。version 1 はこの制約を持つ alpha blending とし、OIT、屈折や transmission を対応済みに含めない。

mesh 経路もこの draw 順と元の primitive 順を保つ。meshlet 分割・並替え・culling の方式が Blend の順序を維持できない場合、その draw は vertex／indexed 経路を使う。Opaque／Mask 向けの並替えを透明描画へ流用しない。

各本体は全 GPU 資源と使用を内部 graph に登録し、program、部品 cache、動的入力と一時変形 buffer を completion まで保持する。Parameter Data の生成・転送は本体が明示し、shader は root から参照・算出する。command が暗黙生成する経路や root の buffer fallback は作らない。raster PSO の生成時点は下位の契約に従い、DirectX 12 は実際の Submit、Native Vulkan は pipeline 作成時に解決する。Model 専用の事前準備 API は設けない。

Graphics は DTO の属性数・範囲・palette 対応や自分の出力契約を扱う。ECS の Entity 生存判定、階層循環、Component 更新競合を再検証しない。native API／WebGPU／compiler が検証する format、usage、binding、PSO、GPU 同期の合法性を独自 validator で再実装しない。

## 適合確認

Graphics の xUnit 試験は転送型を直接作り、次を独立した振舞いとして確認する。新しい loader や ECS 実装の導入を試験の前提にしない。

- ファイルを使わず生成した geometry を描画要求にできる。
- 独立した Component 相当の値から draw item を合成し、node 配列なしで同じ geometry を別 material／transform と共有できる。
- material、position、index、palette、morph weights を個別に改版し、未変更部品の同一性を保持できる。
- 連続する snapshot で頂点数・範囲・可視性を変えても、旧 snapshot の値と CPU memory が変わらない。
- morph、全 skin influence、最終変換と負・非一様 scale が対応し、skin に mesh transform を二重適用しない。
- 内容世代ごとの使用保持と回収、透明順序、動的 bounds が正しく扱われる。
- 変更なしの Snapshot が既存 root と保持情報を共有し、一項目の Set が未変更の全項目をコピー・再走査しない。
- 同じ plan を input bindings だけ差し替えて提出でき、追加・削除・順序変更と古い snapshot の再提出でも結果が正しい。
- handle の削除後再使用、snapshot の世代飛び越し、複数 camera／runtime の使用、途中失敗でも別項目や別世代へ差分を誤適用しない。
- meshlet 接続情報と動的 bounds の cache を分け、position／index／range／palette／morph の変更を必要な派生世代だけへ反映できる。
- mesh 未対応、artifact 不足、順序を維持できない Blend が vertex 経路を選び、選択後の失敗を別経路で黙って再提出しない。

hardware が必要な GPU suite では、一度ビルドした同じ consumer assembly と同じ入力を両 provider に接続する。提出が重なる動的更新、部品の cache 再利用、旧世代の回収と再転送を確認し、command 列や allocation の一致を要求しない。

Native は同じ入力を vertex、mesh-only、amplification 付きの各対応経路で描き、PBR、skin／morph、indexed／non-indexed、部分範囲、動的更新と Blend の出力を許容誤差内で比較する。mesh 未対応 GPU でも同じ consumer が動作することを確認する。Slang の共有関数には CPU の参照値と各 target の実行結果を用い、source や命令列の一致を適合条件にはしない。

glTF の確認には固定版の [Khronos Sample Assets](https://github.com/KhronosGroup/glTF-Sample-Assets)、[glTF Validator](https://github.com/KhronosGroup/glTF-Validator)、[glTF Sample Renderer](https://github.com/KhronosGroup/glTF-Sample-Renderer) を利用する。Resources 側の decode と上位の評価、Graphics 側の描画を個別に試験し、別途 end-to-end で core と採用拡張を確認する。全画面の大きな snapshot を主な判定にせず、法線・色・alpha・遮蔽等の振舞いと許容誤差を fixture ごとに定める。

## 効率の確認

10 万 draw のうち毎フレーム 1 件または 100 件の transform を変更する fixture で、初期構築を除いた CPU 抽出数、コピー量、upload 保持の再列挙数、GPU 転送 bytes、batch 再構築数を別々に計測する。変更していない geometry／画像の再転送と毎回の共通 Compile は不要であることを検証する。page／alignment や内部構造に応じた一定範囲の更新を認め、命令数の完全一致を求めない。

同じ静的／動的 scene で Native の vertex／mesh-only／amplification 付き経路を比較し、meshlet 初回構築と更新の CPU 費用、upload bytes、追加 memory、culling と描画の GPU 時間を分けて記録する。初回準備を隠さず、再利用される frame 数も含めて経路選択を評価する。mesh 対応や Slang の共通化そのものを性能改善の証拠にはしない。

camera による可視判定、透明 sort、動的な bounds、実際の command 記録・GPU 描画は必要に応じて実行する。全処理が変更件数だけになるとは保証しない。60 FPS は hardware、解像度と scene に依存する測定目標であり、この設計段階での達成結果ではない。既存ライブラリの方式と比較の根拠は [描画の再利用と部分更新の調査](../designs/rendering-reuse-and-updates.md) にまとめる。

## 採用範囲と未実装事項

形式に依存しない描画単位、保持型の ModelDrawList と共有 snapshot、部品別の不変転送データと範囲更新、ECS／Component からの直接供給、同じ plan へのフレーム入力の差替え、CPU が生成・更新する動的 geometry、評価済み skin／morph、PBR／Unlit と HDR／Depth の出力を採用する。glTF core と五拡張を描画できることは維持し、API 自体を glTF の scene・node・model の構造へ固定しない。

Native の optional mesh／amplification 経路、同じ Model 入力からの meshlet 準備と派生 cache、Slang の計算 module の共有も採用する。共通 API に meshlet、shader と GPU ABI を露出せず、Portable の mesh エミュレーションは要求しない。

本 ADR の型、保持集合・部分木共有・型付き入力の保持契約、AddModelPass、両本体の packing・差分転送・draw cache、shader、PBR、skin／morph、環境前処理、動的 bounds、透明 sort、meshlet 構築・mesh／amplification の選択、Slang 共通 module と適合・性能試験は未実装である。共通 GpuFormat.Rgba16Float と下位の対応、BRDF の定数と fixture の許容誤差も必要になる。

ファイル形式ごとの importer／decoder と依存解決は Lumyte.Resources、実行時の node／ECS／Component 管理、animation 混合・IK・simulation と描画データ抽出は上位側の実装事項である。この ADR はそれらの API を定義しない。実装進捗は、汎用描画の能力と各形式の end-to-end 対応を分け、未達成項目を部分実装として明示する。

任意の先行 GPU pass が生成した頂点・index を直接受け取る共通入口は初期契約に含めない。これには producer／consumer 間の属性意味・logical buffer 形式・依存の契約を別途定義する必要がある。本体内部の GPU skin／morph は選べるが、それだけで外部 GPU 生成 geometry に対応済みとは扱わない。完全状態を持たない delta だけの提出 API、追加の材質・変形方式、圧縮形式の decoder、影と OIT は未採用とする。
