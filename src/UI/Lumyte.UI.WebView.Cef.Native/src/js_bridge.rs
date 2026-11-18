// Serialized JSON only. This bounded, allocation-free scan ignores brackets in strings.
// Syntax is validated separately; renderer input comes from captured JSON.stringify.
fn json_depth_allowed(json: &str) -> bool {
    let (mut depth, mut quoted, mut escaped) = (0usize, false, false);
    for byte in json.bytes() {
        if quoted {
            if escaped {
                escaped = false;
            } else if byte == b'\\' {
                escaped = true;
            } else if byte == b'"' {
                quoted = false;
            }
        } else {
            match byte {
                b'"' => quoted = true,
                b'[' | b'{' => {
                    depth += 1;
                    if depth > 64 {
                        return false;
                    }
                }
                b']' | b'}' => depth = depth.saturating_sub(1),
                _ => (),
            }
        }
    }
    true
}
use crate::{
    StateRef,
    native_input::{i64_at, u32_at},
};
use cef::*;
use std::{cell::RefCell, rc::Rc};
// Deliberately strict canonical ASCII HTTP(S) origins. Reject ambiguous authority syntax.
pub fn origin(url: &str) -> Option<String> {
    if url.len() > 4096 || !url.is_ascii() || url.bytes().any(|c| c <= 32 || c == 127 || c == b'\\')
    {
        return None;
    }
    let (scheme, tail) = if let Some(t) = url.strip_prefix("https://") {
        ("https", t)
    } else {
        ("http", url.strip_prefix("http://")?)
    };
    let authority = tail.split(['/', '?', '#']).next()?;
    if authority.is_empty() || authority.contains(['@', '*', '%']) {
        return None;
    }
    let (host, port) = if authority.starts_with('[') {
        let end = authority.find(']')?;
        let host = &authority[..=end];
        host[1..end].parse::<std::net::Ipv6Addr>().ok()?;
        let rest = &authority[end + 1..];
        (
            host,
            if rest.is_empty() {
                None
            } else {
                Some(rest.strip_prefix(':')?)
            },
        )
    } else {
        let mut p = authority.split(':');
        let host = p.next()?;
        if host.is_empty()
            || !host
                .bytes()
                .all(|c| c.is_ascii_alphanumeric() || c == b'.' || c == b'-')
        {
            return None;
        }
        let port = p.next();
        if p.next().is_some() {
            return None;
        }
        (host, port)
    };
    let port = if let Some(p) = port {
        let n = p.parse::<u16>().ok()?;
        if n == 0 || n.to_string() != p {
            return None;
        }
        if (scheme == "http" && n == 80) || (scheme == "https" && n == 443) {
            String::new()
        } else {
            format!(":{n}")
        }
    } else {
        String::new()
    };
    Some(format!("{scheme}://{}{port}", host.to_ascii_lowercase()))
}
pub fn config(bytes: &[u8]) -> Result<(String, Vec<String>, DictionaryValue), &'static str> {
    let s = std::str::from_utf8(bytes).map_err(|_| "invalid-start-utf8")?;
    let d = parse_json(Some(&CefString::from(s)), JsonParserOptions::RFC)
        .and_then(|v| v.dictionary())
        .ok_or("invalid-start-json")?;
    let url = CefString::from(&d.string(Some(&CefString::from("url")))).to_string();
    origin(&url).ok_or("invalid-http-url")?;
    let list = d
        .list(Some(&CefString::from("origins")))
        .ok_or("invalid-origins")?;
    if list.size() > 32 {
        return Err("too-many-origins");
    }
    let mut origins = vec![];
    for i in 0..list.size() {
        if list.get_type(i) != ValueType::STRING {
            return Err("invalid-origin-type");
        }
        let v = CefString::from(&list.string(i)).to_string();
        let o = origin(&v).ok_or("invalid-origin")?;
        if v != o && v != format!("{o}/") {
            return Err("noncanonical-origin");
        }
        if !origins.contains(&o) {
            origins.push(o);
        }
    }
    Ok((url, origins, d))
}
pub fn send(frame: &Frame, target: ProcessId, name: &str, fields: &[String]) {
    if let Some(mut m) = process_message_create(Some(&CefString::from(name)))
        && let Some(a) = m.argument_list()
    {
        for (i, v) in fields.iter().enumerate() {
            a.set_string(i, Some(&CefString::from(v.as_str())));
        }
        frame.send_process_message(target, Some(&mut m));
    }
}
fn fields(m: &ProcessMessage) -> Option<Vec<String>> {
    let a = m.argument_list()?;
    if a.size() > 5 {
        return None;
    }
    let mut v = vec![];
    for i in 0..a.size() {
        if a.get_type(i) != ValueType::STRING {
            return None;
        }
        let s = CefString::from(&a.string(i)).to_string();
        if s.len() > 4096 {
            return None;
        }
        v.push(s);
    }
    Some(v)
}
#[derive(Default)]
pub struct RendererState {
    origins: Vec<String>,
    epoch: i64,
    token: String,
    context: Option<V8Context>,
    evaluator: Option<V8Value>,
}
type RendererRef = Rc<RefCell<RendererState>>;
pub fn renderer() -> RenderProcessHandler {
    Renderer::new(Rc::new(RefCell::new(RendererState::default())))
}
wrap_v8_handler! {
 struct Post {origins:Vec<String>,epoch:String,serialize:V8Value}
 impl V8Handler {
 fn execute(&self,_name:Option<&CefString>,_object:Option<&mut V8Value>,arguments:Option<&[Option<V8Value>]>,_retval:Option<&mut Option<V8Value>>,exception:Option<&mut CefString>)->i32 {
  let result=(||->Option<()>{let c=v8_context_get_current_context()?;let f=c.frame()?;if f.is_main()==0{return None;}let url=CefString::from(&f.url()).to_string();if !self.origins.contains(&origin(&url)?){return None;}let args=arguments?;if args.len()!=1{return None;}let v=self.serialize.execute_function(None,Some(args))?;if v.is_string()==0{return None;}let json=CefString::from(&v.string_value()).to_string();if json.len()+url.len()+12>4096 || !json_depth_allowed(&json){return None;}send(&f,ProcessId::BROWSER,"lumyte.post",&[self.epoch.to_string(),json]);Some(())})();if result.is_none()&& let Some(e)=exception{*e=CefString::from("Message denied, unserializable, too large or JSON depth exceeds 64");}1
 }
 }
}
wrap_render_process_handler! {
 struct Renderer {state:RendererRef}
 impl RenderProcessHandler {
 fn on_browser_created(&self,_browser:Option<&mut Browser>,extra_info:Option<&mut DictionaryValue>){if let Some(d)=extra_info&& let Some(l)=d.list(Some(&CefString::from("origins"))){let mut s=self.state.borrow_mut();for i in 0..l.size().min(32){let o=CefString::from(&l.string(i)).to_string();if let Some(o)=origin(&o){s.origins.push(o);}}}}
 fn on_context_created(&self,_browser:Option<&mut Browser>,frame:Option<&mut Frame>,context:Option<&mut V8Context>){let (Some(f),Some(c))=(frame,context)else{return;};if f.is_main()==0{return;}let(epoch,origins)={let mut s=self.state.borrow_mut();s.epoch=s.epoch.saturating_add(1);s.token=format!("{}:{}:{}",std::process::id(),std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_nanos(),s.epoch);s.context=Some(c.clone());(s.token.clone(),s.origins.clone())};send(f,ProcessId::BROWSER,"lumyte.context",std::slice::from_ref(&epoch));let mut evaluator=None;let mut error=None;c.eval(Some(&CefString::from("((stringify)=>(value)=>{let s=stringify(value);return s===undefined?'null':s;})(JSON.stringify)")),None,1,Some(&mut evaluator),Some(&mut error));self.state.borrow_mut().evaluator=evaluator;if origins.is_empty(){return;}let mut value=None;let mut exception=None;
 // Capture pristine JSON.stringify in this context, before page scripts run.
 c.eval(Some(&CefString::from("((stringify)=>(value)=>{let s=stringify(value);return s===undefined?'null':s;})(JSON.stringify)")),None,1,Some(&mut value),Some(&mut exception));
 if let (Some(global),Some(serialize))=(c.global(),value)&& let Some(mut obj)=v8_value_create_object(None,None){let mut h=Post::new(origins,epoch.clone(),serialize);if let Some(mut fun)=v8_value_create_function(Some(&CefString::from("postMessage")),Some(&mut h)){obj.set_value_bykey(Some(&CefString::from("postMessage")),Some(&mut fun),V8Propertyattribute::from(sys::cef_v8_propertyattribute_t(5)));global.set_value_bykey(Some(&CefString::from("lumyte")),Some(&mut obj),V8Propertyattribute::from(sys::cef_v8_propertyattribute_t(5)));}}
 }
 fn on_context_released(&self,_browser:Option<&mut Browser>,frame:Option<&mut Frame>,context:Option<&mut V8Context>){if let (Some(f),Some(c))=(frame,context) && f.is_main()!=0 {let mut state=self.state.borrow_mut();if state.context.as_ref().is_some_and(|old|old.is_same(Some(c))!=0){let token=state.token.clone();state.evaluator=None;state.context=None;drop(state);send(f,ProcessId::BROWSER,"lumyte.released",&[token]);}}}
 fn on_process_message_received(&self,_browser:Option<&mut Browser>,frame:Option<&mut Frame>,source_process:ProcessId,message:Option<&mut ProcessMessage>)->i32 {
 if source_process!=ProcessId::BROWSER{return 0;}let(Some(f),Some(m))=(frame,message)else{return 0;};if f.is_main()==0{return 0;}let name=CefString::from(&m.name()).to_string();let Some(v)=fields(m)else{return 1;};if name=="lumyte.bind" {if v.len()==2 && v[0]==self.state.borrow().token && f.v8_context().is_some_and(|mut current|self.state.borrow().context.as_ref().is_some_and(|expected|expected.is_same(Some(&mut current))!=0)) {send(f,ProcessId::BROWSER,"lumyte.bound",&v);}return 1;}
if name!="lumyte.eval"||v.len()!=3{return 0;}let epoch=self.state.borrow().token.clone();if v[1]!=epoch{return 1;}let Some(c)=f.v8_context()else{return 1;};if c.enter()==0{return 1;}let evaluator=self.state.borrow().evaluator.clone();let code=format!("(\n{}\n)",v[2]);let mut raw=None;let mut error=None;let ok=c.eval(Some(&CefString::from(code.as_str())),None,1,Some(&mut raw),Some(&mut error));let result=if ok!=0 {match(evaluator.as_ref(),raw){(Some(e),Some(a))=>e.execute_function(None,Some(&[Some(a)])),_=>None}}else{None};let(status,text)=if let Some(v)=result{(0,CefString::from(&v.string_value()).to_string())}else{let ex=error.or_else(||evaluator.and_then(|e|e.exception()));(1,ex.map(|e|CefString::from(&e.message()).to_string()).unwrap_or_else(||"evaluation failed".into()))};c.exit();let(status,text)=if text.len()>3072{(1,"result-too-large".into())}else if status==0 && !json_depth_allowed(&text){(4,"result-depth-limit".into())}else{(status,text)};send(f,ProcessId::BROWSER,"lumyte.result",&[v[0].clone(),epoch.to_string(),status.to_string(),text]);1
 }
 }
}
#[derive(Default)]
pub struct ContextGate {
    pub active: String,
    candidate: Option<(String, i64)>,
    retired: std::collections::VecDeque<String>,
}
impl ContextGate {
    pub fn invalidate(&mut self) {
        if !self.active.is_empty() {
            self.retired.push_back(std::mem::take(&mut self.active));
        }
        if let Some((token, _)) = self.candidate.take() {
            self.retired.push_back(token);
        }
        while self.retired.len() > 16 {
            self.retired.pop_front();
        }
    }
    fn begin(&mut self, token: &str, epoch: i64, current_main: bool) -> bool {
        if !current_main
            || !self.active.is_empty()
            || token.is_empty()
            || token.len() > 128
            || token == self.active
            || self.retired.iter().any(|old| old == token)
        {
            return false;
        }
        self.candidate = Some((token.into(), epoch));
        true
    }
    fn bind(&mut self, token: &str, epoch: i64) -> bool {
        if !self
            .candidate
            .as_ref()
            .is_some_and(|(t, e)| t == token && *e == epoch)
        {
            return false;
        }
        self.active = token.into();
        self.candidate = None;
        true
    }
    fn matches(&self, token: &str) -> bool {
        !self.active.is_empty() && self.active == token
    }
    fn is_candidate(&self, token: &str) -> bool {
        self.candidate.as_ref().is_some_and(|(t, _)| t == token)
    }
}
fn emit_post(s: &mut crate::Session, url: &str, json: &str) {
    let mut body = s.epoch.to_le_bytes().to_vec();
    body.extend((url.len() as u32).to_le_bytes());
    body.extend(url.as_bytes());
    body.extend(json.as_bytes());
    s.emit(107, body);
}
pub struct Pending {
    pub id: u64,
    pub epoch: i64,
    pub deadline: std::time::Instant,
}
pub fn response(s: &mut crate::Session, id: u64, epoch: i64, status: u32, text: &str) {
    let mut b = id.to_le_bytes().to_vec();
    b.extend(epoch.to_le_bytes());
    b.extend(status.to_le_bytes());
    b.extend(text.as_bytes());
    s.emit(106, b);
}
pub fn abort(s: &mut crate::Session) {
    for p in std::mem::take(&mut s.pending) {
        response(s, p.id, p.epoch, 2, "document-or-session-ended");
    }
}
pub fn evaluate(state: &StateRef, b: &[u8]) -> Result<(), &'static str> {
    if b.len() < 21 || b.len() > 3092 {
        return Err("invalid-evaluation-size");
    }
    let id = u64::from_le_bytes(b[..8].try_into().unwrap());
    let epoch = i64_at(b, 8);
    let timeout = u32_at(b, 16);
    let script = std::str::from_utf8(&b[20..]).map_err(|_| "invalid-script-utf8")?;
    let mut s = state.borrow_mut();
    if id == 0
        || timeout == 0
        || timeout > 30000
        || s.pending.len() >= 16
        || s.in_flight.len() >= 16
        || id <= s.last_eval_id
    {
        return Err("invalid-evaluation-request");
    }
    if epoch != s.epoch || !s.context_ready {
        response(&mut s, id, epoch, 2, "stale-document");
        return Ok(());
    }
    let f = s
        .browser
        .as_ref()
        .and_then(|b| b.main_frame())
        .ok_or("no-main-frame")?;
    s.pending.push(Pending {
        id,
        epoch,
        deadline: std::time::Instant::now() + std::time::Duration::from_millis(timeout as u64),
    });
    s.last_eval_id = id;
    s.in_flight.push(id);
    let renderer_epoch = s.contexts.active.clone();
    drop(s);
    send(
        &f,
        ProcessId::RENDERER,
        "lumyte.eval",
        &[id.to_string(), renderer_epoch.to_string(), script.into()],
    );
    Ok(())
}
pub fn receive(
    state: &StateRef,
    browser: Option<&mut Browser>,
    frame: Option<&mut Frame>,
    source: ProcessId,
    message: Option<&mut ProcessMessage>,
) -> i32 {
    if source != ProcessId::RENDERER {
        return 0;
    }
    let (Some(b), Some(f), Some(m)) = (browser, frame, message) else {
        return 0;
    };
    if f.is_valid() == 0
        || f.is_main() == 0
        || !b.main_frame().is_some_and(|main| {
            CefString::from(&main.identifier()).to_string()
                == CefString::from(&f.identifier()).to_string()
        })
    {
        return 0;
    }
    let name = CefString::from(&m.name()).to_string();
    let Some(v) = fields(m) else {
        return 0;
    };
    let mut s = state.borrow_mut();
    if s.closing
        || !s
            .browser
            .as_ref()
            .is_some_and(|current| current.identifier() == b.identifier())
    {
        return 1;
    }
    match name.as_str() {
        "lumyte.context" if v.len() == 1 && !s.context_ready => {
            let epoch = s.epoch;
            if s.contexts.begin(&v[0], epoch, true) {
                s.deferred_posts.clear();
                drop(s);
                send(
                    f,
                    ProcessId::RENDERER,
                    "lumyte.bind",
                    &[v[0].clone(), epoch.to_string()],
                );
            }
            return 1;
        }
        "lumyte.bound" if v.len() == 2 => {
            let revision = v[1].parse::<i64>().unwrap_or(-1);
            if revision != s.epoch || !s.contexts.bind(&v[0], revision) {
                return 1;
            }
            abort(&mut s);
            s.epoch = s.epoch.saturating_add(1);
            s.in_flight.clear();
            s.context_ready = true;
            let epoch = s.epoch;
            s.emit(108, epoch.to_le_bytes().to_vec());
            if b.is_loading() == 0 {
                let url = CefString::from(&f.url()).to_string();
                s.emit(102, url.into_bytes());
            }
            for (url, json) in std::mem::take(&mut s.deferred_posts) {
                emit_post(&mut s, &url, &json);
            }
        }
        "lumyte.released" if v.len() == 1 && s.contexts.matches(&v[0]) => {
            s.context_ready = false;
            s.contexts.invalidate();
            s.deferred_posts.clear();
            abort(&mut s);
        }
        "lumyte.result" if v.len() == 4 => {
            let id = v[0].parse::<u64>().unwrap_or(0);

            if s.contexts.matches(&v[1]) {
                s.in_flight.retain(|old| *old != id);
                if let Some(i) = s.pending.iter().position(|p| p.id == id) {
                    let p = s.pending.remove(i);
                    let status = match v[2].as_str() {
                        "0" => 0,
                        "4" => 4,
                        _ => 1,
                    };
                    if v[3].len() <= 3072 {
                        response(&mut s, p.id, p.epoch, status, &v[3]);
                    }
                }
            }
        }
        "lumyte.post" if v.len() == 2 => {
            let url = CefString::from(&f.url()).to_string();
            if ((s.context_ready && s.contexts.matches(&v[0])) || s.contexts.is_candidate(&v[0]))
                && origin(&url).is_some_and(|o| s.origins.contains(&o))
                && url.len() + v[1].len() + 12 <= 4096
                && json_depth_allowed(&v[1])
                && parse_json(
                    Some(&CefString::from(v[1].as_str())),
                    JsonParserOptions::RFC,
                )
                .is_some()
            {
                if s.context_ready {
                    emit_post(&mut s, &url, &v[1]);
                } else if s.deferred_posts.len() < 16 {
                    s.deferred_posts.push((url, v[1].clone()));
                } else {
                    s.failed.store(true, std::sync::atomic::Ordering::Release);
                }
            }
        }
        _ => return 0,
    }
    1
}
pub fn tick(state: &StateRef) {
    let mut s = state.borrow_mut();
    let now = std::time::Instant::now();
    let mut i = 0;
    while i < s.pending.len() {
        if now >= s.pending[i].deadline {
            let p = s.pending.remove(i);
            response(&mut s, p.id, p.epoch, 3, "evaluation-timeout");
        } else {
            i += 1;
        }
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn json_depth_limit_ignores_escaped_string_brackets() {
        assert!(json_depth_allowed(&format!(
            "{}0{}",
            "[".repeat(64),
            "]".repeat(64)
        )));
        assert!(!json_depth_allowed(&format!(
            "{}0{}",
            "[".repeat(65),
            "]".repeat(65)
        )));
        assert!(json_depth_allowed(r#"["\"[[[[", {"x":"}\\"}]"#));
    }
    #[test]
    fn renderer_reset_requires_current_navigation_bind_and_rejects_old_tokens() {
        let mut gate = ContextGate::default();
        assert!(gate.begin("processA:2", 10, true));
        assert!(gate.bind("processA:2", 10));
        assert!(gate.matches("processA:2"));
        assert!(!gate.begin("processB:1", 11, true)); // no new navigation/context release yet
        gate.invalidate(); // authoritative current main-frame navigation
        assert!(!gate.begin("processA:2", 11, true));
        assert!(!gate.begin("processB:1", 11, false)); // subframe/non-current sender denied
        assert!(gate.begin("processB:1", 11, true)); // counter resets across renderer process
        assert!(!gate.bind("processA:2", 11));
        assert!(!gate.bind("processB:1", 10)); // old browser navigation challenge
        assert!(gate.bind("processB:1", 11));
        assert!(!gate.matches("processA:2")); // old result/post/released are ignored
        assert!(gate.matches("processB:1"));
        gate.invalidate();
        assert!(!gate.bind("processB:1", 11)); // navigation invalidates outstanding bind
    }
    #[test]
    fn origins_strict() {
        assert_eq!(
            origin("https://example.com:443/a?q=1").unwrap(),
            "https://example.com"
        );
        for s in [
            "file:///a",
            "https://a@b/",
            "https://*.example/",
            "http://a:0/",
            "http://a:00080/",
        ] {
            assert!(origin(s).is_none(), "{s}");
        }
        assert_eq!(origin("http://[::1]:8080/").unwrap(), "http://[::1]:8080");
    }
}
