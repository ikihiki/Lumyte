# Graphics の共有値

`Lumyte.Graphics` は Native、Portable、共通 RenderGraph が使う基本値のみを定義する。
backend、command、resource handle、pipeline、shader package、メモリ管理、共通の容量制限は置かない。
Native と Portable の低レベル契約は独立した assembly に置く。

| 型 | 役割 |
| --- | --- |
| GpuFormat、GpuCompareOp | format と比較操作の意味 |
| GpuColorWriteMask、GpuClearColor | Native の channel mask と単精度 color 値 |
| GpuShaderCodeFormat、GpuShaderStage | Native shader の code 表現と entry stage |
| GpuStage、GpuAccess、GpuTextureLayout | 明示同期の実行範囲、memory access、texture layout |
| GpuOrigin3D、GpuExtent3D | texel 単位の原点と範囲 |
| GpuDeviceLostException | device の継続利用ができない失敗 |

値型は GPU object を所有せず、native API が行う format／usage／pipeline の合法性検証を複製しない。
Portable の shader visibility flags、double 精度 clear、attachment、sampler と pipeline の値は
`Lumyte.Graphics.Portable` が自身の要件に従って定義する。

公開範囲と責務の正本は [Graphics ADR](../../../docs/adr/0001-graphics-api.md)、
[Native ADR](../../../docs/adr/0002-native-graphics-api.md) と
[Portable ADR](../../../docs/adr/0016-portable-api.md) とする。
旧共通 backend／resource／command と後方互換層は保持しない。
