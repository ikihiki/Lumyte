//! Experimental CEF selection bridge. Not connected to the product adapter until acceptance.
use cef::*;
use std::{
    cell::RefCell,
    panic::{AssertUnwindSafe, catch_unwind},
    rc::Rc as Shared,
    sync::atomic::{AtomicBool, Ordering},
};

const ABI: u32 = 1;
const OK: i32 = 0;
const INVALID: i32 = 1;
const BUSY: i32 = 2;
const PANIC: i32 = 3;
const WRONG_THREAD: i32 = 4;
static USED: AtomicBool = AtomicBool::new(false);

/// Callback data is borrowed only during the call. Copy before returning. Never unwind.
#[repr(C)]
#[derive(Clone, Copy)]
pub struct Callbacks {
    pub size: u32,
    pub version: u32,
    pub context: usize,
    pub event: Option<extern "C" fn(usize, u32, *const u8, usize, i32, i32)>,
}
#[derive(Default)]
struct State {
    browser: Option<Browser>,
    closing: bool,
    closed: bool,
    creating: bool,
}
struct Runtime {
    app: App,
    callbacks: Callbacks,
    state: Shared<RefCell<State>>,
    in_call: bool,
}
thread_local! { static RUNTIME: RefCell<Option<Runtime>> = const { RefCell::new(None) }; }
fn guard(f: impl FnOnce() -> i32) -> i32 {
    catch_unwind(AssertUnwindSafe(f)).unwrap_or(PANIC)
}
fn notify(cb: Callbacks, kind: u32, data: &[u8], width: i32, height: i32) {
    if let Some(call) = cb.event {
        call(cb.context, kind, data.as_ptr(), data.len(), width, height);
    }
}
wrap_app! {
    struct BridgeApp { cb: Callbacks }
    impl App {
        fn browser_process_handler(&self) -> Option<BrowserProcessHandler> { Some(PumpHandler::new(self.cb)) }
    }
}
wrap_browser_process_handler! {
    struct PumpHandler { cb: Callbacks }
    impl BrowserProcessHandler {
        fn on_schedule_message_pump_work(&self, delay_ms: i64) {
            notify(self.cb, 1, &delay_ms.to_le_bytes(), 0, 0);
        }
    }
}
wrap_life_span_handler! {
    struct Life { state: Shared<RefCell<State>>, cb: Callbacks }
    impl LifeSpanHandler {
        fn on_after_created(&self, browser: Option<&mut Browser>) {
            let _ = catch_unwind(AssertUnwindSafe(|| {
                let closing = self.state.borrow().closing;
                self.state.borrow_mut().browser = browser.as_deref().cloned();
                if let Some(host) = browser.and_then(|b| b.host()) {
                    host.set_audio_muted(1);
                    if closing { host.close_browser(1); }
                }
                notify(self.cb, 2, &[], 0, 0);
            }));
        }
        fn on_before_close(&self, _browser: Option<&mut Browser>) {
            let _ = catch_unwind(AssertUnwindSafe(|| {
                let mut state = self.state.borrow_mut();
                state.browser = None;
                state.closed = true;
                drop(state);
                notify(self.cb, 3, &[], 0, 0);
            }));
        }
        fn on_before_popup(&self, _browser: Option<&mut Browser>, _frame: Option<&mut Frame>, _popup_id: i32, _target_url: Option<&CefString>, _target_frame_name: Option<&CefString>, _target_disposition: WindowOpenDisposition, _user_gesture: i32, _popup_features: Option<&PopupFeatures>, _window_info: Option<&mut WindowInfo>, _client: Option<&mut Option<Client>>, _settings: Option<&mut BrowserSettings>, _extra_info: Option<&mut Option<DictionaryValue>>, _no_javascript_access: Option<&mut i32>) -> i32 { 1 }
    }
}
wrap_render_handler! {
    struct Render { state: Shared<RefCell<State>>, cb: Callbacks }
    impl RenderHandler {
        fn view_rect(&self, _browser: Option<&mut Browser>, rect: Option<&mut Rect>) {
            if let Some(rect) = rect { *rect = Rect { x: 0, y: 0, width: 64, height: 64 }; }
        }
        fn on_paint(&self, _browser: Option<&mut Browser>, type_: PaintElementType, _dirty_rects: Option<&[Rect]>, buffer: *const u8, width: i32, height: i32) {
            let _ = catch_unwind(AssertUnwindSafe(|| {
                if buffer.is_null() || width != 64 || height != 64 || type_ != PaintElementType::VIEW { return; }
                if self.state.borrow().closing { return; }

                // CEF owns these top-down, tightly packed premultiplied BGRA bytes.
                let bytes = unsafe { std::slice::from_raw_parts(buffer, 64 * 64 * 4) };
                notify(self.cb, 4, bytes, width, height);
            }));
        }
    }
}
wrap_client! {
    struct BridgeClient { life: LifeSpanHandler, render: RenderHandler }
    impl Client {
        fn life_span_handler(&self) -> Option<LifeSpanHandler> { Some(self.life.clone()) }
        fn render_handler(&self) -> Option<RenderHandler> { Some(self.render.clone()) }
    }
}
#[unsafe(no_mangle)]
pub extern "C" fn lumyte_cef_abi_version() -> u32 {
    ABI
}

/// No engine initialization. Exercises real native callback/PInvoke on the calling thread.
/// # Safety
/// `callbacks` must reference a readable Callbacks value for this call.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn lumyte_cef_contract(callbacks: *const Callbacks) -> i32 {
    guard(|| {
        if callbacks.is_null() {
            return INVALID;
        }
        let cb = unsafe { *callbacks };
        if cb.size as usize != std::mem::size_of::<Callbacks>()
            || cb.version != ABI
            || cb.event.is_none()
        {
            return INVALID;
        }
        notify(cb, 5, &[0x20, 0x40, 0x80, 0x80], 1, 1);
        OK
    })
}

/// Sandbox remains enabled. Windows requires a pre-CLR bootstrap, not implemented here.
/// # Safety
/// Callback value must be readable; callback/context must remain alive through shutdown.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn lumyte_cef_initialize(callbacks: *const Callbacks) -> i32 {
    guard(|| {
        if cfg!(not(target_os = "linux")) || callbacks.is_null() {
            return INVALID;
        }
        let cb = unsafe { *callbacks };
        if cb.size as usize != std::mem::size_of::<Callbacks>()
            || cb.version != ABI
            || cb.event.is_none()
        {
            return INVALID;
        }
        if std::env::args()
            .any(|a| a.starts_with("--no-sandbox") || a.starts_with("--remote-debugging"))
        {
            return INVALID;
        }
        if USED.swap(true, Ordering::SeqCst) {
            return BUSY;
        }
        let _ = api_hash(sys::CEF_API_VERSION_LAST, 0);
        let args = cef::args::Args::new();
        let mut app = BridgeApp::new(cb);
        let settings = Settings {
            no_sandbox: 0,
            windowless_rendering_enabled: 1,
            external_message_pump: 1,
            command_line_args_disabled: 1,
            remote_debugging_port: 0,
            browser_subprocess_path: std::env::current_exe()
                .ok()
                .and_then(|p| p.to_str().map(CefString::from))
                .unwrap_or_default(),
            ..Default::default()
        };
        if initialize(
            Some(args.as_main_args()),
            Some(&settings),
            Some(&mut app),
            std::ptr::null_mut(),
        ) != 1
        {
            return INVALID;
        }
        RUNTIME.with(|slot| {
            *slot.borrow_mut() = Some(Runtime {
                app,
                callbacks: cb,
                state: Shared::new(RefCell::new(State::default())),
                in_call: false,
            })
        });
        OK
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn lumyte_cef_subprocess() -> i32 {
    guard(|| {
        if cfg!(not(target_os = "linux"))
            || std::env::args()
                .any(|a| a.starts_with("--no-sandbox") || a.starts_with("--remote-debugging"))
        {
            return -2;
        }
        let _ = api_hash(sys::CEF_API_VERSION_LAST, 0);
        let args = cef::args::Args::new();
        execute_process(Some(args.as_main_args()), None, std::ptr::null_mut())
    })
}
fn operation(f: impl FnOnce(&mut Runtime) -> i32) -> i32 {
    guard(|| {
        RUNTIME.with(|slot| {
            let Ok(mut slot) = slot.try_borrow_mut() else {
                return BUSY;
            };
            let Some(runtime) = slot.as_mut() else {
                return WRONG_THREAD;
            };
            if runtime.in_call {
                return BUSY;
            }
            runtime.in_call = true;
            let result = f(runtime);
            runtime.in_call = false;
            result
        })
    })
}
/// Creates one fixed-size local acceptance browser; not a general product session API.
/// # Safety
/// `url` must reference `length` readable UTF-8 bytes (maximum 4096).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn lumyte_cef_create(url: *const u8, length: usize) -> i32 {
    if url.is_null() || length > 4096 {
        return INVALID;
    }
    let bytes = unsafe { std::slice::from_raw_parts(url, length) };
    let Ok(url) = std::str::from_utf8(bytes) else {
        return INVALID;
    };
    if !url.starts_with("http://127.0.0.1:") {
        return INVALID;
    }
    operation(|r| {
        if r.state.borrow().creating
            || r.state.borrow().browser.is_some()
            || r.state.borrow().closing
        {
            return BUSY;
        }
        r.state.borrow_mut().creating = true;
        let mut client = BridgeClient::new(
            Life::new(r.state.clone(), r.callbacks),
            Render::new(r.state.clone(), r.callbacks),
        );
        let window = WindowInfo::default().set_as_windowless(Default::default());
        let settings = BrowserSettings {
            windowless_frame_rate: 30,
            javascript_access_clipboard: cef::State::DISABLED,
            javascript_dom_paste: cef::State::DISABLED,
            ..Default::default()
        };
        if browser_host_create_browser(
            Some(&window),
            Some(&mut client),
            Some(&CefString::from(url)),
            Some(&settings),
            None,
            None,
        ) == 1
        {
            OK
        } else {
            INVALID
        }
    })
}
#[unsafe(no_mangle)]
pub extern "C" fn lumyte_cef_pump() -> i32 {
    operation(|_| {
        do_message_loop_work();
        OK
    })
}
#[unsafe(no_mangle)]
pub extern "C" fn lumyte_cef_close() -> i32 {
    operation(|r| {
        r.state.borrow_mut().closing = true;
        let browser = r.state.borrow().browser.clone();
        if let Some(host) = browser.and_then(|b| b.host()) {
            host.close_browser(1);
        }
        OK
    })
}
#[unsafe(no_mangle)]
pub extern "C" fn lumyte_cef_shutdown() -> i32 {
    let result = operation(|r| {
        if !r.state.borrow().closed {
            return BUSY;
        }
        let _ = &r.app;
        shutdown();
        OK
    });
    if result == OK {
        RUNTIME.with(|slot| *slot.borrow_mut() = None);
    }
    result
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn abi_and_null_are_rejected() {
        assert_eq!(lumyte_cef_abi_version(), 1);
        assert_eq!(unsafe { lumyte_cef_contract(std::ptr::null()) }, INVALID);
    }
    #[test]
    fn shutdown_without_runtime_is_safe() {
        assert_eq!(lumyte_cef_shutdown(), WRONG_THREAD);
    }
    #[test]
    fn panic_is_status() {
        assert_eq!(guard(|| panic!("test")), PANIC);
    }
    #[test]
    fn size_version_are_required() {
        let cb = Callbacks {
            size: 0,
            version: 2,
            context: 0,
            event: None,
        };
        assert_eq!(unsafe { lumyte_cef_contract(&cb) }, INVALID);
    }
}
