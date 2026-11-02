using Lumyte.Samples.Framework;

namespace Lumyte.Samples;

internal static class SampleCatalog
{
    internal static IReadOnlyList<SampleDefinition> All { get; } =
    [
        Managed("signals", "基礎: 値の購読と寿命", "Signals"),
        Managed("composition", "基礎: 宣言からオブジェクトを構築", "Composition"),
        Managed("state-machines", "基礎: ガードと型付き遷移", "StateMachines"),
        Managed("animation", "基礎: 時計と Timeline", "Animation"),
        Managed("animation-state-machine", "統合: 遷移とアニメーション完了", "Animation.StateMachine"),
        Managed("resources", "基礎: リソース依存関係と解放", "Resources"),
        Managed("execution", "基礎: 所有スレッドと型付きコマンド (GPU/Audio 不要)", "Execution"),
        Managed("input", "統合: アクションと永続設定", "Input.Advanced"),
        Managed("diagnostics", "基礎: ローカル診断と操作配送", "Diagnostics"),
        new("diagnostics-remote", "統合: 診断サーバー接続 (GAME_TOKEN が必要)", Native("http", "Diagnostics.Remote", "http"), Native("magiconion", "Diagnostics.Remote", "magiconion")),
        new("window", "基礎: OS ウィンドウの寿命", new SampleBackend("windows", Project("Platform.Windows"), [], WindowsOnly: true)),
        new("audio", "契約検証: clip / voice と再生状態", Native("openal", "Audio"), Browser("webaudio", "Audio.Browser")),
        new("audio-execution", "統合: 実デバイスの所有とコマンド配送", Native("openal", "Audio.Execution"), Browser("webaudio", "Audio.Execution.Browser")),
        new("graphics", "契約検証: デバイスと共通 GPU API", Native("vulkan", "Graphics.DeviceCaps", "vulkan"), Native("wgpu", "Graphics.DeviceCaps", "wgpu"), new("d3d12", Project("Graphics.Direct3D12"), ["hardware"], WindowsOnly: true), new("warp", Project("Graphics.Direct3D12"), ["warp"], WindowsOnly: true), Browser("webgpu", "Graphics.Browser")),
        new("graphics-window", "描画: Direct3D 12 の三角形と Present", new("d3d12", Project("Graphics.Direct3D12"), ["hardware", "window"], WindowsOnly: true), new("warp", Project("Graphics.Direct3D12"), ["warp", "window"], WindowsOnly: true)),
        new("text", "描画: 多言語シェーピングと文字描画", Browser("webgpu", "Graphics.Text.Parley.Browser")),
        new("ui", "UI: layout・入力・描画", Native("record-only", "UI", "record-only"), Native("vulkan", "UI", "vulkan"), Native("wgpu", "UI", "wgpu"), Browser("browser", "UI.Browser")),
        new("ui-popup", "UI: Hosted popup と focus", Native("none", "UI.Popup", "none"), Native("vulkan", "UI.Popup", "vulkan"), Native("wgpu", "UI.Popup", "wgpu")),
        new("ui-text-editing", "UI: Windows IME (font file が必要)", new SampleBackend("windows", Project("UI.TextEditing"), [], WindowsOnly: true)),
        Managed("diagnostics-ui-game", "UI診断: ゲームAgent (GAME_TOKEN が必要)", "Diagnostics.UI.Automation.Game"),
        Managed("diagnostics-ui-operator", "UI診断: Operator (OPERATOR_TOKEN が必要)", "Diagnostics.UI.Automation.Operator"),
        new("ui-application", "UI: アプリケーションhost", Native("headless", "UI.Application.Headless"), new SampleBackend("desktop", Project("UI.Application.Desktop"), [], WindowsOnly: true), Browser("browser", "UI.Application.Browser")),
        new("pipeline-benchmark", "測定: Vulkan pipeline cache 比較", Native("vulkan", "Graphics.DeviceCaps", "vulkan", "pipeline-benchmark")),
    ];

    private static string Project(string name) => $"samples/Lumyte.{name}.Sample";

    private static SampleBackend Native(string id, string name, params string[] arguments) => new(id, Project(name), arguments);

    private static SampleBackend Browser(string id, string name) => new(id, Project(name), [], Browser: true);

    private static SampleDefinition Managed(string id, string role, string name) => new(id, role, Native("managed", name));
}
