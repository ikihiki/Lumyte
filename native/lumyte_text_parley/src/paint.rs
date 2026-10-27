//! Records Skrifa's unhinted outline and COLR paint callbacks as flat ABI arrays.
//!
//! Coordinates stay in font units and Y-up. The managed importer applies the
//! selected glyph scale and paragraph origin once around the recorded graph.
use crate::{LpColorStop, LpLayout, LpPaintEvent, LpPathSegment};
use skrifa::{
    color::{Brush, ColorPainter, ColorStop, CompositeMode, Extend, Transform},
    instance::{LocationRef, NormalizedCoord, Size},
    outline::{DrawSettings, OutlinePen},
    raw::{
        types::{BoundingBox, Tag},
        FontRef, TableProvider,
    },
    GlyphId, MetadataProvider,
};
use std::borrow::Cow;

const MAX_PAINT_VISITS: usize = 4096;
const MAX_STOPS: usize = 4096;
const MAX_GLYPH_SEGMENTS: usize = 65_536;
const MAX_SNAPSHOT_SEGMENTS: usize = 2_000_000;
const MAX_SNAPSHOT_EVENTS: usize = 262_144;

pub fn color_location<'a>(
    font: &FontRef<'_>,
    coords: &'a [NormalizedCoord],
) -> Cow<'a, [NormalizedCoord]> {
    let has_variations = font
        .colr()
        .ok()
        .is_some_and(|colr| colr.item_variation_store().is_some());
    if has_variations && coords.iter().all(|coord| *coord == NormalizedCoord::ZERO) {
        let mut color_coords = coords.to_vec();
        color_coords.resize(font.axes().len(), NormalizedCoord::ZERO);
        // LocationRef explicitly ignores coordinates beyond the font's axes.
        // An unused nonzero coordinate prevents Skrifa's effective_coords()
        // optimization from suppressing valid constant-region COLR deltas.
        // Metadata validation ensures the variation store cannot use this axis.
        color_coords.push(NormalizedCoord::from_bits(1));
        Cow::Owned(color_coords)
    } else {
        Cow::Borrowed(coords)
    }
}

/// Returns the ordinary path range, paint range and whether this is a color glyph.
pub fn emit_glyph(
    font: &FontRef<'_>,
    glyph_id: u32,
    normalized_coords: &[NormalizedCoord],
    color_coords: &[NormalizedCoord],
    palette_index: u32,
    output: &mut LpLayout,
) -> Result<(u32, u32, u32, u32, bool), String> {
    let path_start = u32::try_from(output.paths.len()).map_err(|_| "Path buffer overflow.")?;
    let paint_start = u32::try_from(output.paints.len()).map_err(|_| "Paint buffer overflow.")?;
    let glyph = GlyphId::new(glyph_id);
    if font.table_data(Tag::new(b"COLR")).is_some() {
        // Skrifa deliberately treats an unrecognized paint as an absent color
        // glyph. Validate before its lookup so invalid COLR cannot become an
        // apparently successful monochrome fallback.
        validate_colr_work(font, glyph_id)?;
    }
    let colors = font.color_glyphs();
    if let Some(color_glyph) = colors.get(glyph) {
        let palette_id =
            u16::try_from(palette_index).map_err(|_| "Palette index is out of range.")?;
        let palettes = font.color_palettes();
        let palette = palettes
            .get(palette_id)
            .ok_or("The selected color palette is unavailable.")?;
        let palette: Vec<[f32; 4]> = palette
            .colors()
            .iter()
            .map(|c| {
                [
                    f32::from(c.red()) / 255.0,
                    f32::from(c.green()) / 255.0,
                    f32::from(c.blue()) / 255.0,
                    f32::from(c.alpha()) / 255.0,
                ]
            })
            .collect();
        let mut painter = Painter {
            font,
            coords: normalized_coords,
            output,
            palette,
            error: None,
            event_count: 0,
        };
        color_glyph
            .paint(LocationRef::new(color_coords), &mut painter)
            .map_err(|e| format!("Invalid COLR paint: {e}"))?;
        if let Some(error) = painter.error {
            return Err(error);
        }
        let count = u32::try_from(painter.output.paints.len() - paint_start as usize)
            .map_err(|_| "Paint buffer overflow.")?;
        // Paths referenced by color clips live in the same pool but are not an
        // ordinary monochrome glyph outline.
        Ok((path_start, 0, paint_start, count, true))
    } else {
        append_outline(font, glyph, normalized_coords, &mut output.paths)?;
        let path_count = u32::try_from(output.paths.len() - path_start as usize)
            .map_err(|_| "Path buffer overflow.")?;
        Ok((path_start, path_count, paint_start, 0, false))
    }
}

struct Pen<'a> {
    paths: &'a mut Vec<LpPathSegment>,
    start: usize,
    error: Option<String>,
}
impl Pen<'_> {
    fn push(&mut self, segment: LpPathSegment) {
        if self.error.is_some() {
            return;
        }
        if [
            segment.x0, segment.y0, segment.x1, segment.y1, segment.x2, segment.y2,
        ]
        .iter()
        .any(|x| !x.is_finite())
        {
            self.error = Some("A glyph outline contains non-finite coordinates.".into());
            return;
        }
        if self.paths.len() - self.start >= MAX_GLYPH_SEGMENTS
            || self.paths.len() >= MAX_SNAPSHOT_SEGMENTS
        {
            self.error = Some("Glyph outline exceeds the segment budget.".into());
            return;
        }
        self.paths.push(segment);
    }
}
impl OutlinePen for Pen<'_> {
    fn move_to(&mut self, x: f32, y: f32) {
        self.push(LpPathSegment {
            kind: 0,
            x0: x,
            y0: y,
            ..Default::default()
        });
    }
    fn line_to(&mut self, x: f32, y: f32) {
        self.push(LpPathSegment {
            kind: 1,
            x0: x,
            y0: y,
            ..Default::default()
        });
    }
    fn quad_to(&mut self, cx: f32, cy: f32, x: f32, y: f32) {
        self.push(LpPathSegment {
            kind: 2,
            x0: cx,
            y0: cy,
            x1: x,
            y1: y,
            ..Default::default()
        });
    }
    fn curve_to(&mut self, cx0: f32, cy0: f32, cx1: f32, cy1: f32, x: f32, y: f32) {
        self.push(LpPathSegment {
            kind: 3,
            x0: cx0,
            y0: cy0,
            x1: cx1,
            y1: cy1,
            x2: x,
            y2: y,
        });
    }
    fn close(&mut self) {
        self.push(LpPathSegment {
            kind: 4,
            ..Default::default()
        });
    }
}
fn append_outline(
    font: &FontRef<'_>,
    glyph: GlyphId,
    coords: &[NormalizedCoord],
    paths: &mut Vec<LpPathSegment>,
) -> Result<(), String> {
    validate_glyf_work(font, glyph.to_u32())?;
    let outlines = font.outline_glyphs();
    let Some(outline) = outlines.get(glyph) else {
        return Ok(());
    };
    let start = paths.len();
    let mut pen = Pen {
        paths,
        start,
        error: None,
    };
    outline
        .draw(
            DrawSettings::unhinted(Size::unscaled(), LocationRef::new(coords)),
            &mut pen,
        )
        .map_err(|e| format!("Invalid glyph outline: {e}"))?;
    if let Some(error) = pen.error {
        return Err(error);
    }
    Ok(())
}

struct Painter<'a, 'f> {
    font: &'a FontRef<'f>,
    coords: &'a [NormalizedCoord],
    output: &'a mut LpLayout,
    palette: Vec<[f32; 4]>,
    error: Option<String>,
    event_count: usize,
}
impl Painter<'_, '_> {
    fn fail(&mut self, message: impl Into<String>) {
        if self.error.is_none() {
            self.error = Some(message.into());
        }
    }
    fn event(&mut self, event: LpPaintEvent) {
        if self.error.is_some() {
            return;
        }
        self.event_count += 1;
        if self.event_count > MAX_PAINT_VISITS * 8
            || self.output.paints.len() >= MAX_SNAPSHOT_EVENTS
        {
            self.fail("COLR paint exceeds the event budget.");
            return;
        }
        if [event.p0, event.p1, event.p2, event.p3, event.p4, event.p5]
            .iter()
            .any(|x| !x.is_finite())
        {
            self.fail("COLR paint contains non-finite coordinates.");
            return;
        }
        self.output.paints.push(event);
    }
    fn color(&mut self, index: u16, alpha: f32) -> Option<([f32; 4], u32)> {
        if !alpha.is_finite() {
            self.fail("COLR paint contains non-finite alpha.");
            return None;
        }
        let alpha = alpha.clamp(0.0, 1.0);
        if index == 0xffff {
            return Some(([1.0, 1.0, 1.0, alpha], 1));
        }
        let Some(mut rgba) = self.palette.get(index as usize).copied() else {
            self.fail("COLR paint references an invalid palette color.");
            return None;
        };
        rgba[3] *= alpha;
        Some((rgba, 0))
    }
    fn stops(&mut self, stops: &[ColorStop], reverse: bool) -> Option<(u32, u32)> {
        if stops.len() > MAX_STOPS
            || self.output.stops.len().saturating_add(stops.len()) > MAX_SNAPSHOT_EVENTS
        {
            self.fail("COLR gradient exceeds the color-stop budget.");
            return None;
        }
        let start = self.output.stops.len() as u32;
        for i in 0..stops.len() {
            let stop = stops[if reverse { stops.len() - 1 - i } else { i }];
            if !stop.offset.is_finite() {
                self.fail("COLR gradient contains a non-finite color stop.");
                return None;
            }
            let (rgba, is_foreground) = self.color(stop.palette_index, stop.alpha)?;
            self.output.stops.push(LpColorStop {
                offset: if reverse {
                    1.0 - stop.offset
                } else {
                    stop.offset
                },
                r: rgba[0],
                g: rgba[1],
                b: rgba[2],
                a: rgba[3],
                is_foreground,
            });
        }
        Some((start, stops.len() as u32))
    }
    fn extend(&mut self, extend: Extend) -> Option<u32> {
        match extend {
            Extend::Pad => Some(0),
            Extend::Repeat => Some(1),
            Extend::Reflect => Some(2),
            _ => {
                self.fail("Invalid COLR gradient extend mode.");
                None
            }
        }
    }
}
impl ColorPainter for Painter<'_, '_> {
    fn push_transform(&mut self, t: Transform) {
        self.event(LpPaintEvent {
            kind: 0,
            p0: t.xx,
            p1: t.yx,
            p2: t.xy,
            p3: t.yy,
            p4: t.dx,
            p5: t.dy,
            ..Default::default()
        });
    }
    fn pop_transform(&mut self) {
        self.event(LpPaintEvent {
            kind: 1,
            ..Default::default()
        });
    }
    fn push_clip_glyph(&mut self, glyph: GlyphId) {
        if self.error.is_some() {
            return;
        }
        let index = self.output.paths.len() as u32;
        if let Err(error) = append_outline(self.font, glyph, self.coords, &mut self.output.paths) {
            self.fail(error);
            return;
        }
        let count = self.output.paths.len() as u32 - index;
        self.event(LpPaintEvent {
            kind: 2,
            index,
            count,
            ..Default::default()
        });
    }
    fn push_clip_box(&mut self, b: BoundingBox<f32>) {
        self.event(LpPaintEvent {
            kind: 3,
            p0: b.x_min,
            p1: b.y_min,
            p2: b.x_max,
            p3: b.y_max,
            ..Default::default()
        });
    }
    fn pop_clip(&mut self) {
        self.event(LpPaintEvent {
            kind: 4,
            ..Default::default()
        });
    }
    fn fill(&mut self, brush: Brush<'_>) {
        if self.error.is_some() {
            return;
        }
        match brush {
            Brush::Solid {
                palette_index,
                alpha,
            } => {
                if let Some((c, fg)) = self.color(palette_index, alpha) {
                    self.event(LpPaintEvent {
                        kind: 5,
                        mode: fg,
                        p0: c[0],
                        p1: c[1],
                        p2: c[2],
                        p3: c[3],
                        ..Default::default()
                    });
                }
            }
            Brush::LinearGradient {
                p0,
                p1,
                color_stops,
                extend,
            } => {
                if let (Some((index, count)), Some(mode)) =
                    (self.stops(color_stops, false), self.extend(extend))
                {
                    self.event(LpPaintEvent {
                        kind: 6,
                        index,
                        count,
                        mode,
                        p0: p0.x,
                        p1: p0.y,
                        p2: p1.x,
                        p3: p1.y,
                        p4: p0.x - (p1.y - p0.y),
                        p5: p0.y + (p1.x - p0.x),
                    });
                }
            }
            Brush::RadialGradient {
                c0,
                r0,
                c1,
                r1,
                color_stops,
                extend,
            } => {
                if let (Some((index, count)), Some(mode)) =
                    (self.stops(color_stops, false), self.extend(extend))
                {
                    self.event(LpPaintEvent {
                        kind: 7,
                        index,
                        count,
                        mode,
                        p0: c0.x,
                        p1: c0.y,
                        p2: r0,
                        p3: c1.x,
                        p4: c1.y,
                        p5: r1,
                    });
                }
            }
            Brush::SweepGradient {
                c0,
                start_angle,
                end_angle,
                color_stops,
                extend,
            } => {
                // Skrifa normalizes to increasing clockwise degrees and may
                // reverse stops. Restore increasing angles in design Y-up
                // coordinates, because the managed importer flips Y once.
                if let (Some((index, count)), Some(mode)) =
                    (self.stops(color_stops, true), self.extend(extend))
                {
                    self.event(LpPaintEvent {
                        kind: 8,
                        index,
                        count,
                        mode,
                        p0: c0.x,
                        p1: c0.y,
                        p2: (360.0 - end_angle).to_radians(),
                        p3: (360.0 - start_angle).to_radians(),
                        ..Default::default()
                    });
                }
            }
        }
    }
    fn push_layer(&mut self, mode: CompositeMode) {
        let mode = mode as u32;
        if mode > 27 {
            self.fail("Invalid COLR composite mode.");
            return;
        }
        self.event(LpPaintEvent {
            kind: 9,
            mode,
            ..Default::default()
        });
    }
    fn pop_layer(&mut self) {
        self.event(LpPaintEvent {
            kind: 10,
            ..Default::default()
        });
    }
}

// Skrifa's callback interface cannot return an error at every paint operation.
// Bound the expanded COLR traversal *before* entering it: depth alone does not
// prevent repeated shared layer references from causing exponential work.
pub fn validate_color_metadata(font: &FontRef<'_>) -> Result<(), String> {
    let Some(data) = font.table_data(Tag::new(b"COLR")) else {
        return Ok(());
    };
    let data = data.as_bytes();
    let version = be16(data, 0)?;
    if version > 1 {
        return Err("Unsupported COLR table version.".into());
    }
    let base_count = be16(data, 2)? as usize;
    let base = be32(data, 4)? as usize;
    let layers = be32(data, 8)? as usize;
    let layer_count = be16(data, 12)? as usize;
    array_bytes(data, base, base_count, 6)?;
    array_bytes(data, layers, layer_count, 4)?;
    if version == 0 {
        return Ok(());
    }
    data.get(..34).ok_or("Truncated COLR v1 header.")?;
    let base = be32(data, 14)? as usize;
    if base != 0 {
        let records = base_records(data, base)?;
        let mut previous = None;
        for record in records.as_chunks::<6>().0 {
            let glyph = u16::from_be_bytes([record[0], record[1]]);
            if previous.is_some_and(|previous| previous >= glyph) {
                return Err("COLR base glyph records must be strictly sorted.".into());
            }
            previous = Some(glyph);
            let offset = u32::from_be_bytes([record[2], record[3], record[4], record[5]]) as usize;
            let paint = base
                .checked_add(offset)
                .ok_or("Invalid COLR paint offset.")?;
            if offset == 0 || paint >= data.len() {
                return Err("Invalid COLR paint offset.".into());
            }
        }
    }
    // Resolve optional top-level offsets here, once per font in the layout.
    // Detailed paint and variation data is evaluated only for requested glyphs.
    for field in [18, 22, 26, 30] {
        let offset = be32(data, field)? as usize;
        if offset != 0 && offset >= data.len() {
            return Err("Invalid COLR metadata offset.".into());
        }
    }
    let colr = font.colr().map_err(|_| "Invalid COLR header.")?;
    if let Some(store) = colr.item_variation_store() {
        let store = store.map_err(|_| "Invalid COLR variation store.")?;
        let regions = store
            .variation_region_list()
            .map_err(|_| "Invalid COLR variation regions.")?;
        if usize::from(regions.axis_count()) != font.axes().len() {
            return Err("COLR variation axes do not match the font axes.".into());
        }
    }
    Ok(())
}

fn array_bytes(data: &[u8], offset: usize, count: usize, width: usize) -> Result<&[u8], String> {
    if count == 0 && offset == 0 {
        return Ok(&[]);
    }
    if offset == 0 {
        return Err("COLR contains a null record offset.".into());
    }
    let end = count
        .checked_mul(width)
        .and_then(|length| offset.checked_add(length))
        .ok_or("Invalid COLR record range.")?;
    data.get(offset..end)
        .ok_or("Truncated COLR record array.".into())
}

fn base_records(data: &[u8], base: usize) -> Result<&[u8], String> {
    let count = be32(data, base)? as usize;
    if count > 65_536 {
        return Err("COLR base glyph list is too large.".into());
    }
    let records = base.checked_add(4).ok_or("Invalid COLR offset.")?;
    array_bytes(data, records, count, 6)
}

fn validate_colr_work(font: &FontRef<'_>, glyph: u32) -> Result<(), String> {
    let data = font
        .table_data(Tag::new(b"COLR"))
        .ok_or("Missing COLR table.")?;
    let data = data.as_bytes();
    let version = be16(data, 0)?;
    if version == 0 {
        return Ok(());
    }
    if version != 1 {
        return Err("Unsupported COLR table version.".into());
    }
    data.get(..34).ok_or("Truncated COLR v1 header.")?;
    let base = be32(data, 14)? as usize;
    if base == 0 {
        return Ok(());
    }
    let Some(root) = base_paint(data, base, glyph)? else {
        return Ok(());
    };
    let layers = be32(data, 18)? as usize;
    let mut budget = MAX_PAINT_VISITS;
    let mut stack = Vec::new();
    validate_paint(data, base, layers, root, &mut budget, &mut stack)
}
fn base_paint(data: &[u8], base: usize, glyph: u32) -> Result<Option<usize>, String> {
    let records = base_records(data, base)?;
    let mut low = 0;
    let mut high = records.len() / 6;
    while low < high {
        let middle = low + (high - low) / 2;
        let id = u32::from(be16(records, middle * 6)?);
        if id == glyph {
            let p = base + 4 + middle * 6;
            return Ok(Some(rel(data, base, p + 2, 4)?));
        }
        if id < glyph {
            low = middle + 1;
        } else {
            high = middle;
        }
    }
    Ok(None)
}
fn validate_paint(
    data: &[u8],
    base: usize,
    layers: usize,
    p: usize,
    budget: &mut usize,
    stack: &mut Vec<usize>,
) -> Result<(), String> {
    if *budget == 0 {
        return Err("COLR paint exceeds the expanded traversal budget.".into());
    }
    *budget -= 1;
    if stack.len() >= 96 || stack.contains(&p) {
        return Err("COLR paint contains a cycle or exceeds the depth limit.".into());
    }
    stack.push(p);
    let kind = *data.get(p).ok_or("Invalid COLR paint offset.")?;
    match kind {
        1 => {
            let count = *data.get(p + 1).ok_or("Truncated COLR layers paint.")? as usize;
            let first = be32(data, p + 2)? as usize;
            if layers == 0
                || first
                    .checked_add(count)
                    .ok_or("Invalid COLR layer range.")?
                    > be32(data, layers)? as usize
            {
                return Err("Invalid COLR layer range.".into());
            }
            for i in first..first + count {
                let child = rel(data, layers, layers + 4 + i * 4, 4)?;
                validate_paint(data, base, layers, child, budget, stack)?;
            }
        }
        2 | 3 => {}
        4..=9 => {
            let line = rel(data, p, p + 1, 3)?;
            if be16(data, line + 1)? as usize > MAX_STOPS {
                return Err("COLR gradient exceeds the color-stop budget.".into());
            }
        }
        10 | 12..=31 => {
            let child = rel(data, p, p + 1, 3)?;
            validate_paint(data, base, layers, child, budget, stack)?;
        }
        11 => {
            let glyph = u32::from(be16(data, p + 1)?);
            let child = base_paint(data, base, glyph)?
                .ok_or("COLR glyph references a missing paint root.")?;
            validate_paint(data, base, layers, child, budget, stack)?;
        }
        32 => {
            let source = rel(data, p, p + 1, 3)?;
            let backdrop = rel(data, p, p + 5, 3)?;
            validate_paint(data, base, layers, source, budget, stack)?;
            validate_paint(data, base, layers, backdrop, budget, stack)?;
        }
        _ => return Err("Unknown COLR paint format.".into()),
    }
    stack.pop();
    Ok(())
}
fn rel(data: &[u8], parent: usize, field: usize, bytes: usize) -> Result<usize, String> {
    let value = if bytes == 4 {
        be32(data, field)? as usize
    } else {
        let x = data
            .get(field..field.checked_add(3).ok_or("Invalid COLR offset.")?)
            .ok_or("Truncated COLR offset.")?;
        (usize::from(x[0]) << 16) | (usize::from(x[1]) << 8) | usize::from(x[2])
    };
    if value == 0 {
        return Err("COLR contains a null paint offset.".into());
    }
    let absolute = parent.checked_add(value).ok_or("Invalid COLR offset.")?;
    if absolute >= data.len() {
        return Err("Invalid COLR paint offset.".into());
    }
    Ok(absolute)
}
fn be16(data: &[u8], p: usize) -> Result<u16, String> {
    let bytes = data
        .get(p..p.checked_add(2).ok_or("Invalid font offset.")?)
        .ok_or("Truncated font table.")?;
    Ok(u16::from_be_bytes([bytes[0], bytes[1]]))
}
fn be32(data: &[u8], p: usize) -> Result<u32, String> {
    let bytes = data
        .get(p..p.checked_add(4).ok_or("Invalid font offset.")?)
        .ok_or("Truncated font table.")?;
    Ok(u32::from_be_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]))
}

fn validate_glyf_work(font: &FontRef<'_>, glyph: u32) -> Result<(), String> {
    let Some(glyf) = font.table_data(Tag::new(b"glyf")) else {
        return Ok(());
    };
    let loca = font
        .table_data(Tag::new(b"loca"))
        .ok_or("Missing TrueType glyph locations.")?;
    let head = font
        .head()
        .map_err(|e| format!("Invalid font header: {e}"))?;
    let glyph_count = font
        .maxp()
        .map_err(|e| format!("Invalid glyph count: {e}"))?
        .num_glyphs();
    let mut budget = MAX_GLYPH_SEGMENTS;
    let mut stack = Vec::new();
    validate_glyf(
        glyf.as_bytes(),
        loca.as_bytes(),
        head.index_to_loc_format() != 0,
        u32::from(glyph_count),
        glyph,
        &mut budget,
        &mut stack,
    )
}
fn validate_glyf(
    glyf: &[u8],
    loca: &[u8],
    long: bool,
    count: u32,
    glyph: u32,
    budget: &mut usize,
    stack: &mut Vec<u32>,
) -> Result<(), String> {
    if glyph >= count {
        return Err("Glyph outline references an invalid glyph identifier.".into());
    }
    if *budget == 0 || stack.len() >= 64 || stack.contains(&glyph) {
        return Err("Glyph outline contains a cycle or exceeds the traversal budget.".into());
    }
    *budget -= 1;
    stack.push(glyph);
    let index = glyph as usize;
    let start = if long {
        be32(loca, index * 4)? as usize
    } else {
        usize::from(be16(loca, index * 2)?) * 2
    };
    let end = if long {
        be32(loca, (index + 1) * 4)? as usize
    } else {
        usize::from(be16(loca, (index + 1) * 2)?) * 2
    };
    let data = glyf
        .get(start..end)
        .ok_or("Invalid TrueType glyph location.")?;
    if !data.is_empty() && (be16(data, 0)? as i16) < 0 {
        let mut p = 10;
        loop {
            let flags = be16(data, p)?;
            let child = u32::from(be16(data, p + 2)?);
            validate_glyf(glyf, loca, long, count, child, budget, stack)?;
            p += 4 + if flags & 1 != 0 { 4 } else { 2 };
            p += if flags & 8 != 0 {
                2
            } else if flags & 0x40 != 0 {
                4
            } else if flags & 0x80 != 0 {
                8
            } else {
                0
            };
            if p > data.len() {
                return Err("Truncated TrueType composite glyph.".into());
            }
            if flags & 0x20 == 0 {
                break;
            }
        }
    }
    stack.pop();
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    fn snapshot() -> LpLayout {
        LpLayout {
            glyphs: vec![],
            paths: vec![],
            paints: vec![],
            stops: vec![],
            decorations: vec![],
            width: 0.0,
            height: 0.0,
            line_count: 0,
        }
    }
    #[test]
    fn true_type_color_base_retains_its_outline() {
        let data = include_bytes!(
            "../../../tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/ColorFontTest.ttf"
        );
        let font = FontRef::new(data).unwrap();
        let mut paths = Vec::new();
        append_outline(&font, GlyphId::new(154), &[], &mut paths).unwrap();
        assert!(paths.iter().any(|p| p.kind == 1));
        assert!(paths.iter().any(|p| p.x0 == 1000.0 || p.y0 == 1000.0));
    }
    #[test]
    fn all_fixture_formats_emit_finite_events() {
        let data = include_bytes!(
            "../../../tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/ColrVectorFormats.ttf"
        );
        let font = FontRef::new(data).unwrap();
        for codepoint in 0xe001u32..=0xe020u32 {
            let glyph = font.charmap().map(codepoint).unwrap().to_u32();
            let mut output = snapshot();
            let location = color_location(&font, &[]);
            let (_, _, _, count, color) =
                emit_glyph(&font, glyph, &[], &location, 0, &mut output).unwrap();
            assert!(color, "U+{codepoint:X}");
            assert!(count > 0, "U+{codepoint:X}");
        }
    }
    #[test]
    fn invalid_color_roots_cannot_fall_back_to_monochrome() {
        let mut data = include_bytes!(
            "../../../tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/ColrVectorFormats.ttf"
        )
        .to_vec();
        let colr = font_table_offset(&data, b"COLR");
        let base = colr + be32(&data, colr + 14).unwrap() as usize;
        let paint = base + be32(&data, base + 6).unwrap() as usize;
        data[paint] = 255;
        let font = FontRef::new(&data).unwrap();
        validate_color_metadata(&font).unwrap();
        let location = color_location(&font, &[]);
        let error = emit_glyph(&font, 2, &[], &location, 0, &mut snapshot()).unwrap_err();
        assert!(error.contains("Unknown COLR paint format"));
    }
    #[test]
    fn truncated_color_headers_are_rejected_before_glyph_lookup() {
        let mut data = include_bytes!(
            "../../../tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/ColorFontTest.ttf"
        )
        .to_vec();
        let count = be16(&data, 4).unwrap() as usize;
        for i in 0..count {
            let record = 12 + i * 16;
            if &data[record..record + 4] == b"COLR" {
                data[record + 12..record + 16].copy_from_slice(&20u32.to_be_bytes());
                break;
            }
        }
        let font = FontRef::new(&data).unwrap();
        assert!(validate_color_metadata(&font).is_err());
        assert!(emit_glyph(&font, 154, &[], &[], 0, &mut snapshot()).is_err());
    }

    fn font_table_offset(data: &[u8], tag: &[u8; 4]) -> usize {
        let count = be16(data, 4).unwrap() as usize;
        (0..count)
            .find_map(|i| {
                let record = 12 + i * 16;
                (&data[record..record + 4] == tag).then(|| be32(data, record + 8).unwrap() as usize)
            })
            .unwrap()
    }
    #[test]
    fn expanded_shared_layers_are_bounded() {
        let mut data = vec![0u8; 2240];
        data[16..20].copy_from_slice(&510u32.to_be_bytes());
        for i in 0..510 {
            let child = if i < 255 { 2210usize } else { 2220usize };
            data[20 + i * 4..24 + i * 4].copy_from_slice(&((child - 16) as u32).to_be_bytes());
        }
        data[2200] = 1;
        data[2201] = 255;
        data[2210] = 1;
        data[2211] = 255;
        data[2212..2216].copy_from_slice(&255u32.to_be_bytes());
        data[2220] = 2;
        let mut budget = MAX_PAINT_VISITS;
        let error = validate_paint(&data, 0, 16, 2200, &mut budget, &mut vec![]).unwrap_err();
        assert!(error.contains("traversal budget"));
        assert_eq!(budget, 0);
    }
    #[test]
    fn cyclic_composite_outline_is_rejected_before_drawing() {
        let mut glyf = vec![0u8; 16];
        glyf[0..2].copy_from_slice(&(-1i16).to_be_bytes());
        let loca = [0u8, 0, 0, 8];
        let mut budget = MAX_GLYPH_SEGMENTS;
        let error = validate_glyf(&glyf, &loca, false, 1, 0, &mut budget, &mut vec![]).unwrap_err();
        assert!(error.contains("cycle"));
    }
}
