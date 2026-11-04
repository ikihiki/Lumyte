export function create(canvas) {
    if (!(canvas instanceof HTMLCanvasElement)) throw new Error("A canvas is required.");
    const textarea = document.createElement("textarea");
    textarea.setAttribute("aria-label", "Text input");
    textarea.setAttribute("autocomplete", "off");
    textarea.setAttribute("autocapitalize", "off");
    textarea.setAttribute("spellcheck", "false");
    textarea.tabIndex = -1;
    textarea.readOnly = true;
    Object.assign(textarea.style, {position:"fixed", opacity:"0", pointerEvents:"none", width:"1px", height:"20px", padding:"0", margin:"0", border:"0", resize:"none", overflow:"hidden", zIndex:"2147483647"});
    const h = {canvas, textarea, session:"", generation:"0", sequence:0n, queue:[], active:false, composing:false, ending:false, released:false, overflow:false, start:0, data:"", caret:[0,0,1,20], listeners:[]};
    const mount = () => {
        const full = document.fullscreenElement;
        const parent = full && full.contains(canvas) ? full : document.body;
        if (textarea.parentElement !== parent) parent.appendChild(textarea);
        position(h);
    };
    const emit = (kind, event = null) => {
        if (!h.active || h.released) return;
        if (h.queue.length >= 4096 || textarea.value.length > 32768) { h.overflow = true; return; }
        const a = textarea.selectionStart, b = textarea.selectionEnd;
        const backward = textarea.selectionDirection === "backward";
        const length = h.composing ? h.data.length : 0;
        const start = h.composing ? Math.max(0, Math.max(a,b) - length) : h.start;
        h.queue.push({kind, session:h.session, generation:h.generation, sequence:(++h.sequence).toString(), value:textarea.value, anchor:backward?b:a, active:backward?a:b, composing:h.composing, start, length, code:event?.code??null, down:event?.type==="keydown", repeat:!!event?.repeat, shift:!!event?.shiftKey, control:!!event?.ctrlKey, alt:!!event?.altKey, meta:!!event?.metaKey});
    };
    const listen = (target, name, callback) => { target.addEventListener(name, callback); h.listeners.push([target,name,callback]); };
    listen(textarea,"compositionstart", () => { h.composing=true; h.start=textarea.selectionStart; h.data=textarea.value.slice(textarea.selectionStart,textarea.selectionEnd); emit("start"); });
    listen(textarea,"compositionupdate", event => { h.data=event.data??""; });
    listen(textarea,"compositionend", () => { h.composing=false; emit("end"); });
    listen(textarea,"beforeinput", event => {
        if (h.gated) { event.preventDefault(); return; }
        h.lastInputType=event.inputType;
        if (event.isComposing && event.data != null) h.data=event.data;
    });
    listen(textarea,"input", event => { if (event.isComposing && event.data != null) h.data=event.data; emit("input"); });
    listen(document,"selectionchange", () => { if (document.activeElement===textarea && !h.syncing) emit("selection"); });
    h.held = new Map();
    h.imeTail = new Set();
    listen(textarea,"keydown", event => {
        if (!h.active) return;
        if (h.blocked?.has(event.code)) { event.preventDefault(); return; }
        const ime = h.composing || event.isComposing || event.keyCode===229 || event.code==="Process";
        h.held.set(event.code, ime);
        if (ime) { h.imeTail.add(event.code); return; }
        if (event.code==="Tab" || event.code==="Enter" || event.code==="Escape") {
            emit("key",event);
            event.preventDefault();
            if (textarea.lumyteFocusBridge?.(event) === false) { h.queue.length=0; h.overflow=true; }
        }
    });
    listen(textarea,"keyup", event => {
        if (h.blocked?.delete(event.code)) { event.preventDefault(); return; }
        const ime=h.held.get(event.code) || h.imeTail.has(event.code) || event.isComposing || event.keyCode===229;
        h.held.delete(event.code); h.imeTail.delete(event.code);
        if (ime) { event.preventDefault(); return; }
        if (["Tab","Enter","Escape"].includes(event.code)) { emit("key",event); event.preventDefault(); if (textarea.lumyteFocusBridge?.(event) === false) { h.queue.length=0; h.overflow=true; } }
    });
    const lost = () => { if (h.active) { h.composing=false; h.ending=true; emit("lost"); } };
    listen(textarea,"blur", event => { if (event.relatedTarget!==canvas && event.relatedTarget!==textarea) lost(); });
    listen(document,"visibilitychange", () => { if (document.hidden) lost(); });
    listen(document,"fullscreenchange", mount);
    listen(window,"resize", () => position(h));
    listen(window,"scroll", () => position(h));
    if (window.visualViewport) { listen(window.visualViewport,"resize",()=>position(h)); listen(window.visualViewport,"scroll",()=>position(h)); }
    h.observer = new MutationObserver(() => { if (!canvas.isConnected || !textarea.isConnected) lost(); });
    h.observer.observe(document,{childList:true,subtree:true});
    mount();
    return h;
}

function position(h) {
    const rect=h.canvas.getBoundingClientRect(), [x,y,w,height]=h.caret;
    const sx=rect.width/(h.canvas.clientWidth||1), sy=rect.height/(h.canvas.clientHeight||1);
    h.textarea.style.left=`${rect.left+x*sx}px`;
    h.textarea.style.top=`${rect.top+y*sy}px`;
    h.textarea.style.width=`${Math.max(1,w*sx)}px`;
    h.textarea.style.height=`${Math.max(1,height*sy)}px`;
}

export function proxy(h) { return h.textarea; }
export function begin(h,session,generation,value,anchor,active) {
    if (h.released || !h.canvas.isConnected || !h.textarea.isConnected) return false;
    h.session=session; h.generation=generation; h.sequence=0n; h.queue.length=0; h.overflow=false; h.composing=false; h.ending=false; h.active=true; h.held.clear(); h.imeTail.clear(); h.blocked=new Set(h.textarea.lumyteBlockedKeys??[]);
    h.textarea.readOnly=!!h.gated;
    synchronize(h,value,anchor,active);
    h.textarea.focus({preventScroll:true});
    return document.activeElement===h.textarea;
}
export function synchronize(h,value,anchor,active) {
    if (h.composing || h.queue.length) return;
    h.syncing=true;
    try {
        if (h.textarea.value!==value) h.textarea.value=value;
        h.textarea.setSelectionRange(Math.min(anchor,active),Math.max(anchor,active),active<anchor?"backward":"forward");
    } finally { h.syncing=false; }
}
export function drain(h) {
    if (h.overflow) { h.queue.length=0; h.overflow=false; return JSON.stringify([{kind:"overflow",session:h.session,generation:h.generation,sequence:(++h.sequence).toString(),value:"",anchor:0,active:0,composing:false,start:0,length:0,code:null,down:false,repeat:false,shift:false,control:false,alt:false,meta:false}]); }
    return JSON.stringify(h.queue.splice(0));
}
export function pending(h) { return h.queue.length!==0 || h.overflow; }
export function composing(h) { return h.composing; }
export function requestEnd(h) { h.ending=true; if (document.activeElement===h.textarea && h.canvas.isConnected && !document.hidden) h.canvas.focus({preventScroll:true}); else h.textarea.blur(); if (!h.textarea.isConnected || !h.canvas.isConnected || document.hidden) h.composing=false; }
export function endReady(h) { return h.ending && !h.composing && !pending(h); }
export function geometry(h,x,y,w,height) { h.caret=[x,y,w,height]; position(h); }
export function quiescent(h) { return !pending(h) && !h.composing && h.held.size===0 && (h.blocked?.size??0)===0 && (h.textarea.lumyteHeldKeys?.size??0)===0; }
export function release(h) { h.textarea.lumyteConsumedKeys=new Set([...h.held.keys(),...h.imeTail,...(h.blocked??[])]); h.active=false; h.textarea.readOnly=true; h.queue.length=0; h.held.clear(); h.imeTail.clear(); }
export function destroy(h) { h.released=true; h.active=false; h.observer.disconnect(); for(const [target,name,fn] of h.listeners) target.removeEventListener(name,fn); h.listeners.length=0; h.textarea.remove(); }

export function setInputGate(handle, gated) { handle.gated = gated; handle.textarea.readOnly = gated || !handle.active; }
