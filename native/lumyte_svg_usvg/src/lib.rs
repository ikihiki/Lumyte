//! Strict static SVG adapter. XML/CSS/geometry interpretation belongs to usvg.
use std::ffi::c_void;
use std::panic::{catch_unwind, AssertUnwindSafe};
use usvg::{tiny_skia_path::PathSegment, Node, Paint};

const MAX_INPUT: usize = 4 * 1024 * 1024;
const MAX_WORDS: usize = 4 * 1024 * 1024;

struct Document {
    words: Vec<f32>,
    error: Option<String>,
}

fn convert(input: &str) -> Result<Vec<f32>, String> {
    let xml = usvg::roxmltree::Document::parse(input).map_err(|e| format!("XML: {e}"))?;
    // Text is disabled at compile time; reject it before upstream can remove it.
    // The other elements here are upstream-ignored active/foreign content.
    for n in xml.descendants().filter(|n| n.is_element()) {
        let name = n.tag_name().name();
        if n.attribute("vector-effect").is_some_and(|v| v != "none") {
            return Err("Unsupported vector-effect".into());
        }
        if matches!(
            name,
            "text"
                | "image"
                | "foreignObject"
                | "script"
                | "animate"
                | "animateMotion"
                | "animateTransform"
                | "set"
        ) {
            return Err(format!("Unsupported source element: {name}"));
        }
    }
    let mut options = usvg::Options::default();
    options.image_href_resolver = usvg::ImageHrefResolver {
        resolve_data: Box::new(|_, _, _| None),
        resolve_string: Box::new(|_, _| None),
    };
    let tree = usvg::Tree::from_xmltree(&xml, &options).map_err(|e| format!("SVG: {e}"))?;
    let mut words = vec![tree.size().width(), tree.size().height()];
    group(tree.root(), &mut words, 0)?;
    Ok(words)
}

fn group(g: &usvg::Group, out: &mut Vec<f32>, depth: usize) -> Result<(), String> {
    if depth > 128 {
        return Err("SVG nesting exceeds 128".into());
    }
    if g.opacity().get() != 1.0 {
        return Err("Unsupported group opacity".into());
    }
    if g.mask().is_some() {
        return Err("Unsupported mask".into());
    }
    if !g.filters().is_empty() {
        return Err("Unsupported filter".into());
    }
    if g.blend_mode() != usvg::BlendMode::Normal || g.isolate() {
        return Err("Unsupported blend/isolation".into());
    }
    if g.clip_path().is_some() {
        return Err("Unsupported clip path".into());
    }
    for n in g.children() {
        match n {
            Node::Group(g) => group(g, out, depth + 1)?,
            Node::Path(p) => {
                if p.rendering_mode() != usvg::ShapeRendering::GeometricPrecision {
                    return Err("Unsupported shape-rendering mode".into());
                }
                if !p.is_visible() {
                    continue;
                }
                let stroke_first = p.paint_order() == usvg::PaintOrder::StrokeAndFill;
                if stroke_first {
                    stroke(p, out)?;
                }
                if let Some(f) = p.fill() {
                    path(
                        p,
                        f.paint(),
                        f.opacity().get(),
                        if f.rule() == usvg::FillRule::EvenOdd {
                            1.0
                        } else {
                            0.0
                        },
                        None,
                        out,
                    )?;
                }
                if !stroke_first {
                    stroke(p, out)?;
                }
            }
            Node::Text(_) => return Err("Unsupported text".into()),
            Node::Image(_) => return Err("Unsupported image".into()),
        }
        if out.len() > MAX_WORDS {
            return Err("SVG output budget exceeded".into());
        }
    }
    Ok(())
}
fn stroke(p: &usvg::Path, out: &mut Vec<f32>) -> Result<(), String> {
    if let Some(s) = p.stroke() {
        if s.dasharray().is_some() || s.dashoffset() != 0.0 {
            return Err("Unsupported stroke dash/offset".into());
        }
        if s.linejoin() == usvg::LineJoin::MiterClip {
            return Err("Unsupported miter-clip".into());
        }
        path(p, s.paint(), s.opacity().get(), 0.0, Some(s), out)?;
    }
    Ok(())
}
fn path(
    p: &usvg::Path,
    paint: &Paint,
    alpha: f32,
    rule: f32,
    stroke: Option<&usvg::Stroke>,
    out: &mut Vec<f32>,
) -> Result<(), String> {
    let Paint::Color(c) = paint else {
        return Err(
            "Unsupported gradient or vector pattern paint (no color-space approximation)".into(),
        );
    };
    let t = p.abs_transform();
    out.extend_from_slice(&[
        if stroke.is_some() { 1.0 } else { 0.0 },
        t.sx,
        t.ky,
        t.kx,
        t.sy,
        t.tx,
        t.ty,
        c.red as f32 / 255.0,
        c.green as f32 / 255.0,
        c.blue as f32 / 255.0,
        alpha,
        rule,
    ]);
    if let Some(s) = stroke {
        let cap = match s.linecap() {
            usvg::LineCap::Butt => 0.,
            usvg::LineCap::Round => 1.,
            usvg::LineCap::Square => 2.,
        };
        let join = match s.linejoin() {
            usvg::LineJoin::Miter => 0.,
            usvg::LineJoin::Round => 1.,
            usvg::LineJoin::Bevel => 2.,
            _ => unreachable!(),
        };
        out.extend_from_slice(&[s.width().get(), cap, join, s.miterlimit().get()]);
    }
    let count = out.len();
    out.push(0.0);
    let mut segments = 0;
    for s in p.data().segments() {
        match s {
            PathSegment::MoveTo(p) => out.extend_from_slice(&[0., p.x, p.y]),
            PathSegment::LineTo(p) => out.extend_from_slice(&[1., p.x, p.y]),
            PathSegment::QuadTo(c, p) => out.extend_from_slice(&[2., c.x, c.y, p.x, p.y]),
            PathSegment::CubicTo(a, b, p) => {
                out.extend_from_slice(&[3., a.x, a.y, b.x, b.y, p.x, p.y])
            }
            PathSegment::Close => out.push(4.),
        }
        segments += 1;
        if out.len() > MAX_WORDS {
            return Err("SVG output budget exceeded".into());
        }
    }
    out[count] = segments as f32;
    Ok(())
}

/// Returns an owned result, including errors. Null means invalid ABI arguments.
/// Caller owns input during the call and frees the result exactly once.
#[no_mangle]
pub unsafe extern "C" fn ls_parse(data: *const u8, len: usize) -> *mut c_void {
    if data.is_null() || len > MAX_INPUT {
        return std::ptr::null_mut();
    }
    let result = catch_unwind(AssertUnwindSafe(|| {
        let bytes = std::slice::from_raw_parts(data, len);
        let input = std::str::from_utf8(bytes).map_err(|_| "Invalid UTF-8".to_string())?;
        convert(input)
    }))
    .unwrap_or_else(|_| Err("Native SVG conversion panicked".into()));
    let doc = match result {
        Ok(words) => Document { words, error: None },
        Err(error) => Document {
            words: vec![],
            error: Some(error),
        },
    };
    Box::into_raw(Box::new(doc)).cast()
}
#[no_mangle]
pub unsafe extern "C" fn ls_words(doc: *const c_void, len: *mut usize) -> *const f32 {
    if doc.is_null() || len.is_null() {
        return std::ptr::null();
    }
    let doc = &*doc.cast::<Document>();
    *len = doc.words.len();
    doc.words.as_ptr()
}
#[no_mangle]
pub unsafe extern "C" fn ls_error(doc: *const c_void, len: *mut usize) -> *const u8 {
    if doc.is_null() || len.is_null() {
        return std::ptr::null();
    }
    let doc = &*doc.cast::<Document>();
    if let Some(error) = &doc.error {
        *len = error.len();
        error.as_ptr()
    } else {
        *len = 0;
        std::ptr::null()
    }
}
#[no_mangle]
pub unsafe extern "C" fn ls_free(doc: *mut c_void) {
    if !doc.is_null() {
        drop(Box::from_raw(doc.cast::<Document>()));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn rejects_rendering_hints_and_vector_effect() {
        for (attribute, diagnostic) in [
            ("shape-rendering='crispEdges'", "shape-rendering"),
            ("shape-rendering='optimizeSpeed'", "shape-rendering"),
            ("vector-effect='non-scaling-stroke'", "vector-effect"),
        ] {
            let xml = format!("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='8' height='8' {attribute}/></svg>");
            assert!(convert(&xml).unwrap_err().contains(diagnostic));
        }
    }
    #[test]
    fn resolves_css_use_arcs_and_viewbox() {
        let w = convert(r##"<svg xmlns="http://www.w3.org/2000/svg" width="40" height="20" viewBox="0 0 20 10"><style>.x{fill:#ff0000}</style><defs><path id="p" class="x" d="M1 1a2 2 0 0 1 4 0z"/></defs><use href="#p" x="2"/></svg>"##).unwrap();
        assert_eq!(&w[..2], &[40., 20.]);
        assert!(w.len() > 30);
    }
    #[test]
    fn rejects_unrepresentable_tree() {
        for (extra, expected) in [
            ("stroke='red' stroke-dasharray='1 2'", "dash"),
            ("opacity='.5'", "opacity"),
            ("filter='url(#f)'", "filter"),
        ] {
            let s = format!("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><defs><filter id='f'><feGaussianBlur stdDeviation='1'/></filter></defs><rect width='10' height='10' {extra}/></svg>");
            assert!(convert(&s).unwrap_err().contains(expected));
        }
    }
    #[test]
    fn rejects_images_before_resolving() {
        assert!(convert(
            "<svg xmlns='http://www.w3.org/2000/svg'><image href='file:///etc/passwd'/></svg>"
        )
        .unwrap_err()
        .contains("image"));
    }
    #[test]
    fn abi_error_and_free() {
        unsafe {
            let d = ls_parse(b"bad".as_ptr(), 3);
            let mut n = 0;
            assert!(!ls_error(d, &mut n).is_null());
            assert!(n > 0);
            ls_free(d);
            ls_free(std::ptr::null_mut());
        }
    }
}
