//! Strict browser entrypoint; CEF child switches are accepted only with propagated intent.
pub const ALLOW_UNSANDBOXED: &str = "--lumyte-allow-unsandboxed";
#[derive(Debug, PartialEq)]
pub enum Mode {
    Browser { allow_unsandboxed: bool },
    Child,
}
pub fn parse(args: &[String]) -> Result<Mode, &'static str> {
    let allow = args.iter().any(|s| s == ALLOW_UNSANDBOXED);
    let child = args.iter().any(|s| s.starts_with("--type=") && s.len() > 7);
    if child {
        // CEF adds --no-sandbox itself. Only the opted-in browser propagates our
        // dedicated marker in OnBeforeChildProcessLaunch (including zygotes).
        if args.iter().any(|s| {
            s.starts_with("--remote-debugging")
                || s.starts_with("--disable-web-security")
                || s.starts_with("--ignore-certificate-errors")
                || (!allow
                    && (s.starts_with("--no-sandbox")
                        || s.starts_with("--disable-setuid-sandbox")
                        || s.starts_with("--disable-namespace-sandbox")))
        }) {
            return Err("unsafe-child-switch");
        }
        return Ok(Mode::Child);
    }
    if args == ["--lumyte-helper"] {
        Ok(Mode::Browser {
            allow_unsandboxed: false,
        })
    } else if args == ["--lumyte-helper", ALLOW_UNSANDBOXED] {
        Ok(Mode::Browser {
            allow_unsandboxed: true,
        })
    } else {
        Err("expected --lumyte-helper [--lumyte-allow-unsandboxed]")
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    fn mode(a: &[&str]) -> Result<Mode, &'static str> {
        parse(&a.iter().map(|s| s.to_string()).collect::<Vec<_>>())
    }
    #[test]
    fn default_and_explicit_only() {
        assert_eq!(
            mode(&["--lumyte-helper"]),
            Ok(Mode::Browser {
                allow_unsandboxed: false
            })
        );
        assert_eq!(
            mode(&["--lumyte-helper", ALLOW_UNSANDBOXED]),
            Ok(Mode::Browser {
                allow_unsandboxed: true
            })
        );
        for arg in [
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-namespace-sandbox",
            "--remote-debugging-port=9222",
            "--lumyte-allow-unsandboxed=false",
        ] {
            assert!(mode(&["--lumyte-helper", arg]).is_err());
            assert!(mode(&["--lumyte-helper", ALLOW_UNSANDBOXED, arg]).is_err());
        }
        assert!(mode(&[ALLOW_UNSANDBOXED]).is_err());
    }
    #[test]
    fn cef_child_requires_opt_in_for_no_sandbox() {
        assert_eq!(mode(&["--type=renderer"]), Ok(Mode::Child));
        assert!(mode(&["--type=renderer", "--no-sandbox"]).is_err());
        assert_eq!(
            mode(&["--type=renderer", "--no-sandbox", ALLOW_UNSANDBOXED]),
            Ok(Mode::Child)
        );
        assert_eq!(
            mode(&["--type=zygote", "--no-sandbox", ALLOW_UNSANDBOXED]),
            Ok(Mode::Child)
        );
        assert!(
            mode(&[
                "--type=renderer",
                ALLOW_UNSANDBOXED,
                "--remote-debugging-port=9222"
            ])
            .is_err()
        );
    }
}
