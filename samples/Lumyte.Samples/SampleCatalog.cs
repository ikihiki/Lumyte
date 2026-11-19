using Lumyte.Samples.Framework;

namespace Lumyte.Samples;

internal static class SampleCatalog
{
    internal static IReadOnlyList<SampleDefinition> All { get; } =
    [
        Managed("signals", "基礎: 値の購読と寿命", "Signals"),
        Managed("mathematics", "基礎: 数値型を選べる2D・3D幾何", "Mathematics"),
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
        new("maps", "地図: 固定ローカルMVTと保持型UI", Native("headless", "Maps", "headless"), Native("desktop", "Maps", "desktop"), Browser("browser", "Maps.Browser")),
        new("ui-popup", "UI: Hosted popup と focus", Native("none", "UI.Popup", "none"), Native("vulkan", "UI.Popup", "vulkan"), Native("wgpu", "UI.Popup", "wgpu")),
        new("ui-text-editing", "UI: Windows IME (font file が必要)", new SampleBackend("windows", Project("UI.TextEditing"), [], WindowsOnly: true)),
        Managed("diagnostics-ui-game", "UI診断: ゲームAgent (GAME_TOKEN が必要)", "Diagnostics.UI.Automation.Game"),
        Managed("diagnostics-ui-operator", "UI診断: Operator (OPERATOR_TOKEN が必要)", "Diagnostics.UI.Automation.Operator"),
        new("ui-application", "UI: アプリケーションhost", Native("headless", "UI.Application.Headless"), new SampleBackend("desktop", Project("UI.Application.Desktop"), [], WindowsOnly: true), Browser("browser", "UI.Application.Browser")),
        new("ui-navigation", "UI: ページ遷移と寿命", Native("headless", "UI.Navigation"), Browser("browser", "UI.Navigation.Browser")),
        new("ui-pages", "UI: DIとMapPage", Native("headless", "UI.Pages"), Browser("browser", "UI.Pages.Browser")),
        new("ui-transitions", "UI: presence とページ間animation", Native("headless", "UI.Transitions"), Browser("browser", "UI.Transitions.Browser")),
        new("ui-media", "UI: 借用mediaと進捗 (font file が必要)", Native("headless", "UI.Media")),
        new("ui-controls", "UI: 選択と切替の設定画面 (font file が必要)", Native("headless", "UI.Controls")),
        new("ui-layout", "UI: 一回計測のFlex・Wrap・Grid", Native("headless", "UI.Layout")),
        new("ui-webview", "UI: engine-free selfcheck; experimental CEF requires explicit opt-in", Native("managed", "UI.WebView", "--selfcheck"), Native("cef-experimental", "UI.WebView.Cef", "--enable-experimental-cef")),
        new("ui-webview-helper", "UI: engine-free helper IPC fixture (test-only)", Native("managed", "UI.WebView.Helper", "--selfcheck")),
        new("ui-scroll", "UI: 入れ子のwheel・touch・表示位置 (font file が必要)", Native("headless", "UI.Scroll")),
        new("ui-menus", "UI: メニュー・チェック・submenu・受動tooltip (font file が必要)", Native("headless", "UI.Menus")),
        new("ui-dialogs", "UI: 宣言的Dialog・通知FIFO・recipes (font file が必要)", Native("headless", "UI.Dialogs")),
        new("ui-tabs", "UI: 保持タブ・折り畳み・編集 (font file が必要)", Native("headless", "UI.Tabs")),
        new("ui-treeview", "UI: 遅延階層・展開・キー表示 (font file が必要)", Native("headless", "UI.TreeView")),
        new("ui-virtualization", "UI: 固定長の仮想化一覧 (font file が必要)", Native("headless", "UI.Virtualization")),
        new("ui-collections", "UI: 項目・単一選択・非編集combo (font file が必要)", Native("headless", "UI.Collections")),
        new("ui-numeric", "UI: 数値・範囲・実editor入力 (font file が必要)", Native("headless", "UI.Numeric")),
        Managed("ui-functions", "UI: 関数コンポーネントとUseSignal", "UI.Functions"),
        new("pipeline-benchmark", "測定: Vulkan pipeline cache 比較", Native("vulkan", "Graphics.DeviceCaps", "vulkan", "pipeline-benchmark")),
    ];

    private static string Project(string name) => $"samples/Lumyte.{name}.Sample";

    private static SampleBackend Native(string id, string name, params string[] arguments) => new(id, Project(name), arguments);

    private static SampleBackend Browser(string id, string name) => new(id, Project(name), [], Browser: true);

    private static SampleDefinition Managed(string id, string role, string name) => new(id, role, Native("managed", name));
}
