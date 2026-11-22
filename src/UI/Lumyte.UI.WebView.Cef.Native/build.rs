fn main() {
    // The optional executable is staged with the CEF resources. Never require
    // LD_LIBRARY_PATH mutation of the managed host or desktop session.
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("linux") {
        println!("cargo:rustc-link-arg-bin=lumyte-cef-helper=-Wl,-rpath,$ORIGIN");
    }
}
