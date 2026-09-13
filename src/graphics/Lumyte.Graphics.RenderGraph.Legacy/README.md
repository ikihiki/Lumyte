# 旧 RenderGraph の実装移植元

旧 low-level RenderGraph は `Lumyte.Graphics.RenderGraph.Legacy` assembly と namespace に分離した。
既存の Library、TwoD、Text、sample と benchmark はこの assembly を明示的に参照する。
新しい `Lumyte.Graphics.RenderGraph` の機能契約を旧 API に転送する互換層は設けない。

既存の描画能力を確認するテストは維持し、各機能を Native／Portable の pass 実装へ移す際の基準とする。
新しい利用側 API は [RenderGraph ADR](../../../docs/adr/0030-render-graph-api.md) に従う。
