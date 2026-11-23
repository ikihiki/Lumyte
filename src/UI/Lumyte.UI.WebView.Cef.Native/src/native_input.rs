use cef::*;
use std::collections::BTreeMap;
#[derive(Default)]
pub struct Input {
    keys: BTreeMap<i32, KeyEvent>,
    primary: bool,
    modifiers: u32,
    mouse: MouseEvent,
}
pub fn u32_at(b: &[u8], n: usize) -> u32 {
    u32::from_le_bytes(b[n..n + 4].try_into().unwrap())
}
pub fn i64_at(b: &[u8], n: usize) -> i64 {
    i64::from_le_bytes(b[n..n + 8].try_into().unwrap())
}
impl Input {
    pub fn release(&mut self, h: &BrowserHost) {
        for (_, mut k) in std::mem::take(&mut self.keys) {
            k.type_ = KeyEventType::KEYUP;
            k.modifiers = 0;
            h.send_key_event(Some(&k));
        }
        if self.primary {
            self.mouse.modifiers = 0;
            h.send_mouse_click_event(Some(&self.mouse), MouseButtonType::LEFT, 1, 1);
            self.primary = false;
        }
        self.modifiers = 0;
        h.set_focus(0);
    }
    pub fn apply(&mut self, h: &BrowserHost, b: &[u8], epoch: i64) -> Result<(), &'static str> {
        if b.len() >= 48 && u32_at(b, 0) == 11 {
            let units = committed_units(b)?;
            if i64_at(b, 40) != epoch {
                self.release(h);
                return Ok(());
            }
            for ch in units {
                h.send_key_event(Some(&KeyEvent {
                    type_: KeyEventType::CHAR,
                    windows_key_code: ch as i32,
                    character: ch,
                    unmodified_character: ch,
                    ..Default::default()
                }));
            }
            return Ok(());
        }
        if b.len() != 48 {
            return Err("invalid-input-size");
        }
        let t = u32_at(b, 0);
        if !(1..=10).contains(&t) || u32_at(b, 36) & !3 != 0 || u32_at(b, 4) & !0xff9f != 0 {
            return Err("invalid-input-fields");
        }
        if t != 10 && i64_at(b, 40) != epoch {
            self.release(h);
            return Ok(());
        }
        if t == 6 || t == 7 {
            self.modifiers = u32_at(b, 4) & (2 | 4 | 8 | 128);
        }
        let m = MouseEvent {
            x: u32_at(b, 8) as i32,
            y: u32_at(b, 12) as i32,
            modifiers: u32_at(b, 4) | self.modifiers | if self.primary { 16 } else { 0 },
        };
        match t {
            1 | 2 => {
                self.mouse = m.clone();
                h.send_mouse_move_event(Some(&m), i32::from(t == 2));
            }
            3 | 4 => {
                self.mouse = m.clone();
                self.primary = t == 3;
                h.send_mouse_click_event(Some(&m), MouseButtonType::LEFT, i32::from(t == 4), 1);
            }
            5 => h.send_mouse_wheel_event(Some(&m), u32_at(b, 16) as i32, u32_at(b, 20) as i32),
            6..=8 => {
                let vk = u32_at(b, 24) as i32;
                let ch = u32_at(b, 32);
                if ch > 65535 || !(0..=255).contains(&vk) {
                    return Err("invalid-key");
                }
                let k = KeyEvent {
                    type_: if t == 6 {
                        KeyEventType::RAWKEYDOWN
                    } else if t == 7 {
                        KeyEventType::KEYUP
                    } else {
                        KeyEventType::CHAR
                    },
                    modifiers: m.modifiers | if u32_at(b, 36) & 2 != 0 { 8192 } else { 0 },
                    windows_key_code: if t == 8 { ch as i32 } else { vk },
                    native_key_code: if t == 8 { 0 } else { u32_at(b, 28) as i32 },
                    is_system_key: (u32_at(b, 36) & 1) as i32,
                    character: if t == 8 { ch as u16 } else { 0 },
                    unmodified_character: if t == 8 { ch as u16 } else { 0 },
                    ..Default::default()
                };
                if t == 6 {
                    if self.keys.len() >= 256 && !self.keys.contains_key(&(u32_at(b, 28) as i32)) {
                        return Err("too-many-keys");
                    }
                    self.keys.insert(k.native_key_code, k.clone());
                } else if t == 7 {
                    self.keys.remove(&k.native_key_code);
                }
                h.send_key_event(Some(&k));
            }
            9 => h.set_focus(1),
            10 => self.release(h),
            _ => unreachable!(),
        }
        Ok(())
    }
}
fn committed_units(b: &[u8]) -> Result<Vec<u16>, &'static str> {
    if b.len() <= 48 || b.len() > 3120 || b[4..40].iter().any(|v| *v != 0) {
        return Err("invalid-committed-text");
    }
    let text = std::str::from_utf8(&b[48..]).map_err(|_| "invalid-committed-utf8")?;
    let units: Vec<u16> = text.encode_utf16().collect();
    if units.len() > 1024 {
        return Err("committed-text-too-large");
    }
    Ok(units)
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn committed_unicode_is_one_validated_batch() {
        let text = "日本語😀".repeat(32);
        let mut body = vec![0; 48];
        body[..4].copy_from_slice(&11u32.to_le_bytes());
        body.extend(text.as_bytes());
        assert_eq!(
            committed_units(&body).unwrap(),
            text.encode_utf16().collect::<Vec<_>>()
        );
        let mut invalid = body.clone();
        invalid.push(255);
        assert!(committed_units(&invalid).is_err());
        let mut oversized = vec![0; 48];
        oversized.extend("a".repeat(1025).as_bytes());
        assert!(committed_units(&oversized).is_err());
    }
    #[test]
    fn fixed_field_decode() {
        let mut b = [0u8; 48];
        b[40..].copy_from_slice(&123i64.to_le_bytes());
        assert_eq!(i64_at(&b, 40), 123);
        assert_eq!(u32_at(&b, 0), 0);
    }
}
