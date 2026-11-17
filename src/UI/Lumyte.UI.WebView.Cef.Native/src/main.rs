//! Explicit process host with sandbox enabled by default; independent of the selection-probe ABI.
use cef::*;
use std::{
    cell::RefCell,
    rc::Rc as Shared,
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
        mpsc::{self, SyncSender},
    },
    time::{Duration, Instant},
};
mod helper_mode;
mod helper_protocol;
mod shared_frames;
use helper_protocol::{Packet, geometry, http_url};
struct Session {
    frames: shared_frames::SharedFrames,
    browser: Option<Browser>,
    width: i32,
    height: i32,
    generation: i64,
    sequence: i64,
    closing: bool,
    closed: bool,
    tx: SyncSender<Packet>,
    failed: Arc<AtomicBool>,
}
impl Session {
    fn emit(&mut self, kind: u32, body: Vec<u8>) {
        let Some(sequence) = self.sequence.checked_add(1) else {
            self.failed.store(true, Ordering::Release);
            return;
        };
        self.sequence = sequence;
        let packet = Packet {
            kind,
            generation: self.generation,
            sequence: self.sequence,
            width: self.width,
            height: self.height,
            body,
        };
        enqueue(&self.tx, &self.failed, packet);
    }
    fn fault(&mut self, reason: &str) {
        self.emit(104, reason.as_bytes().to_vec());
    }
}
type StateRef = Shared<RefCell<Session>>;
wrap_browser_process_handler! {
 struct ChildBootstrap { allow_unsandboxed: bool }
 impl BrowserProcessHandler {
  fn on_before_child_process_launch(&self, command_line: Option<&mut CommandLine>) {
   if self.allow_unsandboxed && let Some(c) = command_line {
    c.append_switch(Some(&CefString::from("lumyte-allow-unsandboxed")));
   }
  }
 }
}
wrap_app! {
 struct HelperApp { allow_unsandboxed: bool }
 impl App {
  fn browser_process_handler(&self)->Option<BrowserProcessHandler> {Some(ChildBootstrap::new(self.allow_unsandboxed))}
 }
}
wrap_life_span_handler! {
 struct Life { state:StateRef }
 impl LifeSpanHandler {
  fn on_after_created(&self,browser:Option<&mut Browser>) {
   let browser=browser.as_deref().cloned(); let closing=self.state.borrow().closing;
   self.state.borrow_mut().browser=browser.clone();
   if let Some(host)=browser.and_then(|b| b.host()) {host.set_audio_muted(1);if closing {host.close_browser(1);}}
   let mut s=self.state.borrow_mut(); let body=s.frames.ready_body(); s.emit(101,body);
  }
  fn on_before_close(&self,_browser:Option<&mut Browser>) {let mut s=self.state.borrow_mut();s.browser=None;s.closed=true;}
  fn on_before_popup(&self,_browser:Option<&mut Browser>,_frame:Option<&mut Frame>,_popup_id:i32,_target_url:Option<&CefString>,_target_frame_name:Option<&CefString>,_target_disposition:WindowOpenDisposition,_user_gesture:i32,_popup_features:Option<&PopupFeatures>,_window_info:Option<&mut WindowInfo>,_client:Option<&mut Option<Client>>,_settings:Option<&mut BrowserSettings>,_extra_info:Option<&mut Option<DictionaryValue>>,_no_javascript_access:Option<&mut i32>)->i32 {1}
 }
}
wrap_render_handler! {
 struct Render {state:StateRef}
 impl RenderHandler {
  fn view_rect(&self,_browser:Option<&mut Browser>,rect:Option<&mut Rect>) {let s=self.state.borrow();if let Some(r)=rect {*r=Rect{x:0,y:0,width:s.width,height:s.height};}}
  fn on_paint(&self,_browser:Option<&mut Browser>,type_:PaintElementType,_dirty_rects:Option<&[Rect]>,buffer:*const u8,width:i32,height:i32) {
   let mut s=self.state.borrow_mut();
   if s.closing || buffer.is_null() || type_!=PaintElementType::VIEW || width!=s.width || height!=s.height || !geometry(width,height) {return;}
   // CEF's buffer is borrowed for this callback. Copy only validated, tightly packed BGRA8.
   let bytes=unsafe{std::slice::from_raw_parts(buffer,width as usize*height as usize*4)};
   let generation=s.generation;
   match s.frames.publish(generation,width,height,bytes) {
    Ok(true)=>s.emit(103,vec![]),
    Ok(false)=>{},
    Err(reason)=>{s.fault(reason);s.failed.store(true,Ordering::Release);}
   }
  }
 }
}
wrap_load_handler! {
 struct Load {state:StateRef}
 impl LoadHandler {
  fn on_load_end(&self,_browser:Option<&mut Browser>,frame:Option<&mut Frame>,_http_status_code:i32) {
   if let Some(f)=frame && f.is_main()!=0 {let url=CefString::from(&f.url()).to_string();if http_url(url.as_bytes()).is_some() && !self.state.borrow().closing {self.state.borrow_mut().emit(102,url.into_bytes());}}
  }
  fn on_load_error(&self,_browser:Option<&mut Browser>,frame:Option<&mut Frame>,error_code:Errorcode,_error_text:Option<&CefString>,_failed_url:Option<&CefString>) {if error_code!=Errorcode::ABORTED && frame.is_some_and(|f| f.is_main()!=0) {self.state.borrow_mut().fault("navigation-failed");}}
 }
}
wrap_download_handler! {
 struct Downloads;
 impl DownloadHandler {
  fn can_download(&self,_browser:Option<&mut Browser>,_url:Option<&CefString>,_request_method:Option<&CefString>)->i32 {0}
  fn on_before_download(&self,_browser:Option<&mut Browser>,_download_item:Option<&mut DownloadItem>,_suggested_name:Option<&CefString>,_callback:Option<&mut BeforeDownloadCallback>)->i32 {1}
  fn on_download_updated(&self,_browser:Option<&mut Browser>,_download_item:Option<&mut DownloadItem>,callback:Option<&mut DownloadItemCallback>) {if let Some(c)=callback {c.cancel();}}
 }
}
wrap_dialog_handler! {
 struct Dialogs;
 impl DialogHandler {
  fn on_file_dialog(&self,_browser:Option<&mut Browser>,_mode:FileDialogMode,_title:Option<&CefString>,_default_file_path:Option<&CefString>,_accept_filters:Option<&mut CefStringList>,_accept_extensions:Option<&mut CefStringList>,_accept_descriptions:Option<&mut CefStringList>,callback:Option<&mut FileDialogCallback>)->i32 {if let Some(c)=callback {c.cancel();}1}
 }
}
wrap_jsdialog_handler! {
 struct JsDialogs;
 impl JsdialogHandler {
  fn on_jsdialog(&self,_browser:Option<&mut Browser>,_origin_url:Option<&CefString>,_dialog_type:JsdialogType,_message_text:Option<&CefString>,_default_prompt_text:Option<&CefString>,_callback:Option<&mut JsdialogCallback>,suppress_message:Option<&mut i32>)->i32 {if let Some(s)=suppress_message {*s=1;}0}
  fn on_before_unload_dialog(&self,_browser:Option<&mut Browser>,_message_text:Option<&CefString>,_is_reload:i32,callback:Option<&mut JsdialogCallback>)->i32 {if let Some(c)=callback {c.cont(1,None);}1}
 }
}
wrap_permission_handler! {
 struct Permissions;
 impl PermissionHandler {
  fn on_request_media_access_permission(&self,_browser:Option<&mut Browser>,_frame:Option<&mut Frame>,_requesting_origin:Option<&CefString>,_requested_permissions:u32,callback:Option<&mut MediaAccessCallback>)->i32 {if let Some(c)=callback {c.cont(0);}1}
  fn on_show_permission_prompt(&self,_browser:Option<&mut Browser>,_prompt_id:u64,_requesting_origin:Option<&CefString>,_requested_permissions:u32,callback:Option<&mut PermissionPromptCallback>)->i32 {if let Some(c)=callback {c.cont(PermissionRequestResult::DENY);}1}
 }
}
wrap_request_handler! {
 struct Requests {state:StateRef}
 impl RequestHandler {
  fn on_before_browse(&self,_browser:Option<&mut Browser>,_frame:Option<&mut Frame>,request:Option<&mut Request>,_user_gesture:i32,_is_redirect:i32)->i32 {if request.is_some_and(|r| http_url(CefString::from(&r.url()).to_string().as_bytes()).is_some()) {0}else{1}}
  fn on_render_process_terminated(&self,_browser:Option<&mut Browser>,_status:TerminationStatus,_error_code:i32,_error_string:Option<&CefString>) {self.state.borrow_mut().fault("renderer-terminated");self.state.borrow().failed.store(true,Ordering::Release);}
 }
}
wrap_client! {
 struct HelperClient {state:StateRef}
 impl Client {
  fn life_span_handler(&self)->Option<LifeSpanHandler>{Some(Life::new(self.state.clone()))}
  fn render_handler(&self)->Option<RenderHandler>{Some(Render::new(self.state.clone()))}
  fn load_handler(&self)->Option<LoadHandler>{Some(Load::new(self.state.clone()))}
  fn download_handler(&self)->Option<DownloadHandler>{Some(Downloads::new())}
  fn dialog_handler(&self)->Option<DialogHandler>{Some(Dialogs::new())}
  fn jsdialog_handler(&self)->Option<JsdialogHandler>{Some(JsDialogs::new())}
  fn permission_handler(&self)->Option<PermissionHandler>{Some(Permissions::new())}
  fn request_handler(&self)->Option<RequestHandler>{Some(Requests::new(self.state.clone()))}
 }
}
fn close(state: &StateRef) {
    state.borrow_mut().closing = true;
    let b = state.borrow().browser.clone();
    if let Some(h) = b.and_then(|b| b.host()) {
        h.close_browser(1);
    }
}
#[cfg(target_os = "linux")]
fn protocol_output() -> std::io::Result<std::fs::File> {
    use std::os::fd::FromRawFd;
    unsafe extern "C" {
        fn dup(fd: i32) -> i32;
        fn dup2(old: i32, new: i32) -> i32;
        fn fcntl(fd: i32, cmd: i32, arg: i32) -> i32;
    }
    // Preserve a CLOEXEC protocol pipe; CEF and its descendants inherit stderr as
    // fd1, so third-party diagnostics cannot corrupt binary stdout or hold it open.
    let fd = unsafe { dup(1) };
    if fd < 0 {
        return Err(std::io::Error::last_os_error());
    }
    let file = unsafe { std::fs::File::from_raw_fd(fd) };
    if unsafe { fcntl(fd, 2, 1) } < 0 || unsafe { dup2(2, 1) } < 0 {
        return Err(std::io::Error::last_os_error());
    }
    Ok(file)
}
#[cfg(not(target_os = "linux"))]
fn protocol_output() -> std::io::Result<std::fs::File> {
    Err(std::io::Error::other("Linux only"))
}
fn run() -> i32 {
    let args: Vec<String> = std::env::args().collect();
    let mode = match helper_mode::parse(&args[1..]) {
        Ok(mode) => mode,
        Err(reason) => {
            eprintln!("cef-helper-arguments: {reason}");
            return 2;
        }
    };
    let _ = api_hash(sys::CEF_API_VERSION_LAST, 0);
    let cef_args = cef::args::Args::new();
    // Children bootstrap before IPC threads. Intent is explicitly propagated,
    // never inferred from ambient environment or an automatic retry.
    if mode == helper_mode::Mode::Child {
        return execute_process(Some(cef_args.as_main_args()), None, std::ptr::null_mut());
    }
    let helper_mode::Mode::Browser { allow_unsandboxed } = mode else {
        unreachable!()
    };
    if !cfg!(target_os = "linux") {
        return 2;
    }
    eprintln!(
        "cef-helper-mode: unsafeMode={} sandbox={}",
        allow_unsandboxed, !allow_unsandboxed
    );
    let Ok(mut protocol_stdout) = protocol_output() else {
        eprintln!("cannot isolate protocol stdout");
        return 2;
    };
    let frames = match shared_frames::SharedFrames::open_current() {
        Ok(frames) => frames,
        Err(error) => {
            eprintln!("cef-helper-shared-frames: {error}");
            return 2;
        }
    };
    let (tx, out) = mpsc::sync_channel::<Packet>(4);
    let failed = Arc::new(AtomicBool::new(false));
    let writer_failed = failed.clone();
    let (finished_tx, finished_rx) = mpsc::channel();
    std::thread::spawn(move || {
        while let Ok(p) = out.recv() {
            if p.write(&mut protocol_stdout).is_err() {
                writer_failed.store(true, Ordering::Release);
                break;
            }
        }
        let _ = finished_tx.send(());
    });
    let (input_tx, input) = mpsc::sync_channel(8);
    std::thread::spawn(move || {
        let mut stdin = std::io::stdin().lock();
        loop {
            match Packet::read(&mut stdin) {
                Ok(Some(p)) => {
                    if input_tx.send(Ok(p)).is_err() {
                        break;
                    }
                }
                Ok(None) => break,
                Err(_) => {
                    let _ = input_tx.send(Err(()));
                    break;
                }
            }
        }
    });
    let state = Shared::new(RefCell::new(Session {
        frames,
        browser: None,
        width: 1,
        height: 1,
        generation: 0,
        sequence: 0,
        closing: false,
        closed: false,
        tx,
        failed: failed.clone(),
    }));
    // A per-process singleton root prevents accidental attachment to another host.
    // cache_path stays empty (incognito); this owned root is removed after shutdown.
    let profile = std::env::temp_dir().join(format!(
        "lumyte-cef-helper-{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    ));
    if std::fs::create_dir(&profile).is_err() {
        eprintln!("cannot create private CEF root");
        return 2;
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        if std::fs::set_permissions(&profile, std::fs::Permissions::from_mode(0o700)).is_err() {
            return 2;
        }
    }
    // No persistent cache, debug port or subprocess CLI override. No automatic fallback.
    let mut app = HelperApp::new(allow_unsandboxed);
    let settings = Settings {
        root_cache_path: CefString::from(profile.to_str().unwrap()),
        no_sandbox: i32::from(allow_unsandboxed),
        windowless_rendering_enabled: 1,
        external_message_pump: 1,
        command_line_args_disabled: 1,
        remote_debugging_port: 0,
        log_file: CefString::from("/dev/stderr"),
        browser_subprocess_path: CefString::from(
            std::env::current_exe().unwrap().to_str().unwrap(),
        ),
        ..Default::default()
    };
    let mut initialized = false;
    let mut started = false;
    let mut closing_at = None;
    let mut create_at = None;
    let mut exit = 0;
    loop {
        if failed.load(Ordering::Acquire) && closing_at.is_none() {
            close(&state);
            closing_at = Some(Instant::now());
            exit = 3;
        }
        if closing_at.is_none() {
            match input.try_recv() {
                Ok(Ok(p)) => {
                    let result = (|| -> Result<(), &'static str> {
                        if p.generation < 0 || p.sequence < 0 {
                            return Err("invalid-generation-or-sequence");
                        }
                        if !(1..=8).contains(&p.kind) {
                            return Err("capability-unavailable");
                        }
                        if p.kind != 1 && p.kind != 2 && !p.body.is_empty() {
                            return Err("unexpected-command-body");
                        }
                        if p.kind == 4 {
                            close(&state);
                            closing_at = Some(Instant::now());
                            return Ok(());
                        }
                        if p.kind == 1 {
                            if started {
                                return Err("already-started");
                            }
                            let url = http_url(&p.body).ok_or("invalid-http-url")?;
                            if !geometry(p.width, p.height) {
                                return Err("invalid-viewport");
                            }
                            {
                                let mut s = state.borrow_mut();
                                s.width = p.width;
                                s.height = p.height;
                                s.generation = p.generation;
                            }
                            if initialize(
                                Some(cef_args.as_main_args()),
                                Some(&settings),
                                Some(&mut app),
                                std::ptr::null_mut(),
                            ) != 1
                            {
                                return Err("cef-initialize-failed");
                            }
                            initialized = true;
                            let mut client = HelperClient::new(state.clone());
                            let window =
                                WindowInfo::default().set_as_windowless(Default::default());
                            let bs = BrowserSettings {
                                windowless_frame_rate: 30,
                                javascript_access_clipboard: cef::State::DISABLED,
                                javascript_dom_paste: cef::State::DISABLED,
                                ..Default::default()
                            };
                            if browser_host_create_browser(
                                Some(&window),
                                Some(&mut client),
                                Some(&CefString::from(url)),
                                Some(&bs),
                                None,
                                None,
                            ) != 1
                            {
                                return Err("browser-create-failed");
                            }
                            started = true;
                            create_at = Some(Instant::now());
                            return Ok(());
                        }
                        if !started {
                            return Err("not-started");
                        }
                        let b = state.borrow().browser.clone().ok_or("not-ready")?;
                        if p.kind == 3 {
                            if !geometry(p.width, p.height)
                                || p.generation <= state.borrow().generation
                            {
                                return Err("invalid-viewport-generation");
                            }
                            {
                                let mut s = state.borrow_mut();
                                s.width = p.width;
                                s.height = p.height;
                                s.generation = p.generation;
                            }
                            if let Some(h) = b.host() {
                                h.was_resized();
                                h.invalidate(PaintElementType::VIEW);
                            }
                            return Ok(());
                        }
                        match p.kind {
                            2 => {
                                let url = http_url(&p.body).ok_or("invalid-http-url")?;
                                b.main_frame()
                                    .ok_or("no-main-frame")?
                                    .load_url(Some(&CefString::from(url)));
                            }
                            5 => b.go_back(),
                            6 => b.go_forward(),
                            7 => b.reload(),
                            8 => b.stop_load(),
                            _ => return Err("capability-unavailable"),
                        };
                        Ok(())
                    })();
                    if let Err(reason) = result {
                        state.borrow_mut().fault(reason);
                        if p.kind == 1 {
                            close(&state);
                            closing_at = Some(Instant::now());
                            exit = 3;
                        }
                    }
                }
                Ok(Err(())) => {
                    state.borrow_mut().fault("malformed-protocol");
                    close(&state);
                    closing_at = Some(Instant::now());
                    exit = 2;
                }
                Err(mpsc::TryRecvError::Disconnected) => {
                    close(&state);
                    closing_at = Some(Instant::now());
                }
                Err(mpsc::TryRecvError::Empty) => {}
            }
        }
        if initialized {
            do_message_loop_work();
        }
        if let Some(t) = closing_at {
            if state.borrow().closed || !started {
                break;
            }
            if t.elapsed() > Duration::from_secs(5) {
                state.borrow_mut().fault("close-timeout");
                return 4;
            }
        } else if create_at.is_some_and(|t| t.elapsed() > Duration::from_secs(15))
            && state.borrow().browser.is_none()
        {
            state.borrow_mut().fault("create-timeout");
            close(&state);
            closing_at = Some(Instant::now());
            exit = 4;
        }
        std::thread::sleep(Duration::from_millis(5));
    }
    // Drop callback-holding objects before shutdown and before waiting for writer EOF.
    if initialized {
        shutdown();
    }
    drop(app);
    let _ = std::fs::remove_dir_all(&profile);
    state.borrow_mut().emit(105, vec![]);
    drop(state);
    // Never hold native ownership indefinitely behind a parent that stopped draining stdout.
    if finished_rx.recv_timeout(Duration::from_secs(1)).is_err() {
        return 4;
    }
    exit
}
fn main() {
    std::process::exit(run());
}

// Only frame notices are lossy. The shared Ready slot remains discoverable while
// queued control packets drain; there is deliberately no pending/coalescing flag.
fn enqueue(tx: &SyncSender<Packet>, failed: &AtomicBool, packet: Packet) {
    match tx.try_send(packet) {
        Ok(()) | Err(mpsc::TrySendError::Full(Packet { kind: 103, .. })) => {}
        Err(_) => failed.store(true, Ordering::Release),
    }
}
#[cfg(test)]
mod notification_tests {
    use super::*;
    fn packet(kind: u32) -> Packet {
        Packet {
            kind,
            generation: 1,
            sequence: 1,
            width: 1,
            height: 1,
            body: vec![],
        }
    }
    #[test]
    fn final_static_frame_survives_full_control_queue() {
        let root = shared_frames::tests::Root::new();
        root.create(|_| {});
        let frames = shared_frames::SharedFrames::open_root(&root.0).unwrap();
        let (tx, rx) = mpsc::sync_channel(4);
        let failed = Arc::new(AtomicBool::new(false));
        let mut session = Session {
            frames,
            browser: None,
            width: 1,
            height: 1,
            generation: 1,
            sequence: 0,
            closing: false,
            closed: false,
            tx,
            failed: failed.clone(),
        };
        for kind in [101, 102, 102, 102] {
            session.emit(kind, vec![]);
        }
        assert!(session.frames.publish(1, 1, 1, &[9; 4]).unwrap());
        session.emit(103, vec![]);
        assert!(!failed.load(Ordering::Acquire));
        assert_eq!(
            rx.try_iter().map(|p| p.kind).collect::<Vec<_>>(),
            [101, 102, 102, 102]
        );
        // No future paint or notice is needed: draining ANY queued valid packet
        // lets the managed rescan acquire this final persistent Ready slot.
        let bytes = std::fs::read(root.0.join("frames-v1.shm")).unwrap();
        assert_eq!(&bytes[4096..4100], &2u32.to_le_bytes());
        assert_eq!(&bytes[4104..4112], &1u64.to_le_bytes());
        assert_eq!(&bytes[4160..4164], &[9; 4]);
    }
    #[test]
    fn full_frame_notice_drops_but_controls_and_disconnect_fault() {
        let (tx, rx) = mpsc::sync_channel(4);
        let failed = AtomicBool::new(false);
        for _ in 0..4 {
            enqueue(&tx, &failed, packet(102));
        }
        enqueue(&tx, &failed, packet(103));
        assert!(!failed.load(Ordering::Acquire));
        assert_eq!(rx.try_iter().count(), 4);
        for _ in 0..4 {
            enqueue(&tx, &failed, packet(102));
        }
        enqueue(&tx, &failed, packet(104));
        assert!(failed.load(Ordering::Acquire));
        failed.store(false, Ordering::Release);
        drop(rx);
        enqueue(&tx, &failed, packet(103));
        assert!(failed.load(Ordering::Acquire));
    }
}

#[cfg(test)]
mod allocation_probe {
    use std::{
        alloc::{GlobalAlloc, Layout, System},
        cell::Cell,
    };
    thread_local! { pub static ENABLED: Cell<bool> = const {Cell::new(false)}; pub static COUNT: Cell<usize> = const {Cell::new(0)}; }
    struct Probe;
    unsafe impl GlobalAlloc for Probe {
        unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
            ENABLED.with(|enabled| {
                if enabled.get() {
                    COUNT.with(|count| count.set(count.get() + 1));
                }
            });
            unsafe { System.alloc(layout) }
        }
        unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
            unsafe { System.dealloc(ptr, layout) }
        }
        unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, size: usize) -> *mut u8 {
            ENABLED.with(|enabled| {
                if enabled.get() {
                    COUNT.with(|count| count.set(count.get() + 1));
                }
            });
            unsafe { System.realloc(ptr, layout, size) }
        }
    }
    #[global_allocator]
    static ALLOCATOR: Probe = Probe;
}
