use crate::*;
use parlance::Tag;
use parley::{
    Alignment, AlignmentOptions, BaseDirection, FontFamily, FontFamilyName, FontFeature,
    FontFeatures, FontStyle, FontVariation, FontVariations, FontWeight, Layout, LineHeight,
    PositionedLayoutItem, StyleProperty,
};
use skrifa::{instance::NormalizedCoord, raw::FontRef, MetadataProvider};
use std::{
    borrow::Cow,
    collections::{BTreeMap, BTreeSet, HashMap},
    ops::Range,
    sync::Arc,
};

const MAX_FONT_BYTES: usize = 64 * 1024 * 1024;
const MAX_TEXT_BYTES: usize = 1024 * 1024;
const MAX_GLYPHS: usize = 262_144;

pub fn add_font(engine: &mut LpEngine, bytes: &[u8], output: &mut u32) -> Result<(), String> {
    if bytes.is_empty() || bytes.len() > MAX_FONT_BYTES {
        return Err("Font data must contain 1 to 67108864 bytes.".into());
    }
    if engine.fonts.len() >= 1024 {
        return Err("The font registration limit was exceeded.".into());
    }
    let face = FontRef::from_index(bytes, 0).map_err(|_| "Invalid OpenType or TrueType font.")?;
    let metrics = face.metrics(
        skrifa::instance::Size::unscaled(),
        skrifa::instance::LocationRef::default(),
    );
    if metrics.units_per_em == 0 {
        return Err("The font has an invalid em size.".into());
    }
    let data = parley::fontique::Blob::new(Arc::new(bytes.to_vec()));
    let blob_id = data.id();
    let families = engine.font_context.collection.register_fonts(data, None);
    let family = families.first().ok_or("No usable font face was found.")?;
    if family.1.is_empty() {
        return Err("No usable font face was found.".into());
    }
    let name = engine
        .font_context
        .collection
        .family_name(family.0)
        .ok_or("The font has no usable family name.")?
        .to_string();
    let index = family.1[0].index();
    *output = engine.fonts.len() as u32;
    engine.fonts.push(RegisteredFont {
        name,
        blob_id,
        index,
    });
    Ok(())
}

fn families(
    engine: &LpEngine,
    primary: u32,
    fallback_ids: &[u32],
) -> Result<FontFamily<'static>, String> {
    let mut names = Vec::with_capacity(fallback_ids.len() + 1);
    for id in std::iter::once(&primary).chain(fallback_ids) {
        let font = engine
            .fonts
            .get(*id as usize)
            .ok_or("Unknown font identifier.")?;
        names.push(FontFamilyName::Named(Cow::Owned(font.name.clone())));
    }
    Ok(FontFamily::List(Cow::Owned(names)))
}

// Overline is not a native Parley style property. Carry it together with the
// caller's paint in an internal brush token so changes to either property also
// split Parley's positioned glyph runs. Tokens never cross the public ABI.
#[derive(Clone, Copy, PartialEq, Eq, Hash)]
struct BrushState {
    paint: u32,
    overline: bool,
}

fn brush_runs(
    text_len: usize,
    default_overline: bool,
    spans: &[LpSpan],
) -> (Vec<BrushState>, Vec<(Range<usize>, u32)>) {
    let default = BrushState {
        paint: 0,
        overline: default_overline,
    };
    let mut states = vec![default];
    let mut tokens = HashMap::from([(default, 0)]);
    let mut boundaries = BTreeMap::<usize, Vec<(usize, bool)>>::new();
    boundaries.entry(0).or_default();
    boundaries.entry(text_len).or_default();
    for (index, span) in spans.iter().enumerate() {
        if span.start < span.end && (span.brush_index != u32::MAX || span.decorations != u32::MAX) {
            boundaries
                .entry(span.start as usize)
                .or_default()
                .push((index, true));
            boundaries
                .entry(span.end as usize)
                .or_default()
                .push((index, false));
        }
    }
    let mut active_paints = BTreeSet::new();
    let mut active_overlines = BTreeSet::new();
    let mut runs: Vec<(Range<usize>, u32)> = Vec::new();
    let mut boundaries = boundaries.into_iter().peekable();
    while let Some((start, events)) = boundaries.next() {
        for (index, entering) in events {
            let span = &spans[index];
            for (explicit, active) in [
                (span.brush_index != u32::MAX, &mut active_paints),
                (span.decorations != u32::MAX, &mut active_overlines),
            ] {
                if explicit {
                    if entering {
                        active.insert(index);
                    } else {
                        active.remove(&index);
                    }
                }
            }
        }
        let Some((end, _)) = boundaries.peek() else {
            break;
        };
        if start == *end {
            continue;
        }
        let state = BrushState {
            paint: active_paints
                .last()
                .map(|index| spans[*index].brush_index)
                .unwrap_or(0),
            overline: active_overlines
                .last()
                .map(|index| spans[*index].decorations & 4 != 0)
                .unwrap_or(default_overline),
        };
        let token = *tokens.entry(state).or_insert_with(|| {
            let token = states.len() as u32;
            states.push(state);
            token
        });
        if let Some((range, previous_token)) = runs.last_mut() {
            if *previous_token == token && range.end == start {
                range.end = *end;
                continue;
            }
        }
        runs.push((start..*end, token));
    }
    (states, runs)
}

pub fn create(
    engine: &mut LpEngine,
    text: &str,
    options: &LpOptions,
    spans: &[LpSpan],
) -> Result<LpLayout, String> {
    if text.len() > MAX_TEXT_BYTES {
        return Err("The paragraph exceeds the 1048576-byte input limit.".into());
    }
    if spans.len() > 4096 {
        return Err("The style-span limit was exceeded.".into());
    }
    if !options.font_size.is_finite()
        || options.font_size <= 0.0
        || !options.line_height.is_finite()
        || options.line_height <= 0.0
        || !options.max_width.is_finite()
    {
        return Err("Font size, line height and width must be finite; size and line height must be positive.".into());
    }
    if options.fallback_font_count > 1024
        || options.feature_count > 256
        || options.variation_count > 256
    {
        return Err("The font option limit was exceeded.".into());
    }
    let fallback_ids =
        unsafe { input_slice(options.fallback_font_ids, options.fallback_font_count)? };
    let font_families = families(engine, options.font_id, fallback_ids)?;
    let mut features = Vec::new();
    for setting in unsafe { input_slice(options.features, options.feature_count)? } {
        if setting.value > u16::MAX as u32 {
            return Err("OpenType feature values must fit into 16 bits.".into());
        }
        features.push(FontFeature::new(
            Tag::from_bytes(setting.tag.to_be_bytes()),
            setting.value as u16,
        ));
    }
    let mut variations = Vec::new();
    for setting in unsafe { input_slice(options.variations, options.variation_count)? } {
        if !setting.value.is_finite() {
            return Err("Variation values must be finite.".into());
        }
        variations.push(FontVariation::new(
            Tag::from_bytes(setting.tag.to_be_bytes()),
            setting.value,
        ));
    }
    let locale = if options.locale_len == 0 {
        None
    } else {
        if options.locale_len > 256 {
            return Err("Locale tag is too long.".into());
        }
        let bytes = unsafe { input_slice(options.locale, options.locale_len)? };
        let name = std::str::from_utf8(bytes).map_err(|_| "Locale must be valid UTF-8.")?;
        Some(parlance::Language::parse(name).map_err(|_| "Invalid BCP 47 locale tag.")?)
    };
    let direction = match options.direction {
        0 => BaseDirection::Auto,
        1 => BaseDirection::Ltr,
        2 => BaseDirection::Rtl,
        _ => return Err("Invalid base direction.".into()),
    };
    let alignment = match options.alignment {
        0 => Alignment::Start,
        1 => Alignment::End,
        2 => Alignment::Center,
        3 => Alignment::Justify,
        _ => return Err("Invalid alignment.".into()),
    };
    if options.decorations & !7 != 0 {
        return Err("Unknown decoration flags.".into());
    }
    // Resolve names before borrowing the context for the ranged builder.
    let mut span_families = Vec::with_capacity(spans.len());
    for span in spans {
        let range = span.start as usize..span.end as usize;
        if range.start > range.end
            || range.end > text.len()
            || !text.is_char_boundary(range.start)
            || !text.is_char_boundary(range.end)
        {
            return Err("Style spans must be valid UTF-8 byte ranges.".into());
        }
        if !span.font_size.is_finite()
            || span.font_size < 0.0
            || !span.weight.is_finite()
            || span.weight < 0.0
            || span.weight > 1000.0
            || span.italic > 2
            || (span.decorations != u32::MAX && span.decorations & !7 != 0)
        {
            return Err("Invalid style span options.".into());
        }
        span_families.push(if span.font_id == u32::MAX {
            None
        } else {
            Some(families(engine, span.font_id, fallback_ids)?)
        });
    }
    let (brush_states, brush_runs) = brush_runs(text.len(), options.decorations & 4 != 0, spans);
    let mut builder =
        engine
            .layout_context
            .ranged_builder(&mut engine.font_context, text, 1.0, false);
    builder.set_base_direction(direction);
    builder.push_default(font_families);
    builder.push_default(StyleProperty::FontSize(options.font_size));
    builder.push_default(StyleProperty::LineHeight(LineHeight::FontSizeRelative(
        options.line_height,
    )));
    builder.push_default(StyleProperty::Brush(0));
    builder.push_default(StyleProperty::Locale(locale));
    builder.push_default(StyleProperty::FontFeatures(FontFeatures::from(
        features.as_slice(),
    )));
    builder.push_default(StyleProperty::FontVariations(FontVariations::from(
        variations.as_slice(),
    )));
    builder.push_default(StyleProperty::Underline(options.decorations & 1 != 0));
    builder.push_default(StyleProperty::Strikethrough(options.decorations & 2 != 0));
    for (span, family) in spans.iter().zip(span_families) {
        let range = span.start as usize..span.end as usize;
        if let Some(family) = family {
            builder.push(StyleProperty::FontFamily(family), range.clone());
        }
        if span.font_size > 0.0 {
            builder.push(StyleProperty::FontSize(span.font_size), range.clone());
        }
        if span.weight > 0.0 {
            builder.push(
                StyleProperty::FontWeight(FontWeight::new(span.weight)),
                range.clone(),
            );
        }
        if span.italic != 2 {
            builder.push(
                StyleProperty::FontStyle(if span.italic == 1 {
                    FontStyle::Italic
                } else {
                    FontStyle::Normal
                }),
                range.clone(),
            );
        }
        if span.decorations != u32::MAX {
            builder.push(
                StyleProperty::Underline(span.decorations & 1 != 0),
                range.clone(),
            );
            builder.push(
                StyleProperty::Strikethrough(span.decorations & 2 != 0),
                range,
            );
        }
    }
    for (range, token) in brush_runs {
        builder.push(StyleProperty::Brush(token), range);
    }
    let mut layout: Layout<u32> = builder.build(text);
    layout.break_all_lines((options.max_width > 0.0).then_some(options.max_width));
    layout.align(alignment, AlignmentOptions::default());
    let mut output = LpLayout {
        glyphs: Vec::new(),
        paths: Vec::new(),
        paints: Vec::new(),
        stops: Vec::new(),
        decorations: Vec::new(),
        width: layout.width(),
        height: layout.height(),
        line_count: layout.len() as u32,
    };
    if !output.width.is_finite() || !output.height.is_finite() {
        return Err("The resulting paragraph dimensions are not finite.".into());
    }
    for line in layout.lines() {
        let mut run_clusters = HashMap::<usize, (usize, Vec<(u32, u32, u32)>)>::new();
        for item in line.items() {
            let PositionedLayoutItem::GlyphRun(glyph_run) = item else {
                continue;
            };
            let run = glyph_run.run();
            let font = run.font();
            let font_id = engine
                .fonts
                .iter()
                .position(|registered| {
                    registered.blob_id == font.data.id() && registered.index == font.index
                })
                .ok_or("The font engine selected a font face not registered by the caller.")?
                as u32;
            let font_ref = FontRef::from_index(font.data.as_ref(), font.index)
                .map_err(|_| "The resolved font face is invalid.")?;
            let normalized_coords: Vec<_> = run
                .normalized_coords()
                .iter()
                .map(|coord| NormalizedCoord::from_bits(coord.to_bits()))
                .collect();
            let upem = font_ref
                .metrics(
                    skrifa::instance::Size::unscaled(),
                    skrifa::instance::LocationRef::new(&normalized_coords),
                )
                .units_per_em;
            if upem == 0 {
                return Err("The resolved font has an invalid em size.".into());
            }
            let (cursor, clusters) = run_clusters.entry(run.index()).or_insert_with(|| {
                let mut ligature_ends = HashMap::new();
                let logical: Vec<_> = run.clusters().collect();
                for (index, cluster) in logical.iter().enumerate() {
                    let mut end = cluster.text_range().end;
                    if cluster.is_ligature_start() {
                        for continuation in logical[index + 1..]
                            .iter()
                            .take_while(|next| next.is_ligature_continuation())
                        {
                            end = end.max(continuation.text_range().end);
                        }
                    }
                    ligature_ends.insert(cluster.text_range().start, end);
                }
                let clusters: Vec<_> = run
                    .visual_clusters()
                    .flat_map(|cluster| {
                        let range = cluster.text_range();
                        let end = *ligature_ends.get(&range.start).unwrap_or(&range.end);
                        cluster
                            .glyphs()
                            .map(move |glyph| (glyph.id, range.start as u32, end as u32))
                    })
                    .collect();
                (0, clusters)
            });
            let style = glyph_run.style();
            let brush_state = brush_states
                .get(style.brush as usize)
                .ok_or("Invalid internal brush token.")?;
            for glyph in glyph_run.positioned_glyphs() {
                if output.glyphs.len() >= MAX_GLYPHS {
                    return Err("The paragraph glyph limit was exceeded.".into());
                }
                let (cluster_glyph, start, end) = clusters
                    .get(*cursor)
                    .copied()
                    .ok_or("Inconsistent glyph and cluster counts.")?;
                *cursor += 1;
                if cluster_glyph != glyph.id {
                    return Err("Inconsistent glyph and cluster ordering.".into());
                }
                // Parley uses glyph zero as a positioning placeholder for a tab.
                // Keep its metadata and advance without drawing .notdef. Other
                // whitespace still uses its real font outline, if it has one.
                let tab = end == start + 1 && text.as_bytes().get(start as usize) == Some(&b'\t');
                let (path_start, path_count, paint_start, paint_count, color) = if tab {
                    (
                        output.paths.len() as u32,
                        0,
                        output.paints.len() as u32,
                        0,
                        false,
                    )
                } else {
                    paint::emit_glyph(
                        &font_ref,
                        glyph.id,
                        &normalized_coords,
                        options.palette_index,
                        &mut output,
                    )?
                };
                output.glyphs.push(LpGlyph {
                    glyph_id: glyph.id,
                    font_id,
                    cluster_start: start,
                    cluster_end: end,
                    brush_index: brush_state.paint,
                    flags: if color { 1 } else { 0 },
                    x: glyph.x,
                    y: glyph.y,
                    advance: glyph.advance,
                    font_size: run.font_size(),
                    scale: run.font_size() / upem as f32,
                    path_start,
                    path_count,
                    paint_start,
                    paint_count,
                });
            }
            let metrics = run.font_metrics();
            if let Some(decoration) = &style.underline {
                output.decorations.push(LpDecoration {
                    brush_index: brush_states
                        .get(decoration.brush as usize)
                        .ok_or("Invalid internal decoration brush token.")?
                        .paint,
                    x: glyph_run.offset(),
                    y: glyph_run.baseline() - decoration.offset.unwrap_or(metrics.underline_offset),
                    width: glyph_run.advance(),
                    height: decoration.size.unwrap_or(metrics.underline_size),
                });
            }
            if let Some(decoration) = &style.strikethrough {
                output.decorations.push(LpDecoration {
                    brush_index: brush_states
                        .get(decoration.brush as usize)
                        .ok_or("Invalid internal decoration brush token.")?
                        .paint,
                    x: glyph_run.offset(),
                    y: glyph_run.baseline()
                        - decoration.offset.unwrap_or(metrics.strikethrough_offset),
                    width: glyph_run.advance(),
                    height: decoration.size.unwrap_or(metrics.strikethrough_size),
                });
            }
            if brush_state.overline {
                output.decorations.push(LpDecoration {
                    brush_index: brush_state.paint,
                    x: glyph_run.offset(),
                    y: glyph_run.baseline() - metrics.ascent,
                    width: glyph_run.advance(),
                    height: metrics.underline_size,
                });
            }
        }
    }
    Ok(output)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn engine() -> LpEngine {
        let mut engine = LpEngine {
            font_context: FontContext {
                collection: parley::fontique::Collection::new(
                    parley::fontique::CollectionOptions {
                        shared: false,
                        system_fonts: false,
                    },
                ),
                source_cache: Default::default(),
            },
            layout_context: LayoutContext::new(),
            fonts: Vec::new(),
        };
        add_font(
            &mut engine,
            include_bytes!(
                "../../../tests/Graphics/Lumyte.Graphics.Text.Tests/Fonts/DejaVuSans.ttf"
            ),
            &mut 0,
        )
        .unwrap();
        engine
    }

    fn options() -> LpOptions {
        LpOptions {
            font_size: 30.0,
            max_width: 0.0,
            line_height: 1.2,
            font_id: 0,
            alignment: 0,
            direction: 0,
            palette_index: 0,
            decorations: 0,
            fallback_font_ids: std::ptr::null(),
            fallback_font_count: 0,
            features: std::ptr::null(),
            feature_count: 0,
            variations: std::ptr::null(),
            variation_count: 0,
            locale: std::ptr::null(),
            locale_len: 0,
        }
    }

    #[test]
    fn ligature_cluster_covers_all_source_graphemes() {
        let mut engine = engine();
        let result = create(&mut engine, "office", &options(), &[]).unwrap();
        assert_eq!(result.glyphs.len(), 4);
        assert_eq!(
            (result.glyphs[1].cluster_start, result.glyphs[1].cluster_end),
            (1, 4)
        );
        assert!(result.glyphs.iter().any(|glyph| glyph.path_count > 0));
    }

    #[test]
    fn bidi_preserves_logical_source_ranges_in_visual_order() {
        let mut engine = engine();
        let result = create(&mut engine, "abc אבג DEF", &options(), &[]).unwrap();
        let hebrew: Vec<_> = result
            .glyphs
            .iter()
            .filter(|glyph| (4..10).contains(&glyph.cluster_start))
            .map(|glyph| glyph.cluster_start)
            .collect();
        assert_eq!(hebrew, [8, 6, 4]);
        assert!(result.glyphs.windows(2).all(|pair| pair[0].x <= pair[1].x));
    }

    #[test]
    fn feature_settings_change_shaping_without_changing_source_ranges() {
        let mut engine = engine();
        let features = [LpFeature {
            tag: u32::from_be_bytes(*b"liga"),
            value: 0,
        }];
        let mut options = options();
        options.features = features.as_ptr();
        options.feature_count = features.len();
        let result = create(&mut engine, "office", &options, &[]).unwrap();
        assert_eq!(result.glyphs.len(), 6);
        assert_eq!(result.glyphs.last().unwrap().cluster_end, 6);
    }

    #[test]
    fn null_brush_span_inherits_overlapping_paint() {
        let mut engine = engine();
        let spans = [
            LpSpan {
                start: 0,
                end: 4,
                font_id: u32::MAX,
                brush_index: 1,
                font_size: 0.0,
                weight: 0.0,
                italic: 2,
                decorations: u32::MAX,
            },
            LpSpan {
                start: 1,
                end: 3,
                font_id: u32::MAX,
                brush_index: u32::MAX,
                font_size: 18.0,
                weight: 0.0,
                italic: 2,
                decorations: u32::MAX,
            },
        ];
        let result = create(&mut engine, "abcd", &options(), &spans).unwrap();
        assert!(result.glyphs.iter().all(|glyph| glyph.brush_index == 1));
        assert_eq!(result.glyphs[1].font_size, 18.0);
    }

    #[test]
    fn invalid_utf8_boundary_and_locale_return_errors() {
        let mut engine = engine();
        let spans = [LpSpan {
            start: 1,
            end: 2,
            font_id: u32::MAX,
            brush_index: 0,
            font_size: 0.0,
            weight: 0.0,
            italic: 2,
            decorations: u32::MAX,
        }];
        assert!(create(&mut engine, "é", &options(), &spans).is_err());
        let mut options = options();
        options.locale = b"invalid_locale_tag".as_ptr();
        options.locale_len = 18;
        assert!(create(&mut engine, "abc", &options, &[]).is_err());
        options.locale = b"ja-JP".as_ptr();
        options.locale_len = 5;
        assert!(create(&mut engine, "abc", &options, &[]).is_ok());
    }
    #[test]
    fn overline_only_span_preserves_its_boundaries() {
        let mut engine = engine();
        let span = LpSpan {
            start: 1,
            end: 2,
            font_id: u32::MAX,
            brush_index: u32::MAX,
            font_size: 0.0,
            weight: 0.0,
            italic: 2,
            decorations: 4,
        };
        let result = create(&mut engine, "ABC", &options(), &[span]).unwrap();
        assert_eq!(result.glyphs.len(), 3);
        assert_eq!(result.decorations.len(), 1);
        assert_eq!(result.decorations[0].x, result.glyphs[1].x);
        assert_eq!(result.decorations[0].width, result.glyphs[1].advance);
        assert!(result.glyphs.iter().all(|glyph| glyph.brush_index == 0));
    }

    #[test]
    fn span_can_disable_inherited_overline_without_changing_paint() {
        let mut engine = engine();
        let mut options = options();
        options.decorations = 4;
        let span = LpSpan {
            start: 1,
            end: 2,
            font_id: u32::MAX,
            brush_index: u32::MAX,
            font_size: 0.0,
            weight: 0.0,
            italic: 2,
            decorations: 0,
        };
        let result = create(&mut engine, "ABC", &options, &[span]).unwrap();
        assert_eq!(result.decorations.len(), 2);
        for (decoration, glyph_index) in result.decorations.iter().zip([0, 2]) {
            assert_eq!(decoration.x, result.glyphs[glyph_index].x);
            assert_eq!(decoration.width, result.glyphs[glyph_index].advance);
            assert_eq!(decoration.brush_index, 0);
        }
    }

    #[test]
    fn overline_and_paint_resolve_overlaps_independently() {
        let mut engine = engine();
        let spans = [
            LpSpan {
                start: 0,
                end: 3,
                font_id: u32::MAX,
                brush_index: 7,
                font_size: 0.0,
                weight: 0.0,
                italic: 2,
                decorations: u32::MAX,
            },
            LpSpan {
                start: 1,
                end: 2,
                font_id: u32::MAX,
                brush_index: u32::MAX,
                font_size: 0.0,
                weight: 0.0,
                italic: 2,
                decorations: 4,
            },
        ];
        let result = create(&mut engine, "ABC", &options(), &spans).unwrap();
        assert_eq!(result.decorations.len(), 1);
        assert_eq!(result.decorations[0].brush_index, 7);
        assert!(result.glyphs.iter().all(|glyph| glyph.brush_index == 7));
    }
    #[test]
    fn tab_keeps_its_advance_without_drawing_notdef() {
        let mut engine = engine();
        let result = create(&mut engine, "ab\tcd", &options(), &[]).unwrap();
        assert_eq!(result.glyphs.len(), 5);
        let tab = &result.glyphs[2];
        assert_eq!((tab.cluster_start, tab.cluster_end), (2, 3));
        assert!(tab.advance > 0.0);
        assert_eq!(tab.path_count, 0);
        assert_eq!(tab.paint_count, 0);
        assert_eq!(result.glyphs[3].x, tab.x + tab.advance);
        assert!(result.glyphs[3].path_count > 0);
    }
}
