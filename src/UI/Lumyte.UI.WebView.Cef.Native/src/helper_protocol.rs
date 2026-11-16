//! Internal helper protocol. No user-input injection capability is exposed.
use std::io::{self, Read, Write};
pub const MAX: usize = 16_777_280;
pub const HEADER: usize = 40;
#[derive(Debug, Clone, PartialEq)]
pub struct Packet {
    pub kind: u32,
    pub generation: i64,
    pub sequence: i64,
    pub width: i32,
    pub height: i32,
    pub body: Vec<u8>,
}
impl Packet {
    pub fn read(reader: &mut impl Read) -> io::Result<Option<Self>> {
        let mut prefix = [0; 4];
        match reader.read(&mut prefix[..1])? {
            0 => return Ok(None),
            _ => reader.read_exact(&mut prefix[1..])?,
        }
        let length = u32::from_le_bytes(prefix) as usize;
        if !(HEADER..=MAX).contains(&length) {
            return Err(io::Error::other("invalid packet length"));
        }
        let mut h = [0; HEADER];
        reader.read_exact(&mut h)?;
        let u32_at = |i| u32::from_le_bytes(h[i..i + 4].try_into().unwrap());
        let i64_at = |i| i64::from_le_bytes(h[i..i + 8].try_into().unwrap());
        if u64::from_le_bytes(h[4..12].try_into().unwrap()) != 1
            || u32_at(36) as usize != length - HEADER
        {
            return Err(io::Error::other("invalid packet header"));
        }
        // Commands never contain image data: do not allocate an attacker-declared frame-sized command.
        if length > HEADER + 4096 {
            return Err(io::Error::other("command body too large"));
        }
        let mut body = vec![0; length - HEADER];
        reader.read_exact(&mut body)?;
        Ok(Some(Self {
            kind: u32_at(0),
            generation: i64_at(12),
            sequence: i64_at(20),
            width: u32_at(28) as i32,
            height: u32_at(32) as i32,
            body,
        }))
    }
    pub fn write(&self, writer: &mut impl Write) -> io::Result<()> {
        if self.body.len() > MAX - HEADER {
            return Err(io::Error::other("output too large"));
        }
        writer.write_all(&((HEADER + self.body.len()) as u32).to_le_bytes())?;
        writer.write_all(&self.kind.to_le_bytes())?;
        writer.write_all(&1u64.to_le_bytes())?;
        writer.write_all(&self.generation.to_le_bytes())?;
        writer.write_all(&self.sequence.to_le_bytes())?;
        writer.write_all(&self.width.to_le_bytes())?;
        writer.write_all(&self.height.to_le_bytes())?;
        writer.write_all(&(self.body.len() as u32).to_le_bytes())?;
        writer.write_all(&self.body)?;
        writer.flush()
    }
}
pub fn geometry(w: i32, h: i32) -> bool {
    w > 0 && h > 0 && w <= 4096 && h <= 4096 && (w as usize) * (h as usize) * 4 <= MAX - HEADER
}
pub fn http_url(bytes: &[u8]) -> Option<&str> {
    let s = std::str::from_utf8(bytes).ok()?;
    if s.is_empty() || s.len() > 4096 || s.chars().any(|c| c.is_control() || c.is_whitespace()) {
        return None;
    }
    let rest = s
        .strip_prefix("https://")
        .or_else(|| s.strip_prefix("http://"))?;
    if rest.split('/').next()?.is_empty() {
        None
    } else {
        Some(s)
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn roundtrip() {
        let p = Packet {
            kind: 1,
            generation: 7,
            sequence: 9,
            width: 80,
            height: 60,
            body: b"https://example.com/".to_vec(),
        };
        let mut v = vec![];
        p.write(&mut v).unwrap();
        assert_eq!(v.len(), 44 + p.body.len());
        assert_eq!(Packet::read(&mut &v[..]).unwrap(), Some(p));
    }
    #[test]
    fn malformed_bounded() {
        for n in [0, 39, MAX as u32 + 1, u32::MAX] {
            assert!(Packet::read(&mut &n.to_le_bytes()[..]).is_err());
        }
        assert!(Packet::read(&mut &[][..]).unwrap().is_none());
    }
    #[test]
    fn viewport_bound() {
        assert!(geometry(2048, 2048));
        assert!(!geometry(4096, 4096));
        assert!(!geometry(-1, 64));
        assert!(http_url(b"file:///tmp/a").is_none());
        assert!(http_url(b"https://example.com/").is_some());
    }
}
