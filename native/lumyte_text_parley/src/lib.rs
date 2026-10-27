//! C ABI for explicit-font Parley layout and immutable vector snapshots.
mod layout;
mod paint;

use parley::{FontContext, LayoutContext};
use std::{
    cell::RefCell,
    panic::{catch_unwind, AssertUnwindSafe},
};

#[repr(C)]
#[derive(Clone, Copy)]
pub struct LpFeature {
    pub tag: u32,
    pub value: u32,
}
#[repr(C)]
#[derive(Clone, Copy)]
pub struct LpVariation {
    pub tag: u32,
    pub value: f32,
}
#[repr(C)]
#[derive(Clone, Copy)]
pub struct LpOptions {
    pub font_size: f32,
    pub max_width: f32,
    pub line_height: f32,
    pub font_id: u32,
    pub alignment: u32,
    pub direction: u32,
    pub palette_index: u32,
    pub decorations: u32,
    pub fallback_font_ids: *const u32,
    pub fallback_font_count: usize,
    pub features: *const LpFeature,
    pub feature_count: usize,
    pub variations: *const LpVariation,
    pub variation_count: usize,
    pub locale: *const u8,
    pub locale_len: usize,
}
#[repr(C)]
#[derive(Clone, Copy)]
pub struct LpSpan {
    pub start: u32,
    pub end: u32,
    pub font_id: u32,
    pub brush_index: u32,
    pub font_size: f32,
    pub weight: f32,
    pub italic: u32,
    pub decorations: u32,
}
#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct LpPathSegment {
    pub kind: u32,
    pub x0: f32,
    pub y0: f32,
    pub x1: f32,
    pub y1: f32,
    pub x2: f32,
    pub y2: f32,
}
#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct LpPaintEvent {
    pub kind: u32,
    pub index: u32,
    pub count: u32,
    pub mode: u32,
    pub p0: f32,
    pub p1: f32,
    pub p2: f32,
    pub p3: f32,
    pub p4: f32,
    pub p5: f32,
}
#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct LpColorStop {
    pub offset: f32,
    pub r: f32,
    pub g: f32,
    pub b: f32,
    pub a: f32,
    pub is_foreground: u32,
}
#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct LpGlyph {
    pub glyph_id: u32,
    pub font_id: u32,
    pub cluster_start: u32,
    pub cluster_end: u32,
    pub brush_index: u32,
    pub flags: u32,
    pub x: f32,
    pub y: f32,
    pub advance: f32,
    pub font_size: f32,
    pub scale: f32,
    pub path_start: u32,
    pub path_count: u32,
    pub paint_start: u32,
    pub paint_count: u32,
}
#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct LpDecoration {
    pub brush_index: u32,
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
}
#[repr(C)]
#[derive(Clone, Copy)]
pub struct LpLayoutView {
    pub glyphs: *const LpGlyph,
    pub glyph_count: usize,
    pub paths: *const LpPathSegment,
    pub path_count: usize,
    pub paints: *const LpPaintEvent,
    pub paint_count: usize,
    pub stops: *const LpColorStop,
    pub stop_count: usize,
    pub decorations: *const LpDecoration,
    pub decoration_count: usize,
    pub width: f32,
    pub height: f32,
    pub line_count: u32,
}

pub struct LpEngine {
    font_context: FontContext,
    layout_context: LayoutContext<u32>,
    fonts: Vec<RegisteredFont>,
}
struct RegisteredFont {
    name: String,
    blob_id: u64,
    index: u32,
}
pub struct LpLayout {
    glyphs: Vec<LpGlyph>,
    paths: Vec<LpPathSegment>,
    paints: Vec<LpPaintEvent>,
    stops: Vec<LpColorStop>,
    decorations: Vec<LpDecoration>,
    width: f32,
    height: f32,
    line_count: u32,
}

thread_local! { static LAST_ERROR: RefCell<Vec<u8>> = const { RefCell::new(Vec::new()) }; }
fn boundary(action: impl FnOnce() -> Result<(), String>) -> u32 {
    match catch_unwind(AssertUnwindSafe(action)) {
        Ok(Ok(())) => 0,
        Ok(Err(error)) => {
            LAST_ERROR.with(|s| *s.borrow_mut() = error.into_bytes());
            1
        }
        Err(_) => {
            LAST_ERROR.with(|s| *s.borrow_mut() = b"The native text engine panicked.".to_vec());
            3
        }
    }
}
unsafe fn input_slice<'a, T>(data: *const T, len: usize) -> Result<&'a [T], String> {
    if len == 0 {
        return Ok(&[]);
    }
    if data.is_null() || len > (isize::MAX as usize) / std::mem::size_of::<T>().max(1) {
        return Err("Invalid input buffer.".into());
    }
    Ok(std::slice::from_raw_parts(data, len))
}

#[no_mangle]
pub extern "C" fn lp_abi_version() -> u32 {
    1
}
/// Returns borrowed UTF-8 describing the last failed operation on this thread.
///
/// # Safety
/// Non-null output pointers must be aligned and writable. Copy the result before
/// another operation changes the error on this thread; never free the result.
#[no_mangle]
pub unsafe extern "C" fn lp_last_error(data: *mut *const u8, len: *mut usize) {
    if data.is_null() || len.is_null() {
        return;
    }
    LAST_ERROR.with(|s| {
        let error = s.borrow();
        *data = error.as_ptr();
        *len = error.len();
    });
}
/// Creates an engine without discovering system fonts.
///
/// # Safety
/// A non-null output pointer must be aligned and writable. On success the caller
/// owns the returned pointer and must release it once with `lp_engine_free`.
#[no_mangle]
pub unsafe extern "C" fn lp_engine_new(output: *mut *mut LpEngine) -> u32 {
    boundary(|| {
        if output.is_null() {
            return Err("Missing engine output pointer.".into());
        }
        *output = std::ptr::null_mut();
        let collection = parley::fontique::Collection::new(parley::fontique::CollectionOptions {
            shared: false,
            system_fonts: false,
        });
        *output = Box::into_raw(Box::new(LpEngine {
            font_context: FontContext {
                collection,
                source_cache: Default::default(),
            },
            layout_context: LayoutContext::new(),
            fonts: Vec::new(),
        }));
        Ok(())
    })
}
/// Releases an engine; previously copied layout snapshots are independent.
///
/// # Safety
/// A non-null pointer must be a live result of `lp_engine_new`, exclusively owned
/// by this call, with no operation using the engine concurrently.
#[no_mangle]
pub unsafe extern "C" fn lp_engine_free(engine: *mut LpEngine) {
    if !engine.is_null() {
        drop(Box::from_raw(engine));
    }
}
/// Copies and registers caller-supplied font bytes.
///
/// # Safety
/// The engine must be live and exclusively borrowed. The data buffer must be
/// readable for `len` bytes, and a non-null output pointer must be writable.
#[no_mangle]
pub unsafe extern "C" fn lp_font_add(
    engine: *mut LpEngine,
    data: *const u8,
    len: usize,
    output: *mut u32,
) -> u32 {
    boundary(|| {
        let engine = engine.as_mut().ok_or("Missing engine.")?;
        if output.is_null() {
            return Err("Missing font output pointer.".into());
        }
        let bytes = input_slice(data, len)?;
        layout::add_font(engine, bytes, &mut *output)
    })
}
/// Borrows the UTF-8 family name of a registered font.
///
/// # Safety
/// The engine must be live without concurrent mutation. Non-null output pointers
/// must be writable. The returned name is owned by the engine; copy it before
/// releasing the engine and never free the name pointer.
#[no_mangle]
pub unsafe extern "C" fn lp_font_name(
    engine: *mut LpEngine,
    font_id: u32,
    data: *mut *const u8,
    len: *mut usize,
) -> u32 {
    boundary(|| {
        let engine = engine.as_ref().ok_or("Missing engine.")?;
        let font = engine
            .fonts
            .get(font_id as usize)
            .ok_or("Unknown font identifier.")?;
        if data.is_null() || len.is_null() {
            return Err("Missing font name output pointer.".into());
        }
        *data = font.name.as_ptr();
        *len = font.name.len();
        Ok(())
    })
}
/// Creates an immutable snapshot whose arrays are owned by Rust.
///
/// # Safety
/// The engine must be live and exclusively borrowed. All non-null input pointers,
/// including buffers referenced by options, must be readable for their declared
/// lengths. Options must be aligned and the output pointer writable. Release a
/// successful result once with `lp_layout_free`.
#[no_mangle]
pub unsafe extern "C" fn lp_layout_create(
    engine: *mut LpEngine,
    text: *const u8,
    len: usize,
    options: *const LpOptions,
    spans: *const LpSpan,
    span_count: usize,
    output: *mut *mut LpLayout,
) -> u32 {
    boundary(|| {
        let engine = engine.as_mut().ok_or("Missing engine.")?;
        let options = options.as_ref().ok_or("Missing layout options.")?;
        if output.is_null() {
            return Err("Missing layout output pointer.".into());
        }
        *output = std::ptr::null_mut();
        let text = std::str::from_utf8(input_slice(text, len)?)
            .map_err(|_| "Text must be valid UTF-8.")?;
        let spans = input_slice(spans, span_count)?;
        *output = Box::into_raw(Box::new(layout::create(engine, text, options, spans)?));
        Ok(())
    })
}
/// Borrows the snapshot arrays until the layout is released.
///
/// # Safety
/// The layout must be live, and the output pointer must be aligned and writable.
/// Do not free or mutate array pointers; copy them before `lp_layout_free`.
#[no_mangle]
pub unsafe extern "C" fn lp_layout_view(layout: *const LpLayout, output: *mut LpLayoutView) -> u32 {
    boundary(|| {
        let layout = layout.as_ref().ok_or("Missing layout.")?;
        if output.is_null() {
            return Err("Missing layout view output pointer.".into());
        }
        *output = LpLayoutView {
            glyphs: layout.glyphs.as_ptr(),
            glyph_count: layout.glyphs.len(),
            paths: layout.paths.as_ptr(),
            path_count: layout.paths.len(),
            paints: layout.paints.as_ptr(),
            paint_count: layout.paints.len(),
            stops: layout.stops.as_ptr(),
            stop_count: layout.stops.len(),
            decorations: layout.decorations.as_ptr(),
            decoration_count: layout.decorations.len(),
            width: layout.width,
            height: layout.height,
            line_count: layout.line_count,
        };
        Ok(())
    })
}
/// Releases a snapshot and all its borrowed arrays.
///
/// # Safety
/// A non-null pointer must be a live, exclusively owned result of
/// `lp_layout_create`. No borrowed view may be used after this call.
#[no_mangle]
pub unsafe extern "C" fn lp_layout_free(layout: *mut LpLayout) {
    if !layout.is_null() {
        drop(Box::from_raw(layout));
    }
}
