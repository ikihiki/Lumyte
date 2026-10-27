# Parley build orchestration tests

These tests run the real Fallout-based .NET entry point with controlled Cargo,
Rustc and Python processes. The fixture tools are .NET apphosts, so testing the
orchestration does not require Python, Rust or a shell script.

```sh
dotnet test tests/Tools/Lumyte.Text.Parley.Build.Tests
```

The runner and fake tools are built once as project references. Each test uses
an isolated directory whose paths contain spaces and Japanese characters, and
passes child-process settings without changing the test process's environment.
The helper projects follow the selected solution configuration even though they
are not solution entries. Their outputs are staged during build, so the CI's
separate `dotnet test --no-build --no-restore` invocation can run the same tests.

Coverage includes argument boundaries, inherited environment variables, native
builds with no Python on PATH, Cargo failures, missing outputs, exact toolchain
notice copies, Emscripten paths, encoded Rust flags and interpreter isolation.
Invalid SDK settings must fail before starting compiler or interpreter tools.

These tests verify orchestration contracts, not font rendering or downloaded
toolchain contents. The actual native and Browser WASM integration is covered by
the [Parley text tests](../../Graphics/Lumyte.Graphics.Text.Parley.Tests/README.md)
and [Browser sample](../../../samples/Lumyte.Graphics.Text.Parley.Browser.Sample/README.md).
