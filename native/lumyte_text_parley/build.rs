fn main() {
    println!("cargo:rerun-if-changed=src/lib.rs");
    println!("cargo:rerun-if-changed=build.rs");
    let target = std::path::Path::new(
        "../../src/Graphics/Lumyte.Graphics.Text.Parley/Interop/NativeMethods.g.cs",
    );
    std::fs::create_dir_all(target.parent().unwrap()).unwrap();
    // Native and browser builds share this checked-in ABI file. Generate in
    // Cargo's separate OUT_DIR and replace only when its content changes.
    let generated =
        std::path::PathBuf::from(std::env::var_os("OUT_DIR").unwrap()).join("NativeMethods.g.cs");
    csbindgen::Builder::default()
        .input_extern_file("src/lib.rs")
        .csharp_namespace("Lumyte.Graphics.Text.Parley.Interop")
        .csharp_class_name("NativeMethods")
        .csharp_dll_name("lumyte_text_parley")
        .csharp_dll_name_if("BROWSER", "liblumyte_text_parley")
        .generate_csharp_file(&generated)
        .unwrap();
    let generated_text = std::fs::read_to_string(&generated).unwrap();
    let contents = generated_text
        .lines()
        .map(str::trim_end)
        .collect::<Vec<_>>()
        .join("\n")
        + "\n";
    let contents = contents.into_bytes();
    if std::fs::read(target).ok().as_deref() != Some(contents.as_slice()) {
        let temporary = target.with_extension(format!("cs.{}.tmp", std::process::id()));
        std::fs::write(&temporary, contents).unwrap();
        std::fs::rename(temporary, target).unwrap();
    }
}
