# Repository build orchestration tests

These tests run the real Fallout-based .NET entry point with controlled dotnet,
Cargo, Rustc and Python processes. The fixture tools are .NET apphosts, so testing the
orchestration does not require Python, Rust or a shell script.

```sh
dotnet test tests/Tools/Lumyte.Build.Tests
```

The fake tools are built once as project references. Each test uses
an isolated directory whose paths contain spaces and Japanese characters, and
passes child-process settings without changing the test process's environment.
The fixture follows the selected solution configuration even though it is not
a solution entry. An executing build supplies its assembly through
`LUMYTE_BUILD_ASSEMBLY`; standalone test builds compile a separate runner under
`artifacts/build-tests`. This prevents a solution build from overwriting its
own running executable or selecting a runner from another configuration.
Outputs are staged during build, so the CI's
separate `dotnet test --no-build --no-restore` invocation can run the same tests.

Coverage includes restore/build/test ordering, configuration and project selection,
build failures, no-build/no-restore reuse, serialized publication, package discovery
and opt-out, publish/pack arguments, inherited
environment variables, and the current runner's assembly handoff. Parley coverage
includes argument boundaries, native
builds with no Python on PATH, Cargo failures, missing outputs, exact toolchain
notice copies, Emscripten paths, encoded Rust flags and interpreter isolation.
Nested native invocations keep Fallout state in a separate root while reading
the actual repository's Cargo manifest.
Invalid SDK settings must fail before starting compiler or interpreter tools.

These tests verify orchestration contracts, not font rendering or downloaded
toolchain contents. The actual native and Browser WASM integration is covered by
the [Parley text tests](../../Graphics/Lumyte.Graphics.Text.Parley.Tests/README.md)
and [Browser sample](../../../samples/Lumyte.Graphics.Text.Parley.Browser.Sample/README.md).
